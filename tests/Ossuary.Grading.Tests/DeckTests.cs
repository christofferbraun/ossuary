using Ossuary.Deck;
using Xunit;

namespace Ossuary.Grading.Tests;

public class DrawOddsTests
{
    [Fact]
    public void ASingleCopyInATenCardPileIsOneInTen()
    {
        Assert.Equal(0.1, DrawOdds.AtLeastOne(copies: 1, pileSize: 10, draws: 1), 10);
    }

    /// <summary>
    /// The worked example from the panel: three copies left in nineteen cards,
    /// drawing five. Complement of (16/19)(15/18)(14/17)(13/16)(12/15).
    /// </summary>
    [Fact]
    public void MatchesTheHypergeometricByHand()
    {
        var expected = 1 - (16.0 / 19 * (15.0 / 18) * (14.0 / 17) * (13.0 / 16) * (12.0 / 15));

        Assert.Equal(expected, DrawOdds.AtLeastOne(copies: 3, pileSize: 19, draws: 5), 10);
    }

    [Fact]
    public void DrawingTheWholePileIsCertain()
    {
        Assert.Equal(1, DrawOdds.AtLeastOne(copies: 1, pileSize: 5, draws: 5));
        Assert.Equal(1, DrawOdds.AtLeastOne(copies: 1, pileSize: 5, draws: 99));
    }

    [Fact]
    public void NoCopiesIsNeverAndEveryCopyIsAlways()
    {
        Assert.Equal(0, DrawOdds.AtLeastOne(copies: 0, pileSize: 20, draws: 5));
        Assert.Equal(1, DrawOdds.AtLeastOne(copies: 20, pileSize: 20, draws: 1));
    }

    /// <summary>
    /// An empty draw pile is the normal state at the end of a turn, not an
    /// error. It must not divide by zero.
    /// </summary>
    [Fact]
    public void AnEmptyPileIsSurvivable()
    {
        Assert.Equal(0, DrawOdds.AtLeastOne(copies: 0, pileSize: 0, draws: 5));
        Assert.Equal(0, DrawOdds.AtLeastOne(copies: 2, pileSize: 0, draws: 5));
        Assert.Equal(0, DrawOdds.AtLeastOne(copies: 2, pileSize: 10, draws: 0));
    }

    [Fact]
    public void StaysAProbabilityAcrossTheWholeRange()
    {
        for (var pile = 1; pile <= 60; pile++)
        {
            for (var copies = 0; copies <= pile; copies++)
            {
                for (var draws = 0; draws <= pile + 2; draws++)
                {
                    var p = DrawOdds.AtLeastOne(copies, pile, draws);
                    Assert.InRange(p, 0.0, 1.0);
                }
            }
        }
    }

    [Fact]
    public void MoreCopiesAndMoreDrawsNeverLowerTheOdds()
    {
        for (var copies = 1; copies < 10; copies++)
        {
            Assert.True(
                DrawOdds.AtLeastOne(copies + 1, 30, 5) >= DrawOdds.AtLeastOne(copies, 30, 5),
                $"{copies + 1} copies should not be worse than {copies}");
        }

        for (var draws = 1; draws < 10; draws++)
        {
            Assert.True(
                DrawOdds.AtLeastOne(3, 30, draws + 1) >= DrawOdds.AtLeastOne(3, 30, draws),
                $"{draws + 1} draws should not be worse than {draws}");
        }
    }
}

public class DeckGroupingTests
{
    private static TrackedCard Card(string title, int upgrade = 0, int cost = 1, string type = "Attack") =>
        new($"ID_{title.ToUpperInvariant()}", title, type, upgrade, cost);

    [Fact]
    public void IdenticalCopiesCollapseIntoOneRow()
    {
        var groups = DeckGrouping.Group([Card("Strike"), Card("Strike"), Card("Strike")]);

        var row = Assert.Single(groups);
        Assert.Equal("Strike", row.Title);
        Assert.Equal(3, row.Count);
    }

    /// <summary>
    /// A Strike and a Strike+ are different cards to the player choosing, so
    /// they must not share a row.
    /// </summary>
    [Fact]
    public void UpgradesAreSeparateRows()
    {
        var groups = DeckGrouping.Group([Card("Strike"), Card("Strike", upgrade: 1)]);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Equal(1, g.Count));
    }

    /// <summary>
    /// In-combat effects modify individual copies, and a copy that now costs 0
    /// is not interchangeable with one that still costs 1.
    /// </summary>
    [Fact]
    public void CostChangesSplitARow()
    {
        var groups = DeckGrouping.Group([Card("Blur", cost: 1), Card("Blur", cost: 0)]);

        Assert.Equal(2, groups.Count);
        Assert.Equal(0, groups[0].EnergyCost);
    }

    [Fact]
    public void OrdersByCostThenTitleSoTheListHoldsStill()
    {
        var groups = DeckGrouping.Group(
        [
            Card("Zap", cost: 2), Card("Anger", cost: 0), Card("Blur", cost: 1), Card("Armaments", cost: 0),
        ]);

        Assert.Equal(["Anger", "Armaments", "Blur", "Zap"], groups.Select(g => g.Title));
    }

    [Fact]
    public void GroupingNeverLosesOrInventsACard()
    {
        TrackedCard[] pile =
        [
            Card("Strike"), Card("Strike"), Card("Strike", upgrade: 1),
            Card("Defend", type: "Skill"), Card("Defend", type: "Skill", cost: 0),
            Card("Inflame", type: "Power", cost: 2),
        ];

        Assert.Equal(pile.Length, DeckGrouping.Group(pile).Sum(g => g.Count));
    }

    [Fact]
    public void AnEmptyPileGroupsToNothing()
    {
        Assert.Empty(DeckGrouping.Group([]));
        Assert.Empty(DeckGrouping.ByType([]));
    }

    [Fact]
    public void RollsUpByTypeCommonestFirst()
    {
        var byType = DeckGrouping.ByType(
        [
            Card("Strike"), Card("Strike"), Card("Bash"),
            Card("Defend", type: "Skill"),
            Card("Inflame", type: "Power"),
        ]);

        Assert.Equal(("Attack", 3), byType[0]);
        Assert.Equal(5, byType.Sum(t => t.Count));
    }

    /// <summary>
    /// Types arrive from the game as text, so one added by a patch rolls up
    /// under its own name rather than vanishing into a bucket we forgot to
    /// update.
    /// </summary>
    [Fact]
    public void AnUnfamiliarTypeStillRollsUp()
    {
        var byType = DeckGrouping.ByType([Card("Ritual Dagger", type: "SomethingNew")]);

        Assert.Equal(("SomethingNew", 1), Assert.Single(byType));
    }

    [Fact]
    public void OddsComeFromTheGroupsOwnCount()
    {
        var groups = DeckGrouping.Group([Card("Strike"), Card("Strike"), Card("Strike")]);

        Assert.Equal(DrawOdds.AtLeastOne(3, 19, 5), groups[0].OddsIn(pileSize: 19, draws: 5), 10);
    }
}
