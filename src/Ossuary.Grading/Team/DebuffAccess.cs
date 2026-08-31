namespace Ossuary.Team;

/// <summary>
/// The debuffs a party needs somebody to be able to apply.
/// </summary>
/// <remarks>
/// Only Vulnerable and Weak. They are the two the whole party benefits from and
/// the two a run can plausibly end up with nobody carrying: Vulnerable
/// multiplies everyone's damage, Weak cuts every incoming attack, and both are
/// concentrated in a handful of cards and relics rather than spread across the
/// pool. Strength or Block are not comparable — everyone has some.
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
/// One card, relic or potion that can apply a debuff, and which.
/// </summary>
/// <param name="Title">The name as shown in game.</param>
/// <param name="Kind">Card, relic or potion — a potion is one use, so the
/// distinction matters when reading the answer.</param>
/// <param name="Applies">Which debuffs this source can apply.</param>
public sealed record DebuffSource(string Title, SourceKind Kind, Debuffs Applies);

/// <summary>Where a source of a debuff comes from.</summary>
/// <remarks>
/// Kept apart because they are not equally reliable. A card in the deck is
/// available every combat; a potion is available once, and then never again.
/// Reporting "yes" off a single potion without saying so would be misleading.
/// </remarks>
public enum SourceKind
{
    Card,
    Relic,
    Potion,
}

/// <summary>
/// What one player in the party can bring.
/// </summary>
/// <param name="Name">How to label them — their character, since that is what
/// distinguishes players in co-op and is always known.</param>
/// <param name="IsYou">Whether this is the local player.</param>
/// <param name="Sources">Everything they hold that can apply either debuff.</param>
public sealed record TeamMemberAccess(string Name, bool IsYou, IReadOnlyList<DebuffSource> Sources)
{
    /// <summary>Everything this player can apply, from any source.</summary>
    public Debuffs Available => Sources.Aggregate(Debuffs.None, (all, s) => all | s.Applies);

    /// <summary>
    /// What they can apply repeatably — from cards and relics, not potions.
    /// </summary>
    /// <remarks>
    /// A potion is a single use. Someone whose only Vulnerable is one potion
    /// does not have Vulnerable in any planning sense, and a flag that says
    /// otherwise is worse than no flag.
    /// </remarks>
    public Debuffs Repeatable => Sources
        .Where(s => s.Kind != SourceKind.Potion)
        .Aggregate(Debuffs.None, (all, s) => all | s.Applies);

    /// <summary>Whether <paramref name="debuff"/> is available at all.</summary>
    public bool Has(Debuffs debuff) => (Available & debuff) == debuff;

    /// <summary>Whether it is available every combat rather than once.</summary>
    public bool HasRepeatable(Debuffs debuff) => (Repeatable & debuff) == debuff;

    /// <summary>
    /// How to show this player's answer for one debuff.
    /// </summary>
    public Answer AnswerFor(Debuffs debuff) => HasRepeatable(debuff)
        ? Answer.Yes
        : Has(debuff) ? Answer.PotionOnly : Answer.No;

    /// <summary>
    /// The sources behind one debuff, best first, for a tooltip or a detail row.
    /// </summary>
    /// <remarks>
    /// Cards and relics before potions, then alphabetically, so the useful
    /// answer leads and the ordering does not shuffle between frames.
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
    /// Debuffs that nobody in the party can apply repeatably.
    /// </summary>
    /// <remarks>
    /// The party-level answer is the interesting one: it does not matter which
    /// player brings Vulnerable, only that somebody does. An empty party
    /// reports nothing missing rather than everything, because "nobody has
    /// Vulnerable" is not a useful thing to say about no players.
    /// </remarks>
    public static Debuffs MissingFromParty(IReadOnlyList<TeamMemberAccess> party)
    {
        if (party.Count == 0) return Debuffs.None;

        var covered = party.Aggregate(Debuffs.None, (all, m) => all | m.Repeatable);
        return Debuffs.Both & ~covered;
    }

    /// <summary>
    /// Debuffs somebody has, but only out of a potion.
    /// </summary>
    /// <remarks>
    /// Worth separating from <see cref="MissingFromParty"/>: "you have one
    /// Vulnerable potion and nothing else" is a different situation from "you
    /// have no Vulnerable at all", and collapsing them loses the distinction
    /// exactly when it matters.
    /// </remarks>
    public static Debuffs PotionOnlyForParty(IReadOnlyList<TeamMemberAccess> party)
    {
        if (party.Count == 0) return Debuffs.None;

        var any = party.Aggregate(Debuffs.None, (all, m) => all | m.Available);
        var repeatable = party.Aggregate(Debuffs.None, (all, m) => all | m.Repeatable);
        return any & ~repeatable;
    }
}
