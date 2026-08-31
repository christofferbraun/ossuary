using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace Ossuary;

/// <summary>
/// The single entry point the game's mod loader calls.
/// </summary>
/// <remarks>
/// The loader falls back to an automatic <c>Harmony.PatchAll</c> when an
/// assembly declares no initializer, but an explicit one is used here so that
/// startup logging, the compatibility check, and patching all happen in a known
/// order and in a place a stack trace can point at.
/// </remarks>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string ModId = "Ossuary";
    public const string HarmonyId = "com.christofferbraun.ossuary";

    /// <summary>This build of Ossuary, e.g. <c>0.1.0</c>.</summary>
    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

    /// <summary>
    /// Player settings, read once at startup. Panels read this rather than
    /// touching disk, so a slow or missing file costs one read, not one a frame.
    /// </summary>
    internal static OssuarySettings Settings { get; private set; } = new();

    private static Harmony? _harmony;

    public static void Initialize()
    {
        // Nothing in here may throw into the loader: an exception during
        // initialization arrives wrapped in a TargetInvocationException and, on
        // some paths, takes more than just this mod down with it.
        try
        {
            Log.Info($"v{Version} initializing…");

            CheckGameVersion();
            Settings = OssuarySettings.Load();
            Ratings.Load();
            State.CombatWatcher.Register();

            _harmony = new Harmony(HarmonyId);
            _harmony.PatchAll(typeof(ModEntry).Assembly);

            var patched = _harmony.GetPatchedMethods().Count();
            Log.Info($"ready — {patched} patched method(s)");
        }
        catch (Exception ex)
        {
            Log.Error("initialization failed; Ossuary is inactive for this session", ex);
        }
    }

    private static void CheckGameVersion()
    {
        var game = GameVersion.Read();
        if (game is null)
        {
            Log.Warn("could not determine the game version; assuming compatibility");
            return;
        }

        if (game.IsVerified)
        {
            Log.Info($"game {game.Version} ({game.Commit}) — verified build");
            return;
        }

        Log.Warn(
            $"game {game.Version} ({game.Commit}) has not been verified against this "
            + $"build of Ossuary (built for {GameVersion.VerifiedAgainst}). "
            + "Panels that cannot read the game will disable themselves individually.");
    }
}
