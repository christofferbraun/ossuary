using Ossuary.Advice;
using Xunit;

namespace Ossuary.Grading.Tests;

/// <summary>
/// Pins the three properties that make an offline reimplementation of Codex's
/// draft advice possible. If any of them stops holding, the model is wrong and
/// no amount of fresh data will fix it.
/// </summary>
public class DeckAdviceTests
{
    private static LiftTable Lifts(params (string Held, string Candidate, double Lift)[] entries) =>
        new(entries.ToDictionary(e => (e.Held, e.Candidate), e => e.Lift));

    private static Func<string, double> Base(double value) => _ => value;

    [Fact]
    public void AnEmptyDeckLeavesTheBaseScoreAlone()
    {
        var ranked = DeckAdvice.Rank(["BACKFLIP"], [], Base(50), Lifts());

        var offer = Assert.Single(ranked);
        Assert.Equal(50, offer.Base);
        Assert.Equal(50, offer.Score);
        Assert.Empty(offer.Reasons);
    }

    [Fact]
    public void AnUnknownPairIsNeutral()
    {
        var ranked = DeckAdvice.Rank(["BACKFLIP"], ["SOMETHING_ELSE"], Base(50), Lifts());

        Assert.Equal(50, ranked[0].Score);
        Assert.Empty(ranked[0].Reasons);
    }

    /// <summary>
    /// Exactly multiplicative — the property that lets the whole model be
    /// harvested one held card at a time.
    /// </summary>
    [Fact]
    public void LiftsMultiply()
    {
        var lifts = Lifts(("STRIKE", "BACKFLIP", 1.5), ("DEFEND", "BACKFLIP", 2.0));

        var ranked = DeckAdvice.Rank(["BACKFLIP"], ["STRIKE", "DEFEND"], Base(10), lifts);

        Assert.Equal(30, ranked[0].Score, 10);
        Assert.Equal(3.0, ranked[0].DeckFactor, 10);
    }

    /// <summary>
    /// Duplicate-insensitive: the model dedupes to a set, so a second copy adds
    /// nothing. Getting this wrong would compound a card's influence with every
    /// copy and quietly wreck the ranking of a deck built around one card.
    /// </summary>
    [Fact]
    public void ExtraCopiesOfAHeldCardChangeNothing()
    {
        var lifts = Lifts(("STRIKE", "BACKFLIP", 1.5));

        var one = DeckAdvice.Rank(["BACKFLIP"], ["STRIKE"], Base(10), lifts)[0].Score;
        var three = DeckAdvice.Rank(["BACKFLIP"], ["STRIKE", "STRIKE", "STRIKE"], Base(10), lifts)[0].Score;

        Assert.Equal(one, three, 10);
    }

    [Fact]
    public void HeldCardsAreCaseAndPrefixInsensitive()
    {
        var lifts = Lifts(("STRIKE", "BACKFLIP", 1.5));

        var bare = DeckAdvice.Rank(["BACKFLIP"], ["STRIKE"], Base(10), lifts)[0].Score;
        var prefixed = DeckAdvice.Rank(["CARD.BACKFLIP"], ["card.strike"], Base(10), lifts)[0].Score;

        Assert.Equal(bare, prefixed, 10);
    }

    [Fact]
    public void DeckOrderDoesNotMatter()
    {
        var lifts = Lifts(("A", "X", 1.3), ("B", "X", 0.7));

        var forward = DeckAdvice.Rank(["X"], ["A", "B"], Base(10), lifts)[0].Score;
        var backward = DeckAdvice.Rank(["X"], ["B", "A"], Base(10), lifts)[0].Score;

        Assert.Equal(forward, backward, 10);
    }

    [Fact]
    public void RanksBestFirst()
    {
        var lifts = Lifts(("STRIKE", "GOOD", 2.0), ("STRIKE", "BAD", 0.5));

        var ranked = DeckAdvice.Rank(["BAD", "GOOD", "EVEN"], ["STRIKE"], Base(10), lifts);

        Assert.Equal(["GOOD", "EVEN", "BAD"], ranked.Select(r => r.Id));
    }

    /// <summary>
    /// A card that halves an offer's value is as worth reporting as one that
    /// doubles it, so reasons are ordered by how far they move the number in
    /// either direction — not by whether they argue for it.
    /// </summary>
    [Fact]
    public void ReasonsAreOrderedByHowMuchTheyMove()
    {
        var lifts = Lifts(("MILD", "X", 1.1), ("STRONG_DOWN", "X", 0.25), ("STRONG_UP", "X", 3.0));

        var reasons = DeckAdvice.Rank(["X"], ["MILD", "STRONG_DOWN", "STRONG_UP"], Base(10), lifts)[0].Reasons;

        Assert.Equal("STRONG_DOWN", reasons[0].Held);
        Assert.Equal("STRONG_UP", reasons[1].Held);
        Assert.Equal("MILD", reasons[2].Held);
        Assert.False(reasons[0].Supports);
        Assert.True(reasons[1].Supports);
    }

    /// <summary>
    /// Codex truncates its reasons list at five while scoring with all of them.
    /// Ossuary truncates the explanation too, for readability — but never the
    /// calculation, which is the bug that made their response body less
    /// faithful than an offline reimplementation.
    /// </summary>
    [Fact]
    public void EveryHeldCardScoresEvenWhenReasonsAreTruncated()
    {
        var held = Enumerable.Range(0, 8).Select(i => $"H{i}").ToArray();
        var lifts = new LiftTable(held.ToDictionary(h => (h, "X"), _ => 1.1));

        var offer = DeckAdvice.Rank(["X"], held, Base(10), lifts)[0];

        Assert.Equal(DeckAdvice.MaxReasons, offer.Reasons.Count);
        Assert.Equal(10 * Math.Pow(1.1, 8), offer.Score, 10);
    }

    [Fact]
    public void ASparseTableOnlyCarriesPairsThatMove()
    {
        var lifts = Lifts(("A", "X", 1.5));

        Assert.Equal(1, lifts.Count);
        Assert.Equal(1.5, lifts.Of("A", "X"));
        Assert.Equal(LiftTable.Neutral, lifts.Of("A", "Y"));
        Assert.Equal(LiftTable.Neutral, lifts.Of("B", "X"));
    }
}
