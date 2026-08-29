namespace Ossuary.Grading;

/// <summary>
/// Score thresholds that place a population of ratings onto a normal curve.
/// </summary>
/// <remarks>
/// <para>
/// Spire Codex publishes its own <c>tier</c> field, but its distribution is
/// heavily bottom-loaded: measured against the live v26 snapshot, 30% of cards
/// are F and 26% are D — 56% of the game in the bottom two grades — while S and
/// A together hold 13%. A tier list where more than half of everything is a
/// failing grade does not help anyone choose between two cards.
/// </para>
/// <para>
/// Ossuary re-bands the same underlying scores so the grades fall on a normal
/// curve, using cut points at z = ±1.5 and ±0.75 and the mean. That yields the
/// textbook six-band split:
/// </para>
/// <code>
///   S  6.7%   A 16.0%   B 27.3%   C 27.3%   D 16.0%   F  6.7%
/// </code>
/// <para>
/// Bands are assigned by <em>rank</em>, not by absolute score, so the shape holds
/// no matter how the raw scores are distributed. Thresholds are then snapped to
/// real score values so that equal scores always receive equal grades — which
/// means the realised proportions deviate slightly from the targets wherever
/// scores tie. That deviation is correct: it would be worse to grade two
/// identical cards differently in order to hit a percentage exactly.
/// </para>
/// </remarks>
public sealed class TierBands
{
    /// <summary>
    /// Share of the population in each band, best to worst. These are the areas
    /// under a standard normal curve between z = +∞, +1.5, +0.75, 0, -0.75,
    /// -1.5, -∞.
    /// </summary>
    public static readonly IReadOnlyList<(Tier Tier, double Share)> NormalCurve =
    [
        (Tier.S, 0.0668072),
        (Tier.A, 0.1598202),
        (Tier.B, 0.2733726),
        (Tier.C, 0.2733726),
        (Tier.D, 0.1598202),
        (Tier.F, 0.0668072),
    ];

    /// <summary>Minimum score for each tier, best first and strictly decreasing.</summary>
    private readonly (Tier Tier, int MinScore)[] _thresholds;

    private TierBands((Tier, int)[] thresholds) => _thresholds = thresholds;

    /// <summary>Thresholds this band set resolved to, best tier first.</summary>
    public IReadOnlyList<(Tier Tier, int MinScore)> Thresholds => _thresholds;

    /// <summary>
    /// Derives bands from a population of ratings.
    /// </summary>
    /// <param name="population">
    /// Every rating of one kind (all cards, or all relics, or all potions) for
    /// one cohort. Ratings below
    /// <see cref="ConfidenceRules.MinimumPicksForBandDerivation"/> picks are
    /// excluded from the derivation but are still gradeable afterwards.
    /// </param>
    public static TierBands Derive(IEnumerable<Rating> population)
    {
        ArgumentNullException.ThrowIfNull(population);

        var scores = population
            .Where(r => r.Picks >= ConfidenceRules.MinimumPicksForBandDerivation)
            .Select(r => r.Score)
            .OrderByDescending(s => s)
            .ToArray();

        if (scores.Length == 0)
        {
            // Nothing trustworthy to derive from. Grade everything C rather than
            // inventing a spread — a flat "no opinion" is honest, a fabricated
            // ranking is not.
            return new TierBands([(Tier.C, int.MinValue)]);
        }

        var thresholds = new List<(Tier, int)>();
        var cumulative = 0.0;
        var previous = int.MaxValue;

        foreach (var (tier, share) in NormalCurve)
        {
            cumulative += share;

            // The score at this band's lower rank boundary becomes its inclusive
            // minimum, so every rating sharing that score shares the grade.
            var index = Math.Clamp((int)Math.Round(cumulative * scores.Length) - 1, 0, scores.Length - 1);
            var minScore = scores[index];

            // Ties can push a boundary onto the same score as the band above,
            // which would make this band unreachable. Drop it: a genuinely empty
            // band is the truth about that population, and emitting a threshold
            // that can never match would only make the table lie quietly.
            if (minScore >= previous) continue;

            thresholds.Add((tier, minScore));
            previous = minScore;
        }

        // The worst band always extends to the floor, so nothing is ungraded.
        if (thresholds.Count > 0)
        {
            var (lastTier, _) = thresholds[^1];
            thresholds[^1] = (lastTier, int.MinValue);
        }

        return new TierBands([.. thresholds]);
    }

    /// <summary>The tier a score falls into.</summary>
    public Tier Grade(int score)
    {
        foreach (var (tier, minScore) in _thresholds)
        {
            if (score >= minScore) return tier;
        }

        return Tier.F;
    }

    /// <summary>The tier a rating falls into.</summary>
    public Tier Grade(Rating rating) => Grade(rating.Score);
}
