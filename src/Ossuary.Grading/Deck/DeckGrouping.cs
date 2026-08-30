namespace Ossuary.Deck;

/// <summary>
/// Cards that are the same card, collapsed into one row.
/// </summary>
/// <param name="Title">The name shown for the row.</param>
/// <param name="Type">The card's type, for roll-ups.</param>
/// <param name="UpgradeLevel">Shared upgrade level of every copy in the row.</param>
/// <param name="EnergyCost">Shared cost of every copy in the row.</param>
/// <param name="Count">How many copies are in the pile.</param>
public readonly record struct CardGroup(
    string Title,
    string Type,
    int UpgradeLevel,
    int EnergyCost,
    int Count)
{
    /// <summary>The chance of seeing at least one copy in the next N draws.</summary>
    public double OddsIn(int pileSize, int draws) => DrawOdds.AtLeastOne(Count, pileSize, draws);
}

/// <summary>
/// Collapses a pile into the rows a player actually wants to read.
/// </summary>
public static class DeckGrouping
{
    /// <summary>
    /// Groups a pile by what makes two copies interchangeable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The key is title, upgrade level and current cost together — not the card
    /// id. Two Strikes are one row; a Strike and a Strike+ are not, because the
    /// player is choosing between different cards. Cost is in the key because
    /// in-combat effects modify individual copies, and a Skill that now costs 0
    /// is not interchangeable with the copy that still costs 1.
    /// </para>
    /// <para>
    /// Ordering is deliberate and stable: by cost, then title, then upgrade
    /// level. A list that reordered itself as the pile changed would be
    /// unreadable mid-turn, which is exactly when it is read.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<CardGroup> Group(IEnumerable<TrackedCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        return cards
            .GroupBy(c => (c.Title, c.UpgradeLevel, c.EnergyCost))
            .Select(g => new CardGroup(
                Title: g.Key.Title,
                Type: g.First().Type,
                UpgradeLevel: g.Key.UpgradeLevel,
                EnergyCost: g.Key.EnergyCost,
                Count: g.Count()))
            .OrderBy(g => g.EnergyCost)
            .ThenBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.UpgradeLevel)
            .ToList();
    }

    /// <summary>
    /// Counts by card type, highest first, for the summary line.
    /// </summary>
    /// <remarks>
    /// Types come from the game as text, so a type introduced by a patch appears
    /// under its own name rather than vanishing into an "other" bucket we would
    /// have to remember to update.
    /// </remarks>
    public static IReadOnlyList<(string Type, int Count)> ByType(IEnumerable<TrackedCard> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);

        return cards
            .GroupBy(c => c.Type, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Type: g.Key, Count: g.Count()))
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Type, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
