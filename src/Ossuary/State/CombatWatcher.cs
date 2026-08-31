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
/// Ossuary contributes no models. It reads and draws; adding a model to the
/// game's hook iteration would be a change to the run rather than an
/// observation of it. The subscription exists solely to learn which combat is
/// live.
/// </para>
/// <para>
/// This is why the deck tracker needs no Harmony patch. The one patch Ossuary
/// has is still the HUD attach.
/// </para>
/// </remarks>
internal static class CombatWatcher
{
    private static readonly MegaCrit.Sts2.Core.Models.AbstractModel[] None = [];

    private static CombatState? _current;
    private static bool _everSeen;

    /// <summary>The combat in progress, or null outside one.</summary>
    /// <remarks>
    /// The subscription only ever hands us a state; nothing tells us when that
    /// combat ended, so the last one would otherwise linger and the panels would
    /// keep reporting a fight that is over. <c>CombatManager.IsOverOrEnding</c>
    /// is the game's own signal — it is what <c>Hook.IterateCombatHookListeners</c>
    /// checks before dispatching. Note that <c>ICombatState.IsLiveCombat()</c>
    /// looks like the right answer and is not: on <c>CombatState</c> it returns
    /// a constant <c>true</c>.
    /// </remarks>
    internal static ICombatState? Current
    {
        get
        {
            if (_current is null) return null;

            var manager = CombatManager.Instance;
            return manager is null || manager.IsOverOrEnding ? null : _current;
        }
    }

    /// <summary>
    /// Forgets the previous combat and clears any latched failure.
    /// </summary>
    /// <remarks>
    /// Called when a run starts. Everything here is static because there is one
    /// combat at a time, but a session plays many runs, and a failure latched in
    /// run one should not silently disable a feature for the rest of the
    /// session.
    /// </remarks>
    internal static void Reset()
    {
        _current = null;
        _everSeen = false;
    }

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

        return None;
    }
}
