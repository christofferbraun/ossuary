namespace Ossuary.Grading;

/// <summary>A letter grade, best to worst.</summary>
public enum Tier
{
    S,
    A,
    B,
    C,
    D,
    F,
}

/// <summary>
/// How much the sample behind a rating is worth trusting. Thresholds match the
/// ones Reliquary used, so a rating means the same thing in both tools.
/// </summary>
public enum Confidence
{
    /// <summary>Fewer than 300 recorded picks — a rating built on noise.</summary>
    Low,

    /// <summary>300 to 2,000 picks.</summary>
    Medium,

    /// <summary>At least 2,000 picks.</summary>
    High,
}

public static class ConfidenceRules
{
    /// <summary>
    /// Below this, a row is excluded from the band derivation. It is still
    /// graded — it simply does not get a vote in where the bands fall, because
    /// a 53-pick card landing in the top percentile would drag a real threshold
    /// with it.
    /// </summary>
    public const int MinimumPicksForBandDerivation = 300;

    public static Confidence For(long picks) => picks switch
    {
        >= 2000 => Confidence.High,
        >= 300 => Confidence.Medium,
        _ => Confidence.Low,
    };
}
