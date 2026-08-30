namespace Ossuary.Advice;

/// <summary>
/// How much holding one card changes the value of being offered another.
/// </summary>
/// <remarks>
/// <para>
/// Spire Codex's <c>POST /api/draft-advice</c> looks like an opaque server-side
/// model. Probing established that it is not: the score of an offered card is
/// its base score multiplied by one factor per distinct card already held.
/// </para>
/// <code>
///   score(c) = base(c) · ∏ lift(h, c)   for h in the set of cards held
/// </code>
/// <para>
/// Three properties came out of that probing, and each one is what makes an
/// offline reimplementation possible at all:
/// </para>
/// <list type="bullet">
/// <item><b>Exactly multiplicative</b> — nine of ten offers against a
/// forty-card deck matched to four decimal places. The tenth had the most
/// reasons, which revealed that the API truncates its own reasons list at five
/// while scoring with all of them. An offline implementation is therefore
/// <em>more</em> faithful than the response body.</item>
/// <item><b>Pairwise and context-free</b> — a one-card deck returns the
/// identical lift a twelve-card deck does, so the whole model can be harvested
/// one held card at a time.</item>
/// <item><b>Duplicate-insensitive</b> — one, two or three copies produce the
/// same score. The model dedupes to a set.</item>
/// </list>
/// <para>
/// Stored sparsely. Most pairs have no interaction, and a factor of exactly 1
/// is the same as an absent entry, so only pairs that actually move the number
/// are worth carrying.
/// </para>
/// </remarks>
public sealed class LiftTable
{
    /// <summary>A pair with no recorded interaction leaves the score alone.</summary>
    public const double Neutral = 1.0;

    private readonly Dictionary<(string Held, string Candidate), double> _lifts;

    public LiftTable(IReadOnlyDictionary<(string Held, string Candidate), double> lifts)
    {
        ArgumentNullException.ThrowIfNull(lifts);

        _lifts = new Dictionary<(string, string), double>(lifts.Count);
        foreach (var ((held, candidate), lift) in lifts)
        {
            _lifts[(Normalise(held), Normalise(candidate))] = lift;
        }
    }

    /// <summary>Pairs that actually move a score.</summary>
    public int Count => _lifts.Count;

    /// <summary>
    /// The factor holding <paramref name="held"/> applies to being offered
    /// <paramref name="candidate"/>, or <see cref="Neutral"/> when the pair has
    /// no recorded interaction.
    /// </summary>
    public double Of(string held, string candidate) =>
        _lifts.TryGetValue((Normalise(held), Normalise(candidate)), out var lift) ? lift : Neutral;

    private static string Normalise(string id)
    {
        var span = id.AsSpan().Trim();
        var dot = span.LastIndexOf('.');
        if (dot >= 0) span = span[(dot + 1)..];
        return span.ToString().ToUpperInvariant();
    }
}
