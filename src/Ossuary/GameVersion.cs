using System.Text.Json;
using Godot;

namespace Ossuary;

/// <summary>
/// The build of Slay the Spire 2 we are actually running inside, read from the
/// <c>release_info.json</c> Mega Crit ships in the game root.
/// </summary>
/// <remarks>
/// This exists so a mismatch is <em>reported</em> rather than discovered as a
/// mysterious crash. Ossuary never refuses to load on an unknown build: a stats
/// overlay that bricks itself the day the game patches is worse than one that
/// says it is untested and carries on.
/// </remarks>
internal sealed record GameVersion(string Version, string Commit)
{
    /// <summary>The build this release was developed and verified against.</summary>
    internal const string VerifiedAgainst = "0.107.1";

    internal bool IsVerified => Version == VerifiedAgainst;

    internal static GameVersion? Read()
    {
        try
        {
            // res:// is the PCK; the game root is where the executable lives.
            var root = OS.GetExecutablePath().GetBaseDir();
            var path = System.IO.Path.Combine(root, "release_info.json");
            if (!System.IO.File.Exists(path)) return null;

            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(path));
            var r = doc.RootElement;
            var version = r.TryGetProperty("version", out var v) ? v.GetString() : null;
            var commit = r.TryGetProperty("commit", out var c) ? c.GetString() : null;
            if (version is null) return null;

            // release_info.json writes "v0.107.1"; compare on the bare number.
            return new GameVersion(version.TrimStart('v'), commit ?? "unknown");
        }
        catch (Exception ex)
        {
            Log.Error("could not read release_info.json", ex);
            return null;
        }
    }
}
