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
    /// <summary>
    /// Rows past this are summarised rather than listed. A tracker that grows
    /// taller than the screen has stopped being readable, and the tail of a long
    /// pile is the part nobody is deciding on.
    /// </summary>
    private const int MaxRows = 16;

    private const int Columns = 4;

    private static readonly Color Ink = new(0.89f, 0.91f, 0.89f);
    private static readonly Color Dim = new(0.51f, 0.55f, 0.53f);
    private static readonly Color Accent = new(0.42f, 0.78f, 0.70f);

    private readonly OssuarySettings _settings;

    private Label? _header;
    private GridContainer? _grid;
    private Label? _types;
    private Label? _idle;
    private int _shownRows = -1;

    internal DeckPanel(OssuarySettings settings) : base("deck")
        => _settings = settings;

    protected override Control BuildRoot()
    {
        // Position only: no explicit size. The panel is collapsed onto its
        // contents after every update, so four rows occupy a four-row box.
        var panel = new PanelContainer
        {
            Name = "OssuaryDeck",
            Position = new Vector2(24, 300),
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

        _header = MakeLabel("DRAW PILE", Accent);
        box.AddChild(_header);

        _idle = MakeLabel("not in combat", Dim, 15);
        box.AddChild(_idle);

        // A grid rather than padded text: columns line up under a proportional
        // font, which space-padding cannot do.
        _grid = new GridContainer { Columns = Columns, MouseFilter = Control.MouseFilterEnum.Ignore };
        _grid.AddThemeConstantOverride("h_separation", 12);
        _grid.AddThemeConstantOverride("v_separation", 2);
        box.AddChild(_grid);

        _types = MakeLabel("", Dim, 14);
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
        if (_grid is null || _header is null || _types is null || _idle is null) return;

        _idle.Visible = false;
        _grid.Visible = true;

        // How many cards the next turn's draw will be. The game recomputes this
        // each turn by dispatching modifiers across every model and keeps no
        // readable copy, so it is taken from the last draw actually seen — which
        // already accounts for whatever relics and powers are in play. Until a
        // draw has been observed, the configured default stands in, and the
        // header says so rather than presenting an assumption as a measurement.
        var observed = DrawObserver.LastHandDraw;
        var lookahead = observed ?? _settings.DrawLookahead;
        var qualifier = observed is null ? "~" : "";
        _header.Text = $"DRAW PILE  {draw.Count}   ·   odds over {qualifier}{lookahead}";

        var groups = DeckGrouping.Group(draw);
        var rows = Math.Min(groups.Count, MaxRows);
        Fit(rows);

        for (var row = 0; row < _grid.GetChildCount() / Columns; row++)
        {
            var visible = row < rows;
            var cells = Cells(row);
            foreach (var cell in cells) cell.Visible = visible;
            if (!visible) continue;

            var g = groups[row];
            cells[0].Text = g.EnergyCost == TrackedCard.XCost ? "X" : g.EnergyCost.ToString();
            cells[1].Text = g.UpgradeLevel > 0 ? $"{g.Title}+" : g.Title;
            cells[2].Text = g.Count > 1 ? $"x{g.Count}" : "";
            cells[3].Text = $"{g.OddsIn(draw.Count, lookahead):P0}";
        }

        var hidden = groups.Count - rows;
        _types.Text = string.Join("   ", DeckGrouping.ByType(draw).Select(t => $"{t.Type} {t.Count}"))
            + (hidden > 0 ? $"   (+{hidden} more)" : "");

        // Only when the row count actually changes: ResetSize every frame would
        // fight a player dragging the panel by its edge.
        if (_shownRows != rows)
        {
            _shownRows = rows;
            ShrinkToFit();
        }
    }

    private void ShowIdle(string message)
    {
        if (_idle is null || _grid is null || _types is null || _header is null) return;
        if (_shownRows == 0 && _idle.Text == message) return;

        _header.Text = "DRAW PILE";
        _idle.Text = message;
        _idle.Visible = true;
        _grid.Visible = false;
        _types.Text = "";
        _shownRows = 0;
        ShrinkToFit();
    }

    private Label[] Cells(int row)
    {
        var cells = new Label[Columns];
        for (var c = 0; c < Columns; c++) cells[c] = (Label)_grid!.GetChild(row * Columns + c);
        return cells;
    }

    /// <summary>
    /// Grows the grid to fit, reusing cells rather than rebuilding them.
    /// </summary>
    private void Fit(int rows)
    {
        if (_grid is null) return;

        for (var row = _grid.GetChildCount() / Columns; row < rows; row++)
        {
            _grid.AddChild(MakeLabel("", Dim, 15));                       // cost
            _grid.AddChild(MakeLabel("", Ink, 15));                       // title
            _grid.AddChild(MakeLabel("", Dim, 15));                       // copies
            var odds = MakeLabel("", Ink, 15);
            odds.HorizontalAlignment = HorizontalAlignment.Right;
            _grid.AddChild(odds);
        }

        // Cells added after the panel was first scaled still need the scale.
        ApplyTextScale(_settings.ClampedTextScale);
    }
}
