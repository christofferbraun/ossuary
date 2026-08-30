using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models.Powers;

namespace Ossuary.State;

/// <summary>
/// How many cards the player will draw at the start of their next turn.
/// </summary>
/// <remarks>
/// <para>
/// Asks the game the same question it asks itself.
/// <c>CombatManager</c> computes the turn's draw as
/// <c>Hook.ModifyHandDraw(state, player, 5m, out _)</c> — a base of five, then
/// every relic and power in play given a chance to change it. Calling that is
/// how the count stays live: gain Machine Learning and the number moves on the
/// next refresh, without Ossuary knowing what Machine Learning is.
/// </para>
/// <para>
/// <b>Why calling a hook is safe here, when it usually would not be.</b> This is
/// a dispatch, and dispatching hooks normally risks running other models' side
/// effects. Every shipped implementation was decompiled and checked before this
/// was written: all seventeen <c>ModifyHandDraw</c> overrides (nine relics,
/// eight powers) and the single <c>ModifyHandDrawLate</c> override (Fiddle) are
/// pure — they read state and return a number, and not one assigns to a field or
/// property. <c>Hook.ModifyHandDraw</c> itself only iterates and accumulates.
/// Recorded in <c>docs/COMPAT.md</c> and worth re-checking after a game update.
/// </para>
/// <para>
/// The residual risk is another mod contributing a model whose override is not
/// pure. It is throttled rather than called per frame partly for that: at worst
/// this doubles or trebles how often such an override runs, instead of
/// multiplying it by the frame rate.
/// </para>
/// </remarks>
internal static class DrawEstimator
{
    /// <summary>The base the game starts from, before any modifier.</summary>
    private const decimal BaseDraw = 5m;

    /// <summary>
    /// How long an estimate is reused. Powers that change the draw are gained
    /// mid-turn rarely, and half a second is imperceptible against that.
    /// </summary>
    private static readonly TimeSpan Freshness = TimeSpan.FromMilliseconds(500);

    private static DateTime _taken = DateTime.MinValue;
    private static int _cached = (int)BaseDraw;
    private static bool _failed;

    /// <summary>
    /// The next turn's draw, or null if it cannot be determined — in which case
    /// callers should say so rather than present <see cref="BaseDraw"/> as fact.
    /// </summary>
    internal static int? Estimate(ICombatState state, Player player)
    {
        if (_failed) return null;

        var now = DateTime.UtcNow;
        if (now - _taken < Freshness) return _cached;

        try
        {
            var count = Hook.ModifyHandDraw(state, player, BaseDraw, out _) + PendingNextTurn(player);

            // The game truncates when it draws, and a modifier could in
            // principle push the count negative.
            _cached = Math.Max(0, (int)count);
            _taken = now;
            return _cached;
        }
        catch (Exception ex)
        {
            // Never ask again this session. A hook that throws once will throw
            // every frame, and the tracker degrades to "unknown" rather than
            // filling the log.
            _failed = true;
            Log.Error("could not read the turn's draw count; odds will assume a default", ex);
            return null;
        }
    }

    /// <summary>
    /// Extra draw already earned this turn that the hook will not yet report.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one avenue the hook query misses, and it is the common one:
    /// playing a card that says "draw N extra cards next turn". Powers carry an
    /// <c>AmountOnTurnStart</c>, and the game's own guard is
    /// <c>if (AmountOnTurnStart == 0) return count;</c> — deliberate, so a power
    /// gained mid-turn cannot apply to a draw that already happened. The
    /// consequence is that between playing such a card and the turn flipping,
    /// asking the hook under-reports by exactly that pending amount.
    /// </para>
    /// <para>
    /// So it is added back. This is an assumption about a specific game type
    /// rather than a contract, which is why it is registered in
    /// <c>docs/COMPAT.md</c> — if the type is renamed this silently stops
    /// applying, and the estimate degrades to the hook's answer rather than
    /// breaking.
    /// </para>
    /// <para>
    /// Effects that cannot be known now are still not known: cards the player
    /// has not played yet, and anything the enemy does on its turn. Those move
    /// the number when they happen, which is the most any estimate can promise.
    /// </para>
    /// </remarks>
    private static int PendingNextTurn(Player player)
    {
        var powers = player.Creature?.Powers;
        if (powers is null) return 0;

        var pending = 0;
        foreach (var power in powers)
        {
            if (power is DrawCardsNextTurnPower && power.AmountOnTurnStart == 0 && power.Amount > 0)
            {
                pending += power.Amount;
            }
        }

        return pending;
    }

    internal static void Reset()
    {
        _taken = DateTime.MinValue;
        _failed = false;
    }
}
