using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace Ossuary.State;

/// <summary>
/// Watches how many cards the player actually draws at the start of a turn.
/// </summary>
/// <remarks>
/// <para>
/// There is no field to read this from. The game computes it each turn by
/// dispatching <c>ModifyHandDraw</c> across every model, so relics and powers
/// can raise or lower it, and the result is used and discarded. Calling that
/// dispatch ourselves to ask the question would run other models' modifiers —
/// which is a change to the run, not an observation of it — so the count is
/// observed instead: reset when a hand draw begins, incremented once per card
/// that arrives from it.
/// </para>
/// <para>
/// This model is contributed to hook iteration through <c>ModHelper</c>, which
/// is the supported way for a mod to listen. It overrides only hooks that report
/// what happened and returns no modified value from any of them, so it cannot
/// alter a run. Every override returns a completed task without touching game
/// state.
/// </para>
/// </remarks>
internal sealed class DrawObserver : AbstractModel
{
    private int _counting;

    /// <summary>
    /// Opts in to combat hooks. This is the game's own switch for whether a
    /// model is dispatched to during a fight, and the draw happens in combat.
    /// </summary>
    public override bool ShouldReceiveCombatHooks => true;

    /// <summary>
    /// Cards drawn in the most recent hand draw, or null until one is seen.
    /// </summary>
    /// <remarks>
    /// Null rather than a default of five, so the panel can say it is using an
    /// assumption instead of quietly presenting a guess as a measurement.
    /// </remarks>
    internal static int? LastHandDraw { get; private set; }

    internal static void Reset() => LastHandDraw = null;

    public override Task BeforeHandDraw(Player player, PlayerChoiceContext choiceContext, ICombatState combatState)
    {
        _counting = 0;
        return Task.CompletedTask;
    }

    public override Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
    {
        // Cards drawn by a played card are not part of the turn-start draw, and
        // counting them would inflate next turn's estimate.
        if (fromHandDraw) LastHandDraw = ++_counting;
        return Task.CompletedTask;
    }
}
