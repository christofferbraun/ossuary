using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Ossuary.Grading;

namespace Ossuary.Tools.FetchCodexData;

/// <summary>
/// Build-time tool. Pulls Spire Codex community metrics, re-bands them onto a
/// normal curve, and writes the table Ossuary embeds.
/// </summary>
/// <remarks>
/// This never runs on a player's machine. An installed copy of Ossuary makes no
/// network requests at all; everything it knows was written here and committed.
/// </remarks>
internal static class Program
{
    private const string BaseUrl = "https://spire-codex.com";

    /// <summary>
    /// Pace between requests, discovered rather than assumed.
    /// </summary>
    /// <remarks>
    /// The figure that used to be hardcoded here said Codex allows 60/minute.
    /// Their live <c>/api/rate-limits</c> reports 15/minute for an unregistered
    /// caller — and their API did start refusing us mid-development, which is
    /// what a limit being exceeded looks like. Reading the published figure
    /// removes the guess.
    ///
    /// Their limits are scoped <b>per endpoint</b>, which is why this refresh is
    /// cheap regardless: seven requests over six endpoints, none hit more than
    /// twice. The pacing costs half a minute on a weekly job and removes any
    /// question of us being the reason they start refusing.
    /// </remarks>
    private static RateLimit _limit = null!;

    /// <summary>
    /// Exit code for "Codex could not be reached". Distinct from both success
    /// and a real error so the workflow can warn and stop rather than fail.
    /// </summary>
    private const int UpstreamUnavailable = 20;

    private static readonly string[] Kinds = ["cards", "relics", "potions"];

    /// <summary>
    /// Cohorts bundled in v1: the global population only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Codex serves more slices than this — <c>cohort=a10</c> is a real bracket
    /// (a0..a9 and a11..a20 silently fall back to the full population, and
    /// ascension caps at 10 in this build), and all five characters filter. They
    /// are not bundled, because their <c>score</c> field is unusable for
    /// re-banding. Measured against snapshot v26:
    /// </para>
    /// <code>
    ///   cohort                largest score tie    resulting bands
    ///   cards / all / all              23  (4%)    S:37  A:88  B:143  C:139  D:80  F:33
    ///   cards / a10 / all             106 (20%)    S:106 A:14  B:142  C:142  D:81  F:35
    ///   cards / all / IRONCLAD        286 (55%)    S:286 A:0   B:0    C:116  D:83  F:35
    ///   cards / all / NECROBINDER     316 (61%)    S:316 A:0   B:0    C:86   D:83  F:35
    /// </code>
    /// <para>
    /// Character-scoped scores saturate at 100 — 61% of cards for Necrobinder —
    /// which collapses A and B to nothing and puts most of the game in S. That
    /// is the same failure as Codex's own bottom-loaded tiers, just inverted,
    /// and shipping it would defeat the point of re-banding at all. The
    /// saturation is in the published data, not in our banding: it repeats on
    /// <c>/api/runs/scores/{kind}</c>, and <c>elo</c> under a character scope is
    /// the global value rather than a character-specific one.
    /// </para>
    /// <para>
    /// Character win rates <em>are</em> sound and differentiated, so per-character
    /// ratings remain possible later on a metric we derive ourselves. Widening
    /// these two arrays is all it takes if Codex's scoring improves — the
    /// saturation guard below will refuse anything still degenerate.
    /// </para>
    /// </remarks>
    private static readonly string[] Brackets = ["all"];

    private static readonly string[] Characters = ["all"];

    /// <summary>
    /// Largest share the top band may hold before a cohort is rejected.
    /// </summary>
    /// <remarks>
    /// S targets 6.7%. The sound cohorts land at 7.1% (cards), 10.4% (relics)
    /// and 14.1% (potions — only 64 entities, so ties bite harder); the
    /// degenerate ones start at 20.4% and run to 61%. The threshold sits in that
    /// gap, so this catches Codex's data drifting without tripping on the
    /// coarseness of a small population.
    /// </remarks>
    private const double MaxTopBandShare = 0.20;

    private static async Task<int> Main(string[] args)
    {
        var repoRoot = FindRepoRoot();
        var outputPath = Path.Combine(repoRoot, "src", "Ossuary", "Data", "ratings.tsv");
        var check = args.Contains("--check");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Ossuary-data-fetch (+https://github.com/christofferbraun/ossuary)");

        if (args.Contains("--harvest-lifts"))
        {
            // v2 groundwork: the pairwise model behind Codex's draft advice.
            // Deliberately not part of the weekly refresh - it is one request
            // per card and takes the better part of an hour. It discovers its
            // own pacing, so it runs before the ratings path does.
            //
            // --tier lets a caller pace to a higher published tier if Codex
            // ever grants one; unregistered callers get the general tier.
            var tierIndex = Array.IndexOf(args, "--tier");
            var tier = tierIndex >= 0 && tierIndex + 1 < args.Length ? args[tierIndex + 1] : "general";
            return await HarvestLifts.Run(http, BaseUrl, repoRoot, tier);
        }

        _limit = await RateLimit.Discover(http, BaseUrl, "general");
        Console.WriteLine($"rate limit: {_limit}");

        SnapshotStatus status;
        try
        {
            status = await GetJsonWithRetry<SnapshotStatus>(http, $"{BaseUrl}/api/runs/snapshot-status");
        }
        catch (Exception ex) when (check)
        {
            // Codex's /api/runs/* endpoints have gone down independently of the
            // rest of their API - observed returning 502s and then timing out
            // while /api/cards and /api/rate-limits answered in under 200ms.
            // A community service being unavailable is not a failure of ours,
            // and a weekly cron should not go red for it. Report and skip.
            Console.WriteLine($"upstream unavailable: {ex.Message}");
            Console.WriteLine("skipping this run; the bundled data is unchanged");
            return UpstreamUnavailable;
        }

        Console.WriteLine($"Codex snapshot v{status.Version}, {status.TotalRuns:N0} runs, data through {status.DataThrough}");

        if (check)
        {
            // --check is what the scheduled workflow calls: report whether the
            // published snapshot has moved past what is committed, and do
            // nothing else.
            var committed = ReadCommittedVersion(outputPath);
            Console.WriteLine($"committed=v{committed?.ToString() ?? "none"} published=v{status.Version}");
            return committed == status.Version ? 0 : 10;
        }

        var previous = ReadCommittedTiers(outputPath);
        var rows = new List<OutputRow>();
        var coverage = new List<string>();

        foreach (var kind in Kinds)
        {
            var inGame = await FetchCompendium(http, kind);
            await Task.Delay(_limit.Delay);

            foreach (var bracket in Brackets)
            {
                foreach (var character in Characters)
                {
                    var slice = await FetchSlice(http, kind, bracket, character, inGame);
                    var banded = Band(kind, bracket, character, slice).ToList();
                    rows.AddRange(banded);
                    if (bracket == "all" && character == "all")
                    {
                        coverage.Add($"{kind} {banded.Count}/{inGame.Count}");
                    }

                    await Task.Delay(_limit.Delay);
                }
            }
        }

        Write(outputPath, status, rows, coverage);
        Report(rows, previous);
        return 0;
    }

    /// <summary>
    /// The ids that exist in the game right now, from Codex's compendium.
    /// </summary>
    /// <remarks>
    /// The compendium and the metrics endpoint answer different questions, and
    /// their counts disagree in both directions. Measured on snapshot v26:
    /// 577 cards exist but only 520 have run data, while 17 ids with run data
    /// are no longer in the game at all (relics add 2 more, potions 1).
    ///
    /// The gap is not a fault on either side. Curses, statuses, tokens, quest
    /// cards and event/ancient-pool cards are never offered in a ranked card
    /// reward, so there is no pick data to have; and Codex's metrics span every
    /// run ever submitted, including builds where since-removed cards still
    /// existed.
    /// </remarks>
    private static async Task<HashSet<string>> FetchCompendium(HttpClient http, string kind)
    {
        var entries = await GetJsonWithRetry<List<CompendiumEntry>>(http, $"{BaseUrl}/api/{kind}");
        return entries.Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fetches one kind for one cohort, and refuses anything that does not match
    /// what was asked for.
    /// </summary>
    /// <remarks>
    /// This validation is the whole reason the method exists. The API fails
    /// <em>silently</em>: an unrecognised <c>cohort</c> returns the full
    /// population with <c>bracket: "all"</c>, and an unrecognised
    /// <c>character</c> returns an empty row list with HTTP 200. Without an
    /// echo check, a typo or a renamed cohort would quietly bundle
    /// all-population numbers under a character's name and nothing would ever
    /// look wrong.
    /// </remarks>
    private static async Task<List<MetricRow>> FetchSlice(
        HttpClient http, string kind, string bracket, string character, HashSet<string> inGame)
    {
        var url = $"{BaseUrl}/api/runs/metrics/{kind}";
        var query = new List<string>();
        if (bracket != "all") query.Add($"cohort={bracket}");
        if (character != "all") query.Add($"character={character}");
        if (query.Count > 0) url += "?" + string.Join("&", query);

        var response = await GetJsonWithRetry<MetricsResponse>(http, url);

        if (!string.Equals(response.Bracket, bracket, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"asked for bracket '{bracket}' and got '{response.Bracket}' — the API silently "
                + "falls back to the full population for unknown cohorts, so this would have "
                + "bundled wrong numbers.");
        }

        var wantCharacter = character == "all" ? null : character;
        if (!string.Equals(response.Character, wantCharacter, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"asked for character '{character}' and got '{response.Character ?? "null"}'");
        }

        // Base ids only. Upgraded rows are a separate entity in Codex's model,
        // and grading them separately would mean an offered card's tier changed
        // depending on whether you happened to be looking at the upgrade.
        var baseRows = response.Rows.Where(r => !r.Upgraded).ToList();

        // Keep only what the game still has. Carrying a since-removed card would
        // put dead weight in the bundle and, worse, let it vote on where the
        // band thresholds fall — the grades should be a statement about the
        // cards a player can actually be offered.
        var rows = baseRows.Where(r => inGame.Contains(r.Id)).ToList();
        var retired = baseRows.Count - rows.Count;

        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                $"{kind}/{bracket}/{character} returned no base rows — an unknown character "
                + "returns HTTP 200 with an empty list, so this is a bad cohort, not a real gap.");
        }

        var note = retired > 0 ? $"  ({retired} no longer in the game, dropped)" : "";
        Console.WriteLine($"  {kind,-8} {bracket,-4} {character,-12} {rows.Count,4} rows{note}");
        return rows;
    }

    /// <summary>
    /// Gets JSON, retrying transient failures with exponential backoff.
    /// </summary>
    /// <remarks>
    /// A full refresh is 37 requests against a public community service, and a
    /// single 502 partway through would otherwise throw away every request made
    /// before it. Observed in practice on the first real run. Only transport
    /// errors and 429/5xx are retried — a 404 or a malformed body is a real bug
    /// and should fail immediately rather than be retried five times.
    /// </remarks>
    private static async Task<T> GetJsonWithRetry<T>(HttpClient http, string url)
    {
        const int attempts = 5;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await http.GetFromJsonAsync<T>(url)
                    ?? throw new InvalidOperationException($"empty response for {url}");
            }
            catch (Exception ex) when (attempt < attempts && IsTransient(ex))
            {
                var wait = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                Console.WriteLine($"    {Describe(ex)} on attempt {attempt}; retrying in {wait.TotalSeconds:F0}s");
                await Task.Delay(wait);
            }
        }
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException http => http.StatusCode is null
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout,
        TaskCanceledException => true,
        // A gateway in trouble will serve an HTML error page with HTTP 200,
        // which arrives here as "the input does not contain any JSON tokens"
        // rather than as a status code. Observed mid-outage: three 502s followed
        // by a 200 carrying no JSON at all.
        System.Text.Json.JsonException => true,
        _ => false,
    };

    private static string Describe(Exception ex) =>
        ex is HttpRequestException { StatusCode: not null } h
            ? $"HTTP {(int)h.StatusCode.Value}"
            : ex.GetType().Name;

    private static IEnumerable<OutputRow> Band(
        string kind, string bracket, string character, List<MetricRow> slice)
    {
        var ratings = slice
            .Select(r => new Rating(r.Id, r.Score, r.WinRate, r.PickRate, r.Picks))
            .ToList();

        var bands = TierBands.Derive(ratings);
        var graded = ratings
            .Select(r => new OutputRow(kind, bracket, character, r, bands.Grade(r)))
            .OrderBy(r => r.Rating.Id, StringComparer.Ordinal)
            .ToList();

        // Refuse to bundle a cohort whose scores are too tied to rank. Codex
        // clips score to 0..100, and where that clipping is severe the top band
        // swallows the population and the bands below it empty out — a tier list
        // that grades most of the game S is no more useful than one that grades
        // most of it F.
        var topShare = graded.Count(r => r.Tier == TierBands.NormalCurve[0].Tier) / (double)graded.Count;
        if (topShare > MaxTopBandShare)
        {
            throw new InvalidOperationException(
                $"{kind}/{bracket}/{character}: {topShare:P1} of entries landed in "
                + $"{TierBands.NormalCurve[0].Tier}, above the {MaxTopBandShare:P0} limit. "
                + "Codex's scores for this cohort are too saturated to re-band.");
        }

        return graded;
    }

    private static void Write(
        string path, SnapshotStatus status, List<OutputRow> rows, List<string> coverage)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var sb = new StringBuilder();
        // A header the loader validates and a human can read. The snapshot
        // version is what --check compares against.
        sb.Append("# Ossuary rating table — generated by tools/FetchCodexData, do not edit\n");
        sb.Append(CultureInfo.InvariantCulture, $"# snapshot\tv{status.Version}\n");
        sb.Append(CultureInfo.InvariantCulture, $"# data_through\t{status.DataThrough}\n");
        sb.Append(CultureInfo.InvariantCulture, $"# total_runs\t{status.TotalRuns}\n");
        // rated/in-game, so the counts are explainable rather than mysterious:
        // things with no run data (curses, statuses, tokens, event and ancient
        // cards) are never offered in a ranked reward and so are never rated.
        sb.Append(CultureInfo.InvariantCulture, $"# coverage\t{string.Join("  ", coverage)}\n");
        sb.Append("kind\tbracket\tcharacter\tid\tscore\twin_rate\tpick_rate\tpicks\ttier\n");

        // Deterministic order, so a refresh diff shows only what actually moved.
        foreach (var row in rows
                     .OrderBy(r => r.Kind, StringComparer.Ordinal)
                     .ThenBy(r => r.Bracket, StringComparer.Ordinal)
                     .ThenBy(r => r.Character, StringComparer.Ordinal)
                     .ThenBy(r => r.Rating.Id, StringComparer.Ordinal))
        {
            var r = row.Rating;
            var pick = r.PickRate?.ToString("0.#", CultureInfo.InvariantCulture) ?? "";
            sb.Append(CultureInfo.InvariantCulture,
                $"{row.Kind}\t{row.Bracket}\t{row.Character}\t{r.Id}\t{r.Score}\t"
                + $"{r.WinRate.ToString("0.#", CultureInfo.InvariantCulture)}\t{pick}\t{r.Picks}\t{row.Tier}\n");
        }

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"\nwrote {rows.Count:N0} rows to {path} ({new FileInfo(path).Length / 1024.0:F0} KB)");
    }

    /// <summary>
    /// Prints the realised band distribution and, when refreshing over an
    /// existing table, what moved. This is the body of the refresh PR: a data
    /// refresh can silently move a hundred cards between grades, and that should
    /// be glanceable rather than buried in a diff.
    /// </summary>
    private static void Report(List<OutputRow> rows, Dictionary<string, Tier> previous)
    {
        Console.WriteLine("\nBand distribution (all cohorts pooled):");
        var total = rows.Count;
        foreach (var (tier, share) in TierBands.NormalCurve)
        {
            var got = rows.Count(r => r.Tier == tier) / (double)total;
            Console.WriteLine($"  {tier}  target {share:P1}   actual {got:P1}");
        }

        if (previous.Count == 0)
        {
            Console.WriteLine("\nno previous table to compare against");
            return;
        }

        var moved = rows
            .Where(r => previous.TryGetValue(r.Key, out var was) && was != r.Tier)
            .ToList();

        Console.WriteLine($"\n{moved.Count:N0} of {total:N0} entries changed tier");
        foreach (var row in moved.Take(40))
        {
            Console.WriteLine($"  {row.Kind}/{row.Bracket}/{row.Character} {row.Rating.Id}: {previous[row.Key]} -> {row.Tier}");
        }

        if (moved.Count > 40) Console.WriteLine($"  ... and {moved.Count - 40:N0} more");
    }

    private static int? ReadCommittedVersion(string path)
    {
        if (!File.Exists(path)) return null;

        foreach (var line in File.ReadLines(path))
        {
            if (!line.StartsWith("# snapshot\t", StringComparison.Ordinal)) continue;
            var value = line["# snapshot\tv".Length..];
            return int.TryParse(value, out var v) ? v : null;
        }

        return null;
    }

    private static Dictionary<string, Tier> ReadCommittedTiers(string path)
    {
        var map = new Dictionary<string, Tier>(StringComparer.Ordinal);
        if (!File.Exists(path)) return map;

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith('#') || line.StartsWith("kind\t", StringComparison.Ordinal)) continue;
            var f = line.Split('\t');
            if (f.Length < 9 || !Enum.TryParse<Tier>(f[8], out var tier)) continue;
            map[$"{f[0]}/{f[1]}/{f[2]}/{f[3]}"] = tier;
        }

        return map;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ossuary.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("could not locate the repository root");
    }

    private sealed record OutputRow(string Kind, string Bracket, string Character, Rating Rating, Tier Tier)
    {
        internal string Key => $"{Kind}/{Bracket}/{Character}/{Rating.Id}";
    }

    private sealed record SnapshotStatus(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("total_runs")] long TotalRuns,
        [property: JsonPropertyName("data_through")] string DataThrough);

    private sealed record MetricsResponse(
        [property: JsonPropertyName("bracket")] string Bracket,
        [property: JsonPropertyName("character")] string? Character,
        [property: JsonPropertyName("rows")] List<MetricRow> Rows);

    private sealed record CompendiumEntry(
        [property: JsonPropertyName("id")] string Id);

    private sealed record MetricRow(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("upgraded")] bool Upgraded,
        [property: JsonPropertyName("score")] int Score,
        [property: JsonPropertyName("win_rate")] double WinRate,
        [property: JsonPropertyName("pick_rate")] double? PickRate,
        [property: JsonPropertyName("picks")] long Picks);
}
