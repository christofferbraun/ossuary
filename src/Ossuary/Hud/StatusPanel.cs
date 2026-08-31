using Godot;

namespace Ossuary.Hud;

/// <summary>
/// Says that Ossuary is loaded, and what data it is working from.
/// </summary>
/// <remarks>
/// This began as the M1 proof that the HUD attached and kept updating, with a
/// running frame counter to show the engine was calling <c>_Process</c> on a
/// mod-defined node. That question is settled, so the counter is gone — a
/// permanent readout whose only job was to prove a one-off is clutter on every
/// frame after it.
///
/// What stays is the part that keeps earning its place: which Codex snapshot is
/// bundled and how much of the game it covers. That is what separates "no rating
/// for this card" meaning a known gap in the data from meaning the table failed
/// to load.
///
/// It also carries the hotkeys, and is the one panel that cannot be switched
/// off. Every other panel can be, and a player who turned them all off would
/// otherwise be left with a HUD that shows nothing and no indication that a key
/// brings it back — the controls have to outlive the things they control.
/// </remarks>
internal sealed class StatusPanel : HudPanel
{
    private readonly OssuarySettings _settings;

    internal StatusPanel(OssuarySettings settings) : base("status") => _settings = settings;

    /// <inheritdoc />
    internal override bool CanHide => false;

    protected override Control BuildRoot()
    {
        var panel = new PanelContainer
        {
            Name = "OssuaryStatus",
            // In the game's 1920x1080 design space, which Godot scales to
            // whatever the window actually is. Below the top bar (ends ~y=91)
            // and the relic row (~y=96-139) so it covers neither.
            Position = new Vector2(24, 168),
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

        box.AddChild(MakeLabel("OSSUARY", new Color(0.42f, 0.78f, 0.70f)));

        var table = Ratings.Table;
        box.AddChild(MakeLabel(
            table is null
                ? "ratings unavailable"
                : $"codex v{table.SnapshotVersion} · {table.All(Grading.RatingKind.Card).Count} cards · "
                  + $"{table.All(Grading.RatingKind.Relic).Count} relics · "
                  + $"{table.All(Grading.RatingKind.Potion).Count} potions",
            table is null ? new Color(0.83f, 0.45f, 0.37f) : new Color(0.62f, 0.67f, 0.64f),
            14));

        // The keys, from settings rather than hardcoded, so a player who
        // rebinds them is told what they actually rebound to.
        box.AddChild(MakeLabel(
            $"{_settings.ToggleKey} hide  ·  {_settings.LayoutKey} arrange",
            new Color(0.45f, 0.62f, 0.58f),
            13));

        return panel;
    }
}
