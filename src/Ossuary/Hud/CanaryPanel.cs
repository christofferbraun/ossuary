using Godot;

namespace Ossuary.Hud;

/// <summary>
/// A panel that exists only to fail, so that per-panel isolation is something we
/// have watched work rather than something we believe. Enabled by setting
/// <c>canaryPanel: true</c> in <c>user://ossuary.json</c>.
/// </summary>
internal sealed class CanaryPanel : HudPanel
{
    private int _ticks;

    internal CanaryPanel() : base("canary") { }

    protected override Control BuildRoot()
    {
        var label = new Label { Text = "canary: about to throw" };
        label.AddThemeColorOverride("font_color", new Color(0.83f, 0.45f, 0.37f));
        label.OffsetLeft = 24;
        label.OffsetTop = 252;
        return label;
    }

    protected override void OnTick(double delta)
    {
        // Throw a few frames in, so the failure is visibly *after* a successful
        // build and cannot be confused with a panel that never started.
        if (++_ticks < 120) return;
        throw new InvalidOperationException(
            "deliberate canary failure — if the status panel is still updating, isolation works");
    }
}
