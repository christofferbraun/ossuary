using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace Ossuary.Hud;

/// <summary>
/// Attaches the HUD when a run's root node enters the scene tree.
/// </summary>
/// <remarks>
/// Recorded in <c>docs/COMPAT.md</c>. No semantic hook was sufficient: the 146
/// hooks on <c>Hook</c> report gameplay events to models, and none of them hands
/// out the scene node the HUD has to be parented to. This is the one patch the
/// HUD needs, and it is a postfix that only reads <c>__instance</c> — it changes
/// no argument, no return value, and no game state.
/// </remarks>
[HarmonyPatch(typeof(NRun), nameof(NRun._Ready))]
internal static class NRunPatch
{
    private static void Postfix(NRun __instance) =>
        HudController.Attach(__instance, ModEntry.Settings);
}
