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
            // Offsets are in the game's 1920x1080 design space, which Godot
            // scales to whatever the window actually is. Placed below the top
            // bar (ends ~y=91) and the relic row (~y=96-139) so it covers
            // neither.
            //
            // Hardcoded coordinates are a placeholder and the thing going native
            // was supposed to avoid. The real panels parent into the containers
            // NGlobalUi already exposes - TopBar, Overlays, CardPreviewContainer
            // - so they sit beside what they annotate and move with it.
            OffsetLeft = 24,
            OffsetTop = 168,
            OffsetRight = 400,
            OffsetBottom = 264,
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

        // The bundled ratings have no UI of their own until M5, so report them
        // here: it is the difference between "the table is embedded" as a claim
        // and as something visible in the running game.
        var table = Ratings.Table;
        var ratings = new Label
        {
            Text = table is null
                ? "ratings unavailable"
                : $"codex v{table.SnapshotVersion} · {table.All(Grading.RatingKind.Card).Count} cards · "
                  + $"{table.All(Grading.RatingKind.Relic).Count} relics · "
                  + $"{table.All(Grading.RatingKind.Potion).Count} potions",
        };
        ratings.AddThemeColorOverride(
            "font_color",
            table is null ? new Color(0.83f, 0.45f, 0.37f) : new Color(0.62f, 0.67f, 0.64f));
        box.AddChild(ratings);

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
