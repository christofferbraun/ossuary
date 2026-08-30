namespace Ossuary.Deck;

/// <summary>
/// One card in a pile, reduced to the facts the tracker needs.
/// </summary>
/// <remarks>
/// A flat record rather than the game's <c>CardModel</c>, so the grouping and
/// odds maths can be tested in CI on a machine with no copy of the game. The mod
/// converts at the boundary; everything below this line is arithmetic.
/// </remarks>
/// <param name="Id">The game's card id, e.g. <c>BACKFLIP</c>.</param>
/// <param name="Title">The name as shown on the card.</param>
/// <param name="Type">
/// The card's type as the game names it — <c>Attack</c>, <c>Skill</c>,
/// <c>Power</c>, <c>Curse</c>, <c>Status</c>. Carried as text rather than an
/// enum of our own so a type added by a patch rolls up under its own name
/// instead of being silently bucketed as "other".
/// </param>
/// <param name="UpgradeLevel">0 for unupgraded, 1+ for each upgrade applied.</param>
/// <param name="EnergyCost">
/// What this copy costs right now, which is not always what the card costs:
/// in-combat effects modify individual copies.
/// </param>
public readonly record struct TrackedCard(
    string Id,
    string Title,
    string Type,
    int UpgradeLevel,
    int EnergyCost)
{
    /// <summary>
    /// Cost of a card that costs X. Not a real number, and deliberately not
    /// zero: an X card is not a free card, and grouping it with the zero-cost
    /// copies would be wrong in the one direction that matters.
    /// </summary>
    public const int XCost = -1;

    public bool CostsX => EnergyCost == XCost;
}
