using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Ossuary.Advice;

namespace Ossuary.Tools.FetchCodexData;

/// <summary>
/// Harvests the pairwise lift model behind Codex's draft advice.
/// </summary>
/// <remarks>
/// <para>
/// Build-time only, and rare: once per data version, not once per release.
/// </para>
/// <para>
/// <b>Why it is one request per card, and why that cannot be reduced.</b> A
/// request asks "given I hold exactly these cards, what is everything worth",
/// so <c>deck=[h]</c> against every offered id returns a whole row of the
/// matrix — about 500 values from one request. Batching the other axis does not
/// help. In logs, a request with deck subset S returns, for each candidate c,
/// the sum of <c>log lift(h, c)</c> over S: one linear measurement per column,
/// with the same measurement vector shared across every column. Recovering a
/// dense column of N unknowns needs N independent measurements, so N requests
/// is the floor for exact recovery, not an implementation shortcoming.
/// </para>
/// <para>
/// Sparse recovery could in principle beat that bound, since most pairs do not
/// interact. It is not attempted: it would depend on a sparsity that has never
/// been measured, and on summing hundreds of logs without the API's rounding
/// destroying the signal. A one-off half-hour is cheaper than being subtly
/// wrong.
/// </para>
/// <para>
/// So the run is long, and the work went into making a long run survivable:
/// pacing from the limits Codex publishes, honouring <c>Retry-After</c>, and
/// checkpointing after every row so an interrupted harvest resumes instead of
/// starting over.
/// </para>
/// <para>
/// <b>Unverified against a live response.</b> Codex declares no response schema
/// for this endpoint and their API was unreachable when this was written, so the
/// field names are inferred. The parser tries the plausible spellings and prints
/// the raw body when none matches, so the first real run either works or says
/// exactly what to change.
/// </para>
/// </remarks>
internal static class HarvestLifts
{
    /// <summary>
    /// How far a factor must sit from 1 to be worth carrying.
    /// </summary>
    /// <remarks>
    /// A factor of exactly 1 is indistinguishable from no entry, and most pairs
    /// do not interact. Storing only what moves a score is the difference
    /// between a few thousand rows and a quarter of a million.
    /// </remarks>
    private const double Meaningful = 0.005;

    private static readonly string[] ListFields = ["results", "offers", "advice", "cards", "items"];
    private static readonly string[] IdFields = ["id", "card_id", "cardId"];
    private static readonly string[] ScoreFields = ["score", "value", "adjusted_score", "adjustedScore"];

    internal static async Task<int> Run(HttpClient http, string baseUrl, string repoRoot, string tier)
    {
        var outputPath = Path.Combine(repoRoot, "src", "Ossuary", "Data", "lifts.tsv");
        var checkpointDir = Path.Combine(repoRoot, "build", "harvest");
        var partialPath = Path.Combine(checkpointDir, "lifts.partial.tsv");
        var donePath = Path.Combine(checkpointDir, "done.txt");

        var ids = ReadCardIds(repoRoot);
        if (ids.Count == 0)
        {
            Console.Error.WriteLine("no card ids in the bundled table; run the ratings fetch first");
            return 1;
        }

        var limit = await RateLimit.Discover(http, baseUrl, tier);
        Console.WriteLine($"rate limit: {limit}");

        Directory.CreateDirectory(checkpointDir);
        var done = File.Exists(donePath)
            ? File.ReadAllLines(donePath).Where(l => l.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var remaining = ids.Where(id => !done.Contains(id)).ToList();
        if (done.Count > 0)
        {
            Console.WriteLine($"resuming: {done.Count} of {ids.Count} rows already harvested");
        }

        var eta = TimeSpan.FromSeconds(remaining.Count * limit.Delay.TotalSeconds);
        Console.WriteLine($"{remaining.Count} requests to go — about {eta.TotalMinutes:F0} minutes");
        Console.WriteLine("interrupting is safe; rerun to resume\n");

        var baseline = await Advice(http, baseUrl, [], ids, limit);
        Console.WriteLine($"baseline: {baseline.Count} scores\n");

        var started = Stopwatch.StartNew();
        var pairs = 0;

        for (var i = 0; i < remaining.Count; i++)
        {
            var held = remaining[i];
            await Task.Delay(limit.Delay);

            var scored = await Advice(http, baseUrl, [held], ids, limit);
            var rows = new List<string>();

            foreach (var (candidate, score) in scored)
            {
                if (!baseline.TryGetValue(candidate, out var start) || start <= 0) continue;

                // The diagonal is kept on purpose. lift(X, X) is "I already hold
                // one X — how much do I want another", which is a real question
                // and not a degenerate one.
                var lift = score / start;
                if (Math.Abs(lift - LiftTable.Neutral) < Meaningful) continue;

                rows.Add(string.Create(CultureInfo.InvariantCulture, $"{held}\t{candidate}\t{lift:0.####}"));
            }

            // Checkpoint before recording the row as done, so a crash between
            // the two costs a repeated request rather than a missing row.
            if (rows.Count > 0) File.AppendAllLines(partialPath, rows);
            File.AppendAllLines(donePath, [held]);
            pairs += rows.Count;

            if ((i + 1) % 20 == 0 || i == remaining.Count - 1)
            {
                var per = started.Elapsed.TotalSeconds / (i + 1);
                var left = TimeSpan.FromSeconds(per * (remaining.Count - i - 1));
                Console.WriteLine(
                    $"  {i + 1}/{remaining.Count} rows · {pairs:N0} pairs this run · {left.TotalMinutes:F0} min left");
            }
        }

        Finalise(outputPath, partialPath, donePath, ids.Count);
        return 0;
    }

    private static async Task<Dictionary<string, double>> Advice(
        HttpClient http, string baseUrl, IReadOnlyList<string> deck, IReadOnlyList<string> offered, RateLimit limit)
    {
        const int attempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            var payload = JsonSerializer.Serialize(new { deck, offered, lang = "eng" });
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync($"{baseUrl}/api/draft-advice", content);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < attempts)
            {
                // Honour the server's own instruction where it gives one, rather
                // than guessing at a backoff it did not ask for.
                var wait = response.Headers.RetryAfter?.Delta
                           ?? TimeSpan.FromSeconds(Math.Max(limit.Delay.TotalSeconds * 4, Math.Pow(2, attempt)));
                Console.WriteLine($"    rate limited; waiting {wait.TotalSeconds:F0}s (attempt {attempt})");
                await Task.Delay(wait);
                continue;
            }

            if ((int)response.StatusCode >= 500 && attempt < attempts)
            {
                var wait = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                Console.WriteLine($"    HTTP {(int)response.StatusCode}; retrying in {wait.TotalSeconds:F0}s");
                await Task.Delay(wait);
                continue;
            }

            response.EnsureSuccessStatusCode();
            return Parse(await response.Content.ReadAsStringAsync());
        }
    }

    /// <summary>Pulls id/score pairs out of a response whose shape is not documented.</summary>
    private static Dictionary<string, double> Parse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var array = root.ValueKind == JsonValueKind.Array ? root : default;
        if (array.ValueKind != JsonValueKind.Array)
        {
            foreach (var field in ListFields)
            {
                if (root.TryGetProperty(field, out var candidate) && candidate.ValueKind == JsonValueKind.Array)
                {
                    array = candidate;
                    break;
                }
            }
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "could not find the list of scored offers in the draft-advice response. "
                + $"Tried {string.Join(", ", ListFields)}. Body begins: {Trim(body)}");
        }

        var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in array.EnumerateArray())
        {
            var id = First(entry, IdFields)?.GetString();
            var score = First(entry, ScoreFields);
            if (id is null || score is null) continue;
            if (score.Value.TryGetDouble(out var value)) scores[id] = value;
        }

        if (scores.Count == 0)
        {
            throw new InvalidOperationException(
                $"found the list but no id/score pairs. Tried ids {string.Join(", ", IdFields)} and "
                + $"scores {string.Join(", ", ScoreFields)}. Body begins: {Trim(body)}");
        }

        return scores;
    }

    private static JsonElement? First(JsonElement element, string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value)) return value;
        }

        return null;
    }

    private static string Trim(string body) => body.Length <= 400 ? body : body[..400] + "…";

    private static List<string> ReadCardIds(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "src", "Ossuary", "Data", "ratings.tsv");
        if (!File.Exists(path)) return [];

        var ids = new List<string>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith('#') || line.StartsWith("kind\t", StringComparison.Ordinal)) continue;
            var f = line.Split('\t');
            if (f.Length > 3 && f[0] == "cards") ids.Add(f[3]);
        }

        return ids;
    }

    /// <summary>Turns the checkpoint into the bundled table and clears it.</summary>
    private static void Finalise(string outputPath, string partialPath, string donePath, int cards)
    {
        var rows = File.Exists(partialPath) ? File.ReadAllLines(partialPath) : [];
        var sorted = rows
            .Where(r => r.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var sb = new StringBuilder();
        sb.Append("# Ossuary deck-advice lift table — generated by tools/FetchCodexData --harvest-lifts\n");
        sb.Append(CultureInfo.InvariantCulture, $"# cards\t{cards}\n");
        sb.Append(CultureInfo.InvariantCulture, $"# pairs\t{sorted.Count}\n");
        sb.Append("held\tcandidate\tlift\n");
        foreach (var row in sorted) sb.Append(row).Append('\n');

        File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(false));

        File.Delete(partialPath);
        File.Delete(donePath);

        var density = cards > 0 ? sorted.Count / (double)(cards * cards) : 0;
        Console.WriteLine($"\nwrote {sorted.Count:N0} pairs to {outputPath} ({new FileInfo(outputPath).Length / 1024.0:F0} KB)");
        Console.WriteLine($"density: {density:P2} of the {cards * cards:N0} possible pairs actually move a score");
    }
}
