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
/// <b>Which endpoint, and why this one.</b>
/// <c>GET /api/draft-recs/{item_type}/{item_id}</c> — "given a card/relic you
/// already have, the cards players most often draft when offered them, ranked
/// by lift over each card's baseline take-rate". That is the lift table, one
/// row per held item, stated as such.
/// </para>
/// <para>
/// The alternative is to probe <c>POST /api/draft-advice</c> with single-card
/// decks and divide by a baseline, recovering each lift by inference. That
/// works only if the scoring really is exactly multiplicative and context-free,
/// and it reads fields the endpoint does not document. This endpoint returns
/// <c>lift</c> directly and names its fields, so the same table costs the same
/// number of requests with none of the inference. It also covers relics, which
/// the deck-probing approach could not reach at all — and relic synergies are a
/// large part of what makes advice deck-specific.
/// </para>
/// <para>
/// <b>Why it is one request per item, and why that cannot be reduced.</b> The
/// rows are the model: each held item's row is an independent set of numbers,
/// so N items means N rows and there is no request that returns two rows. The
/// only bulk alternative Codex offers is <c>/api/exports/runs</c>, the entire
/// run corpus as gzipped JSONL, from which lifts could be recomputed — that
/// trades several hundred small requests for a multi-gigabyte download and a
/// reimplementation of their model, which is worse on every axis that matters
/// here.
/// </para>
/// <para>
/// So the run is long, and the work went into making a long run survivable:
/// pacing from the limits Codex publishes, honouring <c>Retry-After</c>, and
/// checkpointing after every row so an interrupted harvest resumes instead of
/// starting over.
/// </para>
/// <para>
/// <b>What is still inferred.</b> The field <em>values</em> are documented;
/// the container's shape is not — <c>recommends</c> could be a map keyed by id
/// or a list of objects carrying one. Both are handled, and
/// <c>--probe-lifts</c> fetches a single row and prints it so the shape can be
/// confirmed for the price of one request rather than a whole harvest.
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

    /// <summary>
    /// Below this many observed offers, a lift is noise wearing a number.
    /// </summary>
    /// <remarks>
    /// The endpoint reports <c>offers</c> as the sample behind each pair. A
    /// lift computed from a handful of drafts will be extreme and meaningless,
    /// and carrying it would let one player's oddity move an offered card's
    /// grade.
    /// </remarks>
    private const int MinOffers = 50;

    /// <summary>Item types to harvest, in the plural spelling Codex uses elsewhere.</summary>
    private static readonly string[] Types = ["cards", "relics"];

    private static readonly string[] ListFields = ["recommends", "recommendations", "results", "cards", "items"];
    private static readonly string[] IdFields = ["id", "card_id", "cardId", "item_id", "itemId"];
    private static readonly string[] LiftFields = ["lift"];
    private static readonly string[] OfferFields = ["offers", "n", "sample"];

    /// <summary>
    /// Fetches one row and prints it, so the response shape can be confirmed
    /// before committing to a run of several hundred requests.
    /// </summary>
    internal static async Task<int> Probe(HttpClient http, string baseUrl, string repoRoot)
    {
        var items = ReadIds(repoRoot);
        if (items.Count == 0)
        {
            Console.Error.WriteLine("no ids in the bundled table; run the ratings fetch first");
            return 1;
        }

        var (type, id) = items[0];
        var url = $"{baseUrl}/api/draft-recs/{type}/{id}";
        Console.WriteLine($"GET {url}\n");

        using var response = await http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        Console.WriteLine($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        foreach (var (name, values) in response.Headers)
        {
            if (name.StartsWith("X-", StringComparison.OrdinalIgnoreCase) || name == "Retry-After")
            {
                Console.WriteLine($"  {name}: {string.Join(", ", values)}");
            }
        }

        Console.WriteLine($"\n{Trim(body, 4000)}");

        if (!response.IsSuccessStatusCode) return 1;

        try
        {
            var parsed = Parse(body);
            Console.WriteLine($"\nparsed {parsed.Count} partner(s) with a usable lift");
            foreach (var (partner, lift, offers) in parsed.Take(5))
            {
                Console.WriteLine($"  {partner}\tlift {lift:0.####}\toffers {offers}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\ncould not parse: {ex.Message}");
            return 1;
        }
    }

    internal static async Task<int> Run(HttpClient http, string baseUrl, string repoRoot, string tier)
    {
        var outputPath = Path.Combine(repoRoot, "src", "Ossuary", "Data", "lifts.tsv");
        var checkpointDir = Path.Combine(repoRoot, "build", "harvest");
        var partialPath = Path.Combine(checkpointDir, "lifts.partial.tsv");
        var donePath = Path.Combine(checkpointDir, "done.txt");

        var items = ReadIds(repoRoot);
        if (items.Count == 0)
        {
            Console.Error.WriteLine("no ids in the bundled table; run the ratings fetch first");
            return 1;
        }

        var limit = await RateLimit.Discover(http, baseUrl, tier);
        Console.WriteLine($"rate limit: {limit}");

        Directory.CreateDirectory(checkpointDir);
        var done = File.Exists(donePath)
            ? File.ReadAllLines(donePath).Where(l => l.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var remaining = items.Where(i => !done.Contains(Key(i))).ToList();
        if (done.Count > 0)
        {
            Console.WriteLine($"resuming: {done.Count} of {items.Count} rows already harvested");
        }

        var eta = TimeSpan.FromSeconds(remaining.Count * limit.Delay.TotalSeconds);
        Console.WriteLine($"{remaining.Count} requests to go — about {eta.TotalMinutes:F0} minutes");
        Console.WriteLine("interrupting is safe; rerun to resume\n");

        var started = Stopwatch.StartNew();
        var pairs = 0;
        var empty = 0;

        for (var i = 0; i < remaining.Count; i++)
        {
            var item = remaining[i];
            if (i > 0) await Task.Delay(limit.Delay);

            var recs = await Recommendations(http, baseUrl, item, limit);
            var rows = new List<string>();

            foreach (var (candidate, lift, offers) in recs)
            {
                if (offers < MinOffers) continue;
                if (Math.Abs(lift - LiftTable.Neutral) < Meaningful) continue;

                rows.Add(string.Create(
                    CultureInfo.InvariantCulture, $"{item.Id}\t{candidate}\t{lift:0.####}"));
            }

            if (recs.Count == 0) empty++;

            // Checkpoint before recording the row as done, so a crash between
            // the two costs a repeated request rather than a missing row.
            if (rows.Count > 0) File.AppendAllLines(partialPath, rows);
            File.AppendAllLines(donePath, [Key(item)]);
            pairs += rows.Count;

            if ((i + 1) % 25 == 0 || i == remaining.Count - 1)
            {
                var per = started.Elapsed.TotalSeconds / (i + 1);
                var left = TimeSpan.FromSeconds(per * (remaining.Count - i - 1));
                Console.WriteLine(
                    $"  {i + 1}/{remaining.Count} rows · {pairs:N0} pairs this run · {left.TotalMinutes:F0} min left");
            }
        }

        if (empty > 0)
        {
            // Codex says "Empty recommends until the build job has run", so an
            // all-empty harvest means their model has not been built rather
            // than that anything here is wrong. Worth saying plainly.
            Console.WriteLine($"\n{empty:N0} of {remaining.Count:N0} rows came back with no recommendations.");
        }

        Finalise(outputPath, partialPath, donePath, items.Count);
        return 0;
    }

    private static string Key((string Type, string Id) item) => $"{item.Type}/{item.Id}";

    private static async Task<IReadOnlyList<(string Partner, double Lift, int Offers)>> Recommendations(
        HttpClient http, string baseUrl, (string Type, string Id) item, RateLimit limit)
    {
        const int attempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            using var response = await http.GetAsync($"{baseUrl}/api/draft-recs/{item.Type}/{item.Id}");

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

            // An item the draft model has never seen is a fact about that item,
            // not a failure: curses, starters and anything never offered as a
            // reward have no draft history.
            if (response.StatusCode == HttpStatusCode.NotFound) return [];

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

    /// <summary>
    /// Pulls partner/lift/sample triples out of a recommendations response.
    /// </summary>
    /// <remarks>
    /// The field names are Codex's own — <c>lift</c> and <c>offers</c>, as
    /// documented on the endpoint. Only the container is guessed at: a map
    /// keyed by id and a list of objects each carrying one are both accepted,
    /// because the documentation says "empty <c>recommends</c>" without saying
    /// which it is.
    /// </remarks>
    private static IReadOnlyList<(string Partner, double Lift, int Offers)> Parse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var container = root;
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var field in ListFields)
            {
                if (root.TryGetProperty(field, out var candidate)
                    && candidate.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                {
                    container = candidate;
                    break;
                }
            }
        }

        var found = new List<(string, double, int)>();

        switch (container.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var entry in container.EnumerateArray())
                {
                    var id = First(entry, IdFields)?.GetString();
                    if (id is not null && TryRead(entry, out var lift, out var offers))
                    {
                        found.Add((id, lift, offers));
                    }
                }

                break;

            case JsonValueKind.Object:
                foreach (var property in container.EnumerateObject())
                {
                    if (TryRead(property.Value, out var lift, out var offers))
                    {
                        found.Add((property.Name, lift, offers));
                    }
                }

                break;
        }

        // An empty list is a legitimate answer - Codex says recommendations are
        // empty until their build job has run - so it is not an error. A
        // container we could not even find is.
        if (found.Count == 0 && container.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
        {
            throw new InvalidOperationException(
                $"could not find the recommendations in the response. Tried {string.Join(", ", ListFields)}. "
                + $"Body begins: {Trim(body, 400)}");
        }

        return found;
    }

    private static bool TryRead(JsonElement entry, out double lift, out int offers)
    {
        lift = 0;
        offers = 0;

        if (entry.ValueKind != JsonValueKind.Object) return false;
        if (First(entry, LiftFields) is not { } liftValue || !liftValue.TryGetDouble(out lift)) return false;

        // No sample size reported is treated as "enough": the alternative is to
        // silently drop every pair when Codex renames the field.
        offers = First(entry, OfferFields) is { } n && n.TryGetInt32(out var parsed) ? parsed : int.MaxValue;
        return true;
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

    private static string Trim(string body, int max) => body.Length <= max ? body : body[..max] + "…";

    /// <summary>
    /// Every id worth asking about, from the table we already ship.
    /// </summary>
    /// <remarks>
    /// Cards and relics. Potions are excluded deliberately: they are consumed
    /// rather than held, so "what do players draft given they hold a Fire
    /// Potion" is not a question about a deck.
    /// </remarks>
    private static List<(string Type, string Id)> ReadIds(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "src", "Ossuary", "Data", "ratings.tsv");
        if (!File.Exists(path)) return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new List<(string, string)>();

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith('#') || line.StartsWith("kind\t", StringComparison.Ordinal)) continue;

            var f = line.Split('\t');
            if (f.Length <= 3) continue;
            if (Array.IndexOf(Types, f[0]) < 0) continue;
            if (!seen.Add($"{f[0]}/{f[3]}")) continue;

            ids.Add((f[0], f[3]));
        }

        return ids;
    }

    /// <summary>Turns the checkpoint into the bundled table and clears it.</summary>
    private static void Finalise(string outputPath, string partialPath, string donePath, int items)
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
        sb.Append("# source\tGET /api/draft-recs/{item_type}/{item_id}\n");
        sb.Append(CultureInfo.InvariantCulture, $"# items\t{items}\n");
        sb.Append(CultureInfo.InvariantCulture, $"# pairs\t{sorted.Count}\n");
        sb.Append(CultureInfo.InvariantCulture, $"# min_offers\t{MinOffers}\n");
        sb.Append("held\tcandidate\tlift\n");
        foreach (var row in sorted) sb.Append(row).Append('\n');

        File.WriteAllText(outputPath, sb.ToString(), new UTF8Encoding(false));

        File.Delete(partialPath);
        File.Delete(donePath);

        Console.WriteLine($"\nwrote {sorted.Count:N0} pairs to {outputPath} ({new FileInfo(outputPath).Length / 1024.0:F0} KB)");
        Console.WriteLine($"from {items:N0} held items, {sorted.Count / (double)Math.Max(items, 1):F0} pairs each on average");
    }
}
