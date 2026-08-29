using System.Runtime.CompilerServices;
using Godot;

namespace Ossuary;

/// <summary>
/// Every line Ossuary writes, tagged so it can be found among everything else
/// the game prints. The log this lands in is
/// <c>%APPDATA%\SlayTheSpire2\logs\godot.log</c> — note that the
/// <c>&lt;STS2&gt;/sts2_stdout.log</c> named by the community docs is not
/// reliably written on this build (see docs/COMPAT.md).
/// </summary>
internal static class Log
{
    private const string Tag = "[Ossuary]";

    internal static void Info(string message) => GD.Print($"{Tag} {message}");

    internal static void Warn(string message) => GD.PushWarning($"{Tag} {message}");

    /// <summary>
    /// Logs an error without rethrowing. Ossuary only ever reads and draws, so a
    /// failure here must degrade the mod, never interrupt someone's run.
    /// </summary>
    internal static void Error(string message, Exception? ex = null, [CallerMemberName] string caller = "")
    {
        var detail = ex is null ? "" : $" — {ex.GetType().Name}: {ex.Message}";
        GD.PushError($"{Tag} {caller}: {message}{detail}");
        if (ex is not null) GD.PrintErr($"{Tag} {ex}");
    }
}
