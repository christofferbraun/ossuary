using Ossuary.Grading;
using Xunit;

namespace Ossuary.Grading.Tests;

public class TierBandsTests
{
    private static Rating R(string id, int score, long picks = 10_000)
        => new(id, score, WinRate: 50, PickRate: 50, Picks: picks);

    [Fact]
    public void NormalCurveSharesSumToOne()
    {
        var total = TierBands.NormalCurve.Sum(b => b.Share);
        Assert.Equal(1.0, total, precision: 6);
    }

    [Fact]
    public void NormalCurveIsSymmetric()
    {
        var shares = TierBands.NormalCurve.Select(b => b.Share).ToArray();
        Assert.Equal(shares[0], shares[5], precision: 9); // S vs F
        Assert.Equal(shares[1], shares[4], precision: 9); // A vs D
        Assert.Equal(shares[2], shares[3], precision: 9); // B vs C
    }

    [Fact]
    public void GradesNeverImproveAsScoreFalls()
    {
        var population = Enumerable.Range(0, 500).Select(i => R($"C{i}", i % 101)).ToArray();
        var bands = TierBands.Derive(population);

        var worst = Tier.S;
        for (var score = 100; score >= 0; score--)
        {
            var tier = bands.Grade(score);
            Assert.True(tier >= worst, $"score {score} graded {tier} after {worst}");
            worst = tier;
        }
    }

    [Fact]
    public void EqualScoresAlwaysReceiveEqualGrades()
    {
        // 200 cards across only 4 distinct scores: every boundary lands on a tie.
        var population = Enumerable.Range(0, 200)
            .Select(i => R($"C{i}", new[] { 10, 40, 70, 95 }[i % 4]))
            .ToArray();
        var bands = TierBands.Derive(population);

        foreach (var group in population.GroupBy(r => r.Score))
        {
            var grades = group.Select(bands.Grade).Distinct().ToArray();
            Assert.True(grades.Length == 1, $"score {group.Key} produced grades: {string.Join(",", grades)}");
        }
    }

    [Fact]
    public void ThresholdsAreStrictlyDecreasing()
    {
        var population = Enumerable.Range(0, 500).Select(i => R($"C{i}", i % 101)).ToArray();
        var bands = TierBands.Derive(population);

        var thresholds = bands.Thresholds.Select(t => t.MinScore).ToArray();
        for (var i = 1; i < thresholds.Length; i++)
        {
            Assert.True(thresholds[i] < thresholds[i - 1],
                $"threshold {i} ({thresholds[i]}) is not below {thresholds[i - 1]}");
        }
    }

    [Fact]
    public void LowSampleRatingsDoNotMoveTheBands()
    {
        var trusted = Enumerable.Range(0, 300).Select(i => R($"C{i}", i % 101)).ToArray();
        var withNoise = trusted
            .Concat(Enumerable.Range(0, 40).Select(i => R($"N{i}", 100, picks: 12)))
            .ToArray();

        var before = TierBands.Derive(trusted).Thresholds;
        var after = TierBands.Derive(withNoise).Thresholds;

        Assert.Equal(before, after);
    }

    [Fact]
    public void LowSampleRatingsAreStillGraded()
    {
        var population = Enumerable.Range(0, 300).Select(i => R($"C{i}", i % 101)).ToArray();
        var bands = TierBands.Derive(population);

        // A 12-pick card is excluded from derivation but must still get a grade.
        Assert.Equal(Tier.S, bands.Grade(R("RARE", 100, picks: 12)));
    }

    [Fact]
    public void PopulationWithoutTrustworthySamplesGradesEverythingC()
    {
        var population = Enumerable.Range(0, 50).Select(i => R($"C{i}", i * 2, picks: 5)).ToArray();
        var bands = TierBands.Derive(population);

        Assert.Equal(Tier.C, bands.Grade(0));
        Assert.Equal(Tier.C, bands.Grade(100));
    }

    [Fact]
    public void EmptyPopulationDoesNotThrow()
    {
        var bands = TierBands.Derive([]);
        Assert.Equal(Tier.C, bands.Grade(50));
    }

    [Theory]
    [InlineData(0, Confidence.Low)]
    [InlineData(299, Confidence.Low)]
    [InlineData(300, Confidence.Medium)]
    [InlineData(1999, Confidence.Medium)]
    [InlineData(2000, Confidence.High)]
    public void ConfidenceMatchesSampleSize(long picks, Confidence expected)
        => Assert.Equal(expected, ConfidenceRules.For(picks));
}
