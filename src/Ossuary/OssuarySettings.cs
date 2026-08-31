using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Ossuary;

/// <summary>
/// Everything the player can change, persisted beside the game's own settings.
/// </summary>
/// <remarks>
/// Stored under <c>user://</c> rather than next to the DLL: the mod directory is
/// replaced wholesale on a Workshop update, and settings that vanish on update
/// are worse than no settings at all.
/// </remarks>
internal sealed class OssuarySettings
{
    private const string Path = "user://ossuary.json";

    /// <summary>Whether the HUD is drawn. Toggled by <see cref="ToggleKey"/>.</summary>
    public bool HudVisible { get; set; } = true;

    /// <summary>
    /// Name of a <see cref="Godot.Key"/> value, e.g. <c>F9</c>. Stored as a name
    /// rather than an integer so the file stays hand-editable.
    /// </summary>
    public string ToggleKey { get; set; } = "F9";

    /// <summary>
    /// Name of a <see cref="Godot.Key"/> value that enters layout mode, in which
    /// panels can be dragged. Outside layout mode the HUD never takes the mouse.
    /// </summary>
    public string LayoutKey { get; set; } = "F10";

    /// <summary>
    /// Where the player has dragged each panel, keyed by panel name. Absent
    /// entries keep the panel's built-in default, so adding a panel later does
    /// not require touching this file.
    /// </summary>
    public Dictionary<string, PanelPlacement> Panels { get; set; } = new();

    /// <summary>
    /// How many cards ahead the draw odds look when the real draw is unknown.
    /// Five is a standard hand; the tracker uses the observed draw once it has
    /// seen a turn begin.
    /// </summary>
    public int DrawLookahead { get; set; } = 5;

    /// <summary>
    /// Multiplier on every panel's text, so the HUD can be made to fit the
    /// player's screen and taste. Clamped on read rather than on write, so a
    /// hand-edited file cannot produce an unreadable HUD with no way back.
    /// </summary>
    public double TextScale { get; set; } = 1.0;

    [JsonIgnore]
    public double ClampedTextScale => Math.Clamp(TextScale, 0.5, 2.5);

    /// <summary>
    /// Shows the community grade on cards, relics and potions you are offered.
    /// Off means Ossuary adds nothing to the game's own nodes at all.
    /// </summary>
    public bool OfferRatings { get; set; } = true;

    /// <summary>
    /// Shows whether anyone in a co-op party can apply Vulnerable or Weak.
    /// </summary>
    public bool TeamPanel { get; set; } = true;

    /// <summary>
    /// Shows the party panel in single player too.
    /// </summary>
    /// <remarks>
    /// Off by default. The panel exists because in co-op everyone assumes
    /// somebody else picked up Vulnerable; alone there is no somebody else, and
    /// the answer is one you already know from your own deck.
    /// </remarks>
    public bool TeamPanelInSinglePlayer { get; set; }

    /// <summary>
    /// Adds a panel whose only purpose is to throw, proving that one failing
    /// panel disables itself and leaves the rest of the HUD running. Off by
    /// default; this is a development aid, not a feature.
    /// </summary>
    public bool CanaryPanel { get; set; }

    [JsonIgnore]
    public Key ToggleKeyCode =>
        Enum.TryParse<Key>(ToggleKey, ignoreCase: true, out var key) ? key : Key.F9;

    [JsonIgnore]
    public Key LayoutKeyCode =>
        Enum.TryParse<Key>(LayoutKey, ignoreCase: true, out var key) ? key : Key.F10;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Reads the settings file, falling back to defaults for anything missing or
    /// malformed. Never throws: bad settings must not cost someone their run.
    /// </summary>
    internal static OssuarySettings Load()
    {
        try
        {
            if (!FileAccess.FileExists(Path))
            {
                var fresh = new OssuarySettings();
                fresh.Save();
                Log.Info($"settings: wrote defaults to {Path}");
                return fresh;
            }

            using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Read);
            if (file is null)
            {
                Log.Warn($"settings: could not open {Path} ({FileAccess.GetOpenError()}); using defaults");
                return new OssuarySettings();
            }

            var settings = JsonSerializer.Deserialize<OssuarySettings>(file.GetAsText(), Options);
            if (settings is null)
            {
                Log.Warn("settings: file was empty; using defaults");
                return new OssuarySettings();
            }

            Log.Info($"settings: loaded (toggle={settings.ToggleKey}, visible={settings.HudVisible})");
            return settings;
        }
        catch (Exception ex)
        {
            Log.Error("settings: could not be read; using defaults", ex);
            return new OssuarySettings();
        }
    }

    internal void Save()
    {
        try
        {
            using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
            if (file is null)
            {
                Log.Warn($"settings: could not write {Path} ({FileAccess.GetOpenError()})");
                return;
            }

            file.StoreString(JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex)
        {
            Log.Error("settings: could not be saved", ex);
        }
    }
}

/// <summary>Where a panel sits, and whether it is switched on.</summary>
/// <remarks>
/// Absent entries keep the panel's built-in default, so adding a panel later
/// does not require touching a settings file that predates it - and an older
/// file simply has no <c>hidden</c> key, which deserialises to false, meaning
/// every panel stays on. Upgrading cannot silently switch something off.
/// </remarks>
internal sealed class PanelPlacement
{
    public float X { get; set; }
    public float Y { get; set; }

    /// <summary>Switched off by the player in layout mode.</summary>
    public bool Hidden { get; set; }
}
