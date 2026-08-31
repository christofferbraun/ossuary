namespace Ossuary.Team;

/// <summary>
/// The debuffs a party needs somebody to be able to apply <em>this turn</em>.
/// </summary>
/// <remarks>
/// Only Vulnerable and Weak. They are the two the whole party benefits from and
/// the two a turn can plausibly arrive with nobody able to apply: both are
/// concentrated in a handful of cards rather than spread across the pool.
/// Strength or Block are not comparable — everyone has some.
/// </remarks>
[Flags]
public enum Debuffs
{
    None = 0,
    Vulnerable = 1,
    Weak = 2,
    Both = Vulnerable | Weak,
}

/// <summary>
/// One thing a player can play right now that would apply a debuff.
/// </summary>
/// <param name="Title">The name as shown in game.</param>
/// <param name="Kind">Where it is — a card held this turn, or a potion.</param>
/// <param name="Applies">Which debuffs playing it would apply.</param>
public sealed record DebuffSource(string Title, SourceKind Kind, Debuffs Applies);

/// <summary>Where something playable this turn is sitting.</summary>
/// <remarks>
/// Kept apart because they are not the same answer. A card in hand is the
/// question being asked. A potion is an escape hatch that is gone once used, so
/// somebody whose only Vulnerable is a potion is in a different position from
/// somebody holding a card that applies it.
///
/// Notably absent: the deck, and relics. Neither answers "this turn". A card
/// three shuffles away is not available now, and a relic that applied Weak at
/// the start of combat has already done it — that is a state the enemy is in,
/// not something the player can choose to do.
/// </remarks>
public enum SourceKind
{
    Hand,
    Potion,
}

/// <summary>
/// What one player in the party can apply this turn.
/// </summary>
/// <param name="Name">How to label them — their character, since that is what
/// distinguishes players in co-op and is always known.</param>
/// <param name="IsYou">Whether this is the local player.</param>
/// <param name="Sources">Everything they could play now that applies a debuff.</param>
public sealed record TeamMemberAccess(string Name, bool IsYou, IReadOnlyList<DebuffSource> Sources)
{
    /// <summary>Everything they could apply this turn, from hand or belt.</summary>
    public Debuffs Available => Sources.Aggregate(Debuffs.None, (all, s) => all | s.Applies);

    /// <summary>
    /// What they are actually holding — cards in hand, not potions.
    /// </summary>
    /// <remarks>
    /// This is the question the panel exists to answer. A potion is a one-shot
    /// that is gone afterwards, so folding it in here would turn "somebody can
    /// apply Vulnerable this turn and every turn they draw it" into the same
    /// answer as "somebody can do it once, ever".
    /// </remarks>
    public Debuffs InHand => Sources
        .Where(s => s.Kind == SourceKind.Hand)
        .Aggregate(Debuffs.None, (all, s) => all | s.Applies);

    /// <summary>Whether <paramref name="debuff"/> is available at all this turn.</summary>
    public bool Has(Debuffs debuff) => (Available & debuff) == debuff;

    /// <summary>Whether it is in hand rather than only in the belt.</summary>
    public bool HasInHand(Debuffs debuff) => (InHand & debuff) == debuff;

    /// <summary>
    /// How to show this player's answer for one debuff.
    /// </summary>
    public Answer AnswerFor(Debuffs debuff) => HasInHand(debuff)
        ? Answer.Yes
        : Has(debuff) ? Answer.PotionOnly : Answer.No;

    /// <summary>
    /// The sources behind one debuff, best first, for a tooltip or a detail row.
    /// </summary>
    /// <remarks>
    /// Hand before belt, then alphabetically, so the useful answer leads and
    /// the ordering does not shuffle between frames.
    /// </remarks>
    public IEnumerable<DebuffSource> SourcesFor(Debuffs debuff) => Sources
        .Where(s => (s.Applies & debuff) == debuff)
        .OrderBy(s => s.Kind == SourceKind.Potion ? 1 : 0)
        .ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase);
}

/// <summary>What to show in a cell.</summary>
public enum Answer
{
    No,
    PotionOnly,
    Yes,
}

/// <summary>
/// Reads a party as a whole.
/// </summary>
public static class TeamDebuffs
{
    /// <summary>
    /// Debuffs nobody in the party is holding a card for this turn.
    /// </summary>
    /// <remarks>
    /// The party-level answer is the interesting one: it does not matter which
    /// player has Vulnerable in hand, only that somebody does. An empty party
    /// reports nothing missing rather than everything, because "nobody has
    /// Vulnerable" is not a useful thing to say about no players — and during
    /// the frames a combat is being set up, that is exactly what the party is.
    /// </remarks>
    public static Debuffs MissingFromParty(IReadOnlyList<TeamMemberAccess> party)
    {
        if (party.Count == 0) return Debuffs.None;

        var covered = party.Aggregate(Debuffs.None, (all, m) => all | m.InHand);
        return Debuffs.Both & ~covered;
    }

    /// <summary>
    /// Debuffs somebody could apply, but only by spending a potion.
    /// </summary>
    /// <remarks>
    /// Worth separating from <see cref="MissingFromParty"/>: "nobody drew it but
    /// somebody is carrying the potion" is a different turn from "nobody can do
    /// it at all", and collapsing them loses the distinction exactly when it
    /// decides whether to spend the potion.
    /// </remarks>
    public static Debuffs PotionOnlyForParty(IReadOnlyList<TeamMemberAccess> party)
    {
        if (party.Count == 0) return Debuffs.None;

        var any = party.Aggregate(Debuffs.None, (all, m) => all | m.Available);
        var inHand = party.Aggregate(Debuffs.None, (all, m) => all | m.InHand);
        return any & ~inHand;
    }
}
