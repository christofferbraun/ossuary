namespace Ossuary.Advice;

/// <summary>
/// One offered card, scored against the deck actually held.
/// </summary>
/// <param name="Id">The card's id.</param>
/// <param name="Base">Its score in the abstract, before the deck is considered.</param>
/// <param name="Score">Its score given this deck.</param>
/// <param name="Reasons">
/// The held cards that moved the score most, strongest first. This is the part
/// a player can argue with, which is why it is carried rather than just the
/// number.
/// </param>
public readonly record struct ScoredOffer(
    string Id,
    double Base,
    double Score,
    IReadOnlyList<AdviceReason> Reasons)
{
    /// <summary>How much the deck changed the verdict, as a multiplier.</summary>
    public double DeckFactor => Base <= 0 ? LiftTable.Neutral : Score / Base;
}

/// <summary>One held card's contribution to an offered card's score.</summary>
/// <param name="Held">The card already in the deck.</param>
/// <param name="Lift">Its factor. Above 1 argues for the offer, below 1 against.</param>
public readonly record struct AdviceReason(string Held, double Lift)
{
    public bool Supports => Lift > LiftTable.Neutral;
}

/// <summary>
/// Scores an offer against a deck, offline.
/// </summary>
/// <remarks>
/// This is the v2 feature: grades that account for the deck you actually have,
/// rather than a card's standing in the abstract. It needs no network access at
/// play time for the same reason the tier list does not — the model is
/// harvested once at build time and bundled.
/// </remarks>
public static class DeckAdvice
{
    /// <summary>
    /// The most reasons to report for one offer.
    /// </summary>
    /// <remarks>
    /// Five, matching what Codex's own response shows. Not a limit on the
    /// scoring — every held card contributes to the number, and truncating the
    /// explanation rather than the calculation is precisely the bug found in
    /// their API.
    /// </remarks>
    public const int MaxReasons = 5;

    /// <summary>
    /// Scores each offered card against the deck, best first.
    /// </summary>
    /// <param name="offered">Card ids being offered.</param>
    /// <param name="deck">
    /// Everything already held. Deduplicated, because the model is
    /// duplicate-insensitive: a second copy of a card does not double its
    /// influence.
    /// </param>
    /// <param name="baseScore">A card's score before the deck is considered.</param>
    /// <param name="lifts">The pairwise model.</param>
    public static IReadOnlyList<ScoredOffer> Rank(
        IEnumerable<string> offered,
        IEnumerable<string> deck,
        Func<string, double> baseScore,
        LiftTable lifts)
    {
        ArgumentNullException.ThrowIfNull(offered);
        ArgumentNullException.ThrowIfNull(deck);
        ArgumentNullException.ThrowIfNull(baseScore);
        ArgumentNullException.ThrowIfNull(lifts);

        var held = deck.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return offered
            .Select(id => Score(id, held, baseScore, lifts))
            .OrderByDescending(o => o.Score)
            .ThenBy(o => o.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static ScoredOffer Score(
        string id, HashSet<string> held, Func<string, double> baseScore, LiftTable lifts)
    {
        var start = baseScore(id);
        var score = start;
        var reasons = new List<AdviceReason>();

        foreach (var card in held)
        {
            var lift = lifts.Of(card, id);
            if (lift == LiftTable.Neutral) continue;

            score *= lift;
            reasons.Add(new AdviceReason(card, lift));
        }

        // Strongest movers first, in either direction: a card that halves the
        // value of an offer is as worth knowing about as one that doubles it.
        reasons.Sort((a, b) => Math.Abs(Math.Log(b.Lift)).CompareTo(Math.Abs(Math.Log(a.Lift))));
        if (reasons.Count > MaxReasons) reasons.RemoveRange(MaxReasons, reasons.Count - MaxReasons);

        return new ScoredOffer(id, start, score, reasons);
    }
}
