using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;

namespace Ossuary.State;

/// <summary>
/// Keeps hold of the combat currently being played, so panels can read it.
/// </summary>
/// <remarks>
/// <para>
/// Registered through <c>ModHelper.SubscribeForCombatStateHooks</c>, which is
/// the game's own mechanism for a mod to take part in hook dispatch. The
/// delegate is handed the live <see cref="CombatState"/> precisely so it can
/// decide what to contribute, and it is invoked on every hook dispatch — many
/// times a turn — so noting the state here keeps it current without polling
/// anything.
/// </para>
/// <para>
/// The one model Ossuary contributes is <see cref="DrawObserver"/>, which
/// overrides only hooks that report what happened and returns no modified value
/// from any of them. It cannot alter a run; it exists because the number of
/// cards drawn at the start of a turn is computed and discarded rather than
/// stored anywhere readable.
/// </para>
/// <para>
/// This is why the deck tracker needs no Harmony patch. The one patch Ossuary
/// has is still the HUD attach.
/// </para>
/// </remarks>
internal static class CombatWatcher
{
    private static readonly MegaCrit.Sts2.Core.Models.AbstractModel[] Listeners = [new DrawObserver()];

    private static CombatState? _current;
    private static bool _everSeen;

    /// <summary>The combat in progress, or null outside one.</summary>
    internal static ICombatState? Current => _current;

    /// <summary>
    /// The player whose piles the tracker shows.
    /// </summary>
    /// <remarks>
    /// The first player in the combat. Correct for single player, which is what
    /// v1 targets; in co-op this would need the local player rather than the
    /// first, and there is a <c>NetId</c> to match on when that matters.
    /// </remarks>
    internal static Player? LocalPlayer
    {
        get
        {
            var players = _current?.Players;
            return players is { Count: > 0 } ? players[0] : null;
        }
    }

    internal static void Register()
    {
        try
        {
            ModHelper.SubscribeForCombatStateHooks(ModEntry.ModId, Observe);
            Log.Info("combat watcher registered");
        }
        catch (Exception ex)
        {
            Log.Error("combat watcher could not register; the deck tracker will stay empty", ex);
        }
    }

    private static IEnumerable<MegaCrit.Sts2.Core.Models.AbstractModel> Observe(CombatState state)
    {
        _current = state;

        if (!_everSeen)
        {
            _everSeen = true;
            Log.Info($"combat observed — {state.Players.Count} player(s)");
        }

        return Listeners;
    }
}
