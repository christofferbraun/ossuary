using System.Globalization;

namespace Ossuary.Grading;

/// <summary>What kind of thing a rating describes.</summary>
public enum RatingKind
{
    Card,
    Relic,
    Potion,
}

/// <summary>One graded entity, as bundled.</summary>
/// <param name="Id">Codex's id, e.g. <c>BALL_LIGHTNING</c>.</param>
/// <param name="Tier">
/// The grade, decided at build time by <see cref="TierBands"/> over the whole
/// population. Bundled rather than derived at startup so that what ships is
/// exactly what the tests asserted.
/// </param>
public readonly record struct RatedEntry(
    string Id,
    Tier Tier,
    int Score,
    double WinRate,
    double? PickRate,
    long Picks)
{
    public Confidence Confidence => ConfidenceRules.For(Picks);
}

/// <summary>
/// The bundled community ratings, parsed from the table
/// <c>tools/FetchCodexData</c> generates.
/// </summary>
/// <remarks>
/// <para>
/// Parsing is kept here, in the project that references nothing from the game,
/// so CI can test it against the real shipped table on a machine with no copy of
/// Slay the Spire 2.
/// </para>
/// <para>
/// The format is tab-separated text rather than a packed binary. At 39 KB the
/// saving from packing is not worth giving up a diff a human can read: a data
/// refresh can move a hundred entries between grades, and that should be
/// reviewable in the pull request that proposes it.
/// </para>
/// </remarks>
public sealed class RatingTable
{
    private readonly Dictionary<(RatingKind Kind, string Id), RatedEntry> _byId;
    private readonly Dictionary<RatingKind, IReadOnlyList<RatedEntry>> _byKind;

    private RatingTable(
        int snapshotVersion,
        string dataThrough,
        long totalRuns,
        Dictionary<(RatingKind, string), RatedEntry> byId,
        Dictionary<RatingKind, IReadOnlyList<RatedEntry>> byKind)
    {
        SnapshotVersion = snapshotVersion;
        DataThrough = dataThrough;
        TotalRuns = totalRuns;
        _byId = byId;
        _byKind = byKind;
    }

    /// <summary>Codex snapshot this table was built from, e.g. 26.</summary>
    public int SnapshotVersion { get; }

    /// <summary>The date Codex's data ran through, as published.</summary>
    public string DataThrough { get; }

    /// <summary>Runs behind the numbers, across the whole population.</summary>
    public long TotalRuns { get; }

    /// <summary>
    /// How much of the game each kind covers: how many entities were rated, out
    /// of how many exist in Codex's compendium.
    /// </summary>
    /// <remarks>
    /// Relics and potions come out fully covered. Cards do not and cannot:
    /// curses, statuses, tokens, quest cards and event or ancient-pool cards are
    /// never offered in a ranked card reward, so there is no pick data to rate
    /// them from. Carrying the counts here lets tests assert the *relationship*
    /// — everything offerable is rated — instead of a magic number that a game
    /// patch would break for an entirely legitimate reason.
    /// </remarks>
    public IReadOnlyDictionary<RatingKind, (int Rated, int InGame)> Coverage { get; private init; }
        = new Dictionary<RatingKind, (int, int)>();

    /// <summary>Every entry of one kind, in the order they were bundled.</summary>
    public IReadOnlyList<RatedEntry> All(RatingKind kind) =>
        _byKind.TryGetValue(kind, out var rows) ? rows : [];

    /// <summary>
    /// Looks up one entity, returning false when it is not in the table.
    /// </summary>
    /// <remarks>
    /// A miss is normal and must stay cheap to handle: modded cards, cards added
    /// by a game patch newer than the bundle, and anything Codex has too little
    /// data on will all miss. Callers show "no data" rather than a wrong grade.
    /// </remarks>
    public bool TryGet(RatingKind kind, string id, out RatedEntry entry) =>
        _byId.TryGetValue((kind, Normalize(id)), out entry);

    /// <summary>
    /// Reduces a game id to the form the table is keyed by.
    /// </summary>
    /// <remarks>
    /// The game's own ids carry a category prefix — its logs report unknown
    /// cards as <c>CARD.FOLLOW_THROUGH</c> — while Codex publishes the bare
    /// <c>FOLLOW_THROUGH</c>. Everything up to and including the last dot is
    /// dropped, which is a no-op for ids that already have no prefix. The exact
    /// shape the game hands us at an offer is confirmed in M5; until then this
    /// is deliberately tolerant rather than clever.
    /// </remarks>
    public static string Normalize(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        var span = id.AsSpan().Trim();
        var dot = span.LastIndexOf('.');
        if (dot >= 0) span = span[(dot + 1)..];

        return span.ToString().ToUpperInvariant();
    }

    /// <summary>Parses a bundled table.</summary>
    /// <exception cref="FormatException">The table is malformed.</exception>
    public static RatingTable Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var version = 0;
        var dataThrough = "";
        var totalRuns = 0L;
        var coverage = new Dictionary<RatingKind, (int, int)>();
        var byId = new Dictionary<(RatingKind, string), RatedEntry>();
        var byKind = new Dictionary<RatingKind, List<RatedEntry>>();

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;

            if (line[0] == '#')
            {
                var meta = line[1..].Split('\t');
                if (meta.Length < 2) continue;
                switch (meta[0].Trim())
                {
                    case "snapshot":
                        version = int.Parse(meta[1].TrimStart('v'), CultureInfo.InvariantCulture);
                        break;
                    case "data_through":
                        dataThrough = meta[1];
                        break;
                    case "total_runs":
                        totalRuns = long.Parse(meta[1], CultureInfo.InvariantCulture);
                        break;
                    case "coverage":
                        coverage = ParseCoverage(meta[1]);
                        break;
                }

                continue;
            }

            // The column header.
            if (line.StartsWith("kind\t", StringComparison.Ordinal)) continue;

            var f = line.Split('\t');
            if (f.Length < 9) throw new FormatException($"expected 9 columns, got {f.Length}: {line}");

            var kind = ParseKind(f[0]);
            var entry = new RatedEntry(
                Id: f[3],
                Tier: Enum.Parse<Tier>(f[8]),
                Score: int.Parse(f[4], CultureInfo.InvariantCulture),
                WinRate: double.Parse(f[5], CultureInfo.InvariantCulture),
                PickRate: f[6].Length == 0 ? null : double.Parse(f[6], CultureInfo.InvariantCulture),
                Picks: long.Parse(f[7], CultureInfo.InvariantCulture));

            // Later rows win, so a table that ever carries more than one cohort
            // does not silently produce duplicate keys.
            byId[(kind, Normalize(entry.Id))] = entry;
            if (!byKind.TryGetValue(kind, out var list)) byKind[kind] = list = [];
            list.Add(entry);
        }

        if (byId.Count == 0) throw new FormatException("the rating table contained no rows");

        return new RatingTable(
            version,
            dataThrough,
            totalRuns,
            byId,
            byKind.ToDictionary(p => p.Key, p => (IReadOnlyList<RatedEntry>)p.Value))
        {
            Coverage = coverage,
        };
    }

    /// <summary>
    /// Reads the coverage header, e.g. <c>cards 503/577  relics 296/296</c>.
    /// </summary>
    private static Dictionary<RatingKind, (int, int)> ParseCoverage(string value)
    {
        var result = new Dictionary<RatingKind, (int, int)>();
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var i = 0; i + 1 < tokens.Length; i += 2)
        {
            var parts = tokens[i + 1].Split('/');
            if (parts.Length != 2) continue;
            if (!int.TryParse(parts[0], CultureInfo.InvariantCulture, out var rated)) continue;
            if (!int.TryParse(parts[1], CultureInfo.InvariantCulture, out var inGame)) continue;
            result[ParseKind(tokens[i])] = (rated, inGame);
        }

        return result;
    }

    private static RatingKind ParseKind(string value) => value switch
    {
        "cards" => RatingKind.Card,
        "relics" => RatingKind.Relic,
        "potions" => RatingKind.Potion,
        _ => throw new FormatException($"unknown kind '{value}'"),
    };
}
