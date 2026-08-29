using Ossuary.Grading;
using Xunit;

namespace Ossuary.Grading.Tests;

/// <summary>
/// Verifies the banding against the real Spire Codex card table (snapshot v26,
/// 1,416,903 runs) rather than synthetic data, because the property that matters
/// — that the grades land on a normal curve — is a claim about this population.
/// </summary>
public class RealSnapshotTests
{
    private static Rating[] LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "codex-cards-v26.csv");
        return File.ReadAllLines(path)
            .Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(','))
            .Select(f => new Rating(f[0], int.Parse(f[1]), WinRate: 0, PickRate: null, Picks: long.Parse(f[2])))
            .ToArray();
    }

    [Fact]
    public void FixtureIsTheExpectedSnapshot()
    {
        var ratings = LoadFixture();
        Assert.Equal(520, ratings.Length);
    }

    [Fact]
    public void GradeDistributionApproximatesTheNormalCurve()
    {
        var ratings = LoadFixture();
        var bands = TierBands.Derive(ratings);

        var counts = ratings.GroupBy(bands.Grade).ToDictionary(g => g.Key, g => g.Count());

        foreach (var (tier, targetShare) in TierBands.NormalCurve)
        {
            var actualShare = counts.GetValueOrDefault(tier, 0) / (double)ratings.Length;

            // Integer scores tie heavily, so exact proportions are unreachable —
            // and chasing them would mean splitting equal scores. Four points of
            // slack absorbs the ties while still failing loudly if a band
            // collapses or runs away.
            Assert.True(Math.Abs(actualShare - targetShare) <= 0.04,
                $"{tier}: expected ~{targetShare:P1}, got {actualShare:P1}");
        }
    }

    [Fact]
    public void NoGradeSwallowsMoreThanAThirdOfTheCards()
    {
        // The defect being fixed: Spire Codex's own tiers put 30% in F and 26%
        // in D. Any band above a third means the curve has collapsed again.
        var ratings = LoadFixture();
        var bands = TierBands.Derive(ratings);

        foreach (var group in ratings.GroupBy(bands.Grade))
        {
            var share = group.Count() / (double)ratings.Length;
            Assert.True(share <= 0.333, $"{group.Key} holds {share:P1} of all cards");
        }
    }

    [Fact]
    public void EveryTierIsReachable()
    {
        var ratings = LoadFixture();
        var bands = TierBands.Derive(ratings);
        var used = ratings.Select(bands.Grade).Distinct().ToArray();

        Assert.Equal(6, used.Length);
    }
}
