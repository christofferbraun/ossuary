using System.Reflection;
using Ossuary.Grading;

namespace Ossuary;

/// <summary>
/// The community rating table, read once from the assembly's own resources.
/// </summary>
/// <remarks>
/// The table is compiled into the DLL rather than shipped beside it. The loader
/// copies <c>&lt;id&gt;.dll</c> and the manifest and nothing else, so a data file
/// next to them is a file that may or may not arrive — and a rating table that
/// silently fails to install would leave every offer reading "no data" with
/// nothing to point at.
///
/// This is also why Ossuary makes no network requests while you play: everything
/// it knows was fetched at build time and committed.
/// </remarks>
internal static class Ratings
{
    private const string ResourceName = "Ossuary.Data.ratings.tsv";

    private static RatingTable? _table;

    /// <summary>
    /// The bundled table, or null if it could not be read. Null is survivable:
    /// panels show "no data" rather than refusing to draw.
    /// </summary>
    internal static RatingTable? Table => _table;

    /// <summary>Reads the table. Called once, during initialization.</summary>
    internal static void Load()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is null)
            {
                Log.Error($"rating table '{ResourceName}' is missing from the assembly; ratings are unavailable");
                return;
            }

            using var reader = new StreamReader(stream);
            _table = RatingTable.Parse(reader);

            Log.Info(
                $"ratings: Codex v{_table.SnapshotVersion} — {_table.All(RatingKind.Card).Count} cards, "
                + $"{_table.All(RatingKind.Relic).Count} relics, {_table.All(RatingKind.Potion).Count} potions "
                + $"({_table.TotalRuns:N0} runs, through {_table.DataThrough})");
        }
        catch (Exception ex)
        {
            Log.Error("rating table could not be read; ratings are unavailable this session", ex);
        }
    }
}
