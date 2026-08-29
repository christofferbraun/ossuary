using Godot;

namespace Ossuary.Hud;

/// <summary>
/// The M1 placeholder: proves the HUD attaches, draws, and updates every frame.
/// It is replaced by the real deck tracker in M3.
/// </summary>
internal sealed class StatusPanel : HudPanel
{
    private Label? _label;
    private double _elapsed;
    private int _frames;

    internal StatusPanel() : base("status") { }

    protected override Control BuildRoot()
    {
        var panel = new PanelContainer
        {
            Name = "OssuaryStatus",
            // Anchored rather than positioned: the game is played at every
            // aspect ratio, and there is no coordinate math to get wrong.
            OffsetLeft = 24,
            OffsetTop = 24,
            OffsetRight = 300,
            OffsetBottom = 96,
        };

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.06f, 0.06f, 0.78f),
            BorderColor = new Color(0.17f, 0.48f, 0.42f, 0.9f),
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        };
        style.SetBorderWidthAll(1);
        style.SetContentMarginAll(10);
        panel.AddThemeStyleboxOverride("panel", style);

        var box = new VBoxContainer();
        panel.AddChild(box);

        var title = new Label { Text = "OSSUARY" };
        title.AddThemeColorOverride("font_color", new Color(0.42f, 0.78f, 0.70f));
        box.AddChild(title);

        _label = new Label { Text = "attaching…" };
        _label.AddThemeColorOverride("font_color", new Color(0.89f, 0.91f, 0.89f));
        box.AddChild(_label);

        return panel;
    }

    protected override void OnTick(double delta)
    {
        _elapsed += delta;
        _frames++;
        if (_label is null) return;

        // Updating from _Process is what proves the generated bridge is live:
        // if _Process were never invoked, this text would never change.
        _label.Text = $"v{ModEntry.Version}  ·  {_elapsed:F1}s  ·  {_frames} frames";
    }
}
