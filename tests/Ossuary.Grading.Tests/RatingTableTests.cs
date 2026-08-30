using Xunit;

namespace Ossuary.Grading.Tests;

/// <summary>
/// Verifies the table Ossuary actually ships, not a fixture standing in for it.
/// </summary>
/// <remarks>
/// The file under test is the one linked from <c>src/Ossuary/Data</c> and
/// embedded in the mod assembly, so a data refresh that broke the curve or the
/// format fails here — in CI, on a machine with no copy of the game — rather
/// than in front of a player.
/// </remarks>
public class RatingTableTests
{
    private static RatingTable Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ratings.tsv");
        using var reader = new StreamReader(path);
        return RatingTable.Parse(reader);
    }

    /// <summary>
    /// Every relic and potion in the game is rated.
    /// </summary>
    /// <remarks>
    /// Asserted as a relationship against the compendium counts in the table's
    /// own header, not as a fixed number. A game patch that adds relics is an
    /// entirely legitimate event and must not fail the build; a fetch that
    /// silently dropped half of them must.
    /// </remarks>
    [Theory]
    [InlineData(RatingKind.Relic)]
    [InlineData(RatingKind.Potion)]
    public void RatesEveryOneOfThese(RatingKind kind)
    {
        var table = Load();
        var (rated, inGame) = table.Coverage[kind];

        Assert.Equal(inGame, rated);
        Assert.Equal(rated, table.All(kind).Count);
    }

    /// <summary>
    /// Most cards are rated, but not all of them can be: curses, statuses,
    /// tokens, quest cards and event or ancient-pool cards are never offered in
    /// a ranked card reward, so there is no pick data to rate them from. Those
    /// show "no data" at an offer rather than a grade.
    /// </summary>
    [Fact]
    public void RatesMostCards()
    {
        var table = Load();
        var (rated, inGame) = table.Coverage[RatingKind.Card];

        Assert.Equal(rated, table.All(RatingKind.Card).Count);
        Assert.True(
            rated >= inGame * 0.80,
            $"only {rated} of {inGame} cards are rated, which is too few to be the unratable ones alone");
        Assert.True(rated <= inGame, $"{rated} rated exceeds the {inGame} that exist");
    }

    [Fact]
    public void KnowsHowMuchOfTheGameItCovers()
    {
        var table = Load();

        foreach (var kind in new[] { RatingKind.Card, RatingKind.Relic, RatingKind.Potion })
        {
            Assert.True(table.Coverage.ContainsKey(kind), $"no coverage recorded for {kind}");
            Assert.True(table.Coverage[kind].InGame > 0);
        }
    }

    /// <summary>
    /// Nothing retired from the game is carried. Codex's metrics span every run
    /// ever submitted, so they still report ids from builds where those cards
    /// existed; keeping them would put dead weight in the bundle and let them
    /// vote on where the band thresholds fall.
    /// </summary>
    [Fact]
    public void CarriesNothingTheGameHasRetired()
    {
        var table = Load();

        foreach (var id in new[] { "BLAZE", "CACOPHONY", "HIBERNATE", "TUTOR", "UNDERWORLD" })
        {
            Assert.False(table.TryGet(RatingKind.Card, id, out _), $"{id} was removed from the game");
        }

        Assert.False(table.TryGet(RatingKind.Relic, "DOWSING_ROD", out _));
        Assert.False(table.TryGet(RatingKind.Potion, "AMBERGRIS", out _));
    }

    [Fact]
    public void CarriesTheSnapshotItWasBuiltFrom()
    {
        var table = Load();

        Assert.Equal(26, table.SnapshotVersion);
        Assert.True(table.TotalRuns > 1_000_000, $"expected over a million runs, got {table.TotalRuns}");
        Assert.NotEmpty(table.DataThrough);
    }

    [Fact]
    public void CardGradesApproximateTheNormalCurve()
    {
        var cards = Load().All(RatingKind.Card);

        foreach (var (tier, target) in TierBands.NormalCurve)
        {
            var actual = cards.Count(c => c.Tier == tier) / (double)cards.Count;
            Assert.True(
                Math.Abs(actual - target) < 0.05,
                $"{tier}: expected about {target:P1}, got {actual:P1}");
        }
    }

    /// <summary>
    /// The failure this guards against is the one that made per-character
    /// cohorts unusable: Codex clips score to 0-100, and where that clipping
    /// bites, the top band swallows the population and the bands below it empty
    /// out. A tier list that grades most of the game S is no more useful than
    /// one that grades most of it F.
    /// </summary>
    [Theory]
    [InlineData(RatingKind.Card)]
    [InlineData(RatingKind.Relic)]
    [InlineData(RatingKind.Potion)]
    public void NoKindIsTopHeavy(RatingKind kind)
    {
        var rows = Load().All(kind);
        var share = rows.Count(r => r.Tier == Tier.S) / (double)rows.Count;

        Assert.True(share < 0.20, $"{kind}: {share:P1} graded S, which means the scores are too tied to rank");
    }

    /// <summary>
    /// Equal scores must receive equal grades, and a better score must never
    /// receive a worse grade. This is the property the tie-snapping in
    /// <see cref="TierBands"/> exists to preserve, and it is what a player would
    /// notice immediately if it broke.
    /// </summary>
    [Theory]
    [InlineData(RatingKind.Card)]
    [InlineData(RatingKind.Relic)]
    [InlineData(RatingKind.Potion)]
    public void GradesNeverDisagreeWithScores(RatingKind kind)
    {
        var rows = Load().All(kind).OrderByDescending(r => r.Score).ToList();

        for (var i = 1; i < rows.Count; i++)
        {
            var better = rows[i - 1];
            var worse = rows[i];

            if (better.Score == worse.Score)
            {
                Assert.True(
                    better.Tier == worse.Tier,
                    $"{better.Id} and {worse.Id} both score {better.Score} but graded {better.Tier} and {worse.Tier}");
            }
            else
            {
                Assert.True(
                    better.Tier <= worse.Tier,
                    $"{better.Id} scores {better.Score} ({better.Tier}) but {worse.Id} scores {worse.Score} ({worse.Tier})");
            }
        }
    }

    [Fact]
    public void LooksUpByGameIdAsWellAsCodexId()
    {
        var table = Load();

        Assert.True(table.TryGet(RatingKind.Card, "BACKFLIP", out var bare));
        // The game's own ids carry a category prefix; its logs report unknown
        // cards as CARD.FOLLOW_THROUGH.
        Assert.True(table.TryGet(RatingKind.Card, "CARD.BACKFLIP", out var prefixed));
        Assert.True(table.TryGet(RatingKind.Card, "backflip", out var lower));

        Assert.Equal(bare, prefixed);
        Assert.Equal(bare, lower);
    }

    [Fact]
    public void ReportsAMissRatherThanGuessing()
    {
        var table = Load();

        // Modded cards, and cards from a game patch newer than the bundle, both
        // land here. Callers show "no data" rather than a wrong grade.
        Assert.False(table.TryGet(RatingKind.Card, "SOME_MODDED_CARD", out _));
        Assert.False(table.TryGet(RatingKind.Relic, "BACKFLIP", out _));
    }

    [Fact]
    public void EveryEntryIsUsable()
    {
        var table = Load();

        foreach (var kind in new[] { RatingKind.Card, RatingKind.Relic, RatingKind.Potion })
        {
            foreach (var entry in table.All(kind))
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Id));
                Assert.InRange(entry.Score, 0, 100);
                Assert.InRange(entry.WinRate, 0, 100);
                Assert.True(entry.Picks > 0, $"{entry.Id} has no picks");
            }
        }
    }

    [Fact]
    public void OnlyCardsCarryAPickRate()
    {
        var table = Load();

        // Relics and potions are not drafted from a ranked offer set, so a pick
        // rate there would read as "picked 0% of the time" rather than "not a
        // meaningful question".
        Assert.All(table.All(RatingKind.Card), c => Assert.NotNull(c.PickRate));
        Assert.All(table.All(RatingKind.Relic), r => Assert.Null(r.PickRate));
        Assert.All(table.All(RatingKind.Potion), p => Assert.Null(p.PickRate));
    }

    [Fact]
    public void RejectsAnEmptyTable()
    {
        using var reader = new StringReader("# snapshot\tv26\nkind\tbracket\tcharacter\tid\tscore\twin_rate\tpick_rate\tpicks\ttier\n");
        Assert.Throws<FormatException>(() => RatingTable.Parse(reader));
    }
}
