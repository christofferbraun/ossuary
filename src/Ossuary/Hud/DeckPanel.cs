using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using Ossuary.Deck;
using Ossuary.State;

namespace Ossuary.Hud;

/// <summary>
/// What is left in the draw pile, and how likely you are to see it.
/// </summary>
/// <remarks>
/// Rebuilt from the live piles every frame rather than kept in sync by events.
/// Reading a few dozen object references costs nothing next to rendering a
/// frame, and a tracker that cannot drift out of step with the game is worth
/// more than one that is marginally cheaper — desynchronised counts are the
/// failure mode that makes a deck tracker worse than useless.
/// </remarks>
internal sealed class DeckPanel : HudPanel
{
    private const int MaxRows = 14;

    private readonly OssuarySettings _settings;

    private Label? _header;
    private VBoxContainer? _rows;
    private Label? _types;
    private Label? _empty;

    internal DeckPanel(OssuarySettings settings) : base("deck")
        => _settings = settings;

    protected override Control BuildRoot()
    {
        var panel = new PanelContainer
        {
            Name = "OssuaryDeck",
            OffsetLeft = 24,
            OffsetTop = 300,
            OffsetRight = 420,
            OffsetBottom = 760,
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

        _header = new Label { Text = "DRAW PILE" };
        _header.AddThemeColorOverride("font_color", new Color(0.42f, 0.78f, 0.70f));
        box.AddChild(_header);

        _empty = new Label { Text = "not in combat" };
        _empty.AddThemeColorOverride("font_color", new Color(0.51f, 0.55f, 0.53f));
        box.AddChild(_empty);

        _rows = new VBoxContainer();
        box.AddChild(_rows);

        _types = new Label { Text = "" };
        _types.AddThemeColorOverride("font_color", new Color(0.51f, 0.55f, 0.53f));
        box.AddChild(_types);

        return panel;
    }

    protected override void OnTick(double delta)
    {
        var player = CombatWatcher.LocalPlayer;
        if (player is null)
        {
            ShowIdle("not in combat");
            return;
        }

        var draw = PileReader.Read(player, PileType.Draw);
        if (draw.Count == 0)
        {
            // An empty draw pile is a normal end-of-turn state, not a fault. It
            // reshuffles from the discard, so say what will happen rather than
            // showing nothing.
            var discard = PileReader.Read(player, PileType.Discard).Count;
            ShowIdle(discard > 0 ? $"empty — {discard} reshuffle next draw" : "empty");
            return;
        }

        Render(draw);
    }

    private void Render(IReadOnlyList<TrackedCard> draw)
    {
        if (_rows is null || _header is null || _types is null || _empty is null) return;

        _empty.Visible = false;
        _rows.Visible = true;

        var lookahead = _settings.DrawLookahead;
        _header.Text = $"DRAW PILE  {draw.Count}   ·   odds over {lookahead}";

        var groups = DeckGrouping.Group(draw);
        Fit(groups.Count);

        for (var i = 0; i < _rows.GetChildCount(); i++)
        {
            if (_rows.GetChild(i) is not Label label) continue;

            if (i >= groups.Count)
            {
                label.Visible = false;
                continue;
            }

            var g = groups[i];
            label.Visible = true;
            var name = g.UpgradeLevel > 0 ? $"{g.Title}+" : g.Title;
            var copies = g.Count > 1 ? $"x{g.Count}" : "  ";
            var cost = g.EnergyCost == TrackedCard.XCost ? "X" : g.EnergyCost.ToString();
            label.Text = $"{cost}  {name,-22} {copies,-4} {g.OddsIn(draw.Count, lookahead),6:P0}";
        }

        _types.Text = string.Join("   ", DeckGrouping.ByType(draw).Select(t => $"{t.Type} {t.Count}"));
    }

    private void ShowIdle(string message)
    {
        if (_empty is null || _rows is null || _types is null || _header is null) return;

        _header.Text = "DRAW PILE";
        _empty.Text = message;
        _empty.Visible = true;
        _rows.Visible = false;
        _types.Text = "";
    }

    /// <summary>
    /// Grows the row list to fit, reusing labels rather than rebuilding them.
    /// </summary>
    /// <remarks>
    /// Freeing and allocating a few dozen nodes every frame would churn the
    /// scene tree for no reason; rows are created once and then only have their
    /// text set. Capped so a pathological deck cannot grow the panel without
    /// limit.
    /// </remarks>
    private void Fit(int needed)
    {
        if (_rows is null) return;

        for (var i = _rows.GetChildCount(); i < Math.Min(needed, MaxRows); i++)
        {
            var label = new Label();
            label.AddThemeColorOverride("font_color", new Color(0.89f, 0.91f, 0.89f));
            label.AddThemeFontSizeOverride("font_size", 15);
            _rows.AddChild(label);
        }
    }
}
