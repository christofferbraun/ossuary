using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using Ossuary.Deck;

namespace Ossuary.State;

/// <summary>
/// Reads a pile out of the running game and flattens it for the tracker.
/// </summary>
/// <remarks>
/// The whole read is <c>CardPile.Get(type, player).Cards</c> — a public static
/// accessor over live objects. Nothing is scanned, diffed, or inferred, which is
/// the entire reason for building this as a mod rather than as an overlay: an
/// external tool has to reconstruct this from process memory and then decide
/// whether it believes what it read.
/// </remarks>
internal static class PileReader
{
    /// <summary>
    /// Reads one pile, or an empty list if it cannot be read.
    /// </summary>
    /// <remarks>
    /// Failure is silent by design. This runs every frame, so a logged error
    /// would become thousands of lines; the panel that called it shows an empty
    /// pile, and <see cref="Ossuary.Hud.HudPanel"/> catches anything worse.
    /// </remarks>
    internal static IReadOnlyList<TrackedCard> Read(Player player, PileType type)
    {
        var pile = CardPile.Get(type, player);
        if (pile is null) return [];

        var cards = pile.Cards;
        var result = new List<TrackedCard>(cards.Count);

        foreach (var card in cards)
        {
            if (card is null) continue;
            result.Add(Convert(card));
        }

        return result;
    }

    private static TrackedCard Convert(MegaCrit.Sts2.Core.Models.CardModel card) => new(
        Id: card.Id.ToString(),
        Title: card.Title ?? "?",
        Type: card.Type.ToString(),
        UpgradeLevel: card.CurrentUpgradeLevel,
        EnergyCost: ResolveCost(card));

    /// <summary>
    /// What this copy costs right now.
    /// </summary>
    /// <remarks>
    /// <c>GetResolved</c> rather than the canonical cost, because in-combat
    /// effects modify individual copies and two copies at different costs are
    /// not interchangeable to someone deciding what to play. An X card has no
    /// resolved cost to report, so it carries a sentinel and is rendered as X.
    /// </remarks>
    private static int ResolveCost(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        var cost = card.EnergyCost;
        if (cost is null) return 0;
        return cost.CostsX ? TrackedCard.XCost : cost.GetResolved();
    }
}
