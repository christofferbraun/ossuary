using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using Ossuary.Deck;
using Ossuary.State;

namespace Ossuary.Hud;

/// <summary>
/// What is left in the draw pile, and how likely you are to see it next turn.
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
    /// Rows past this are summarised rather than listed. A tracker taller than
    /// the screen has stopped being readable, and the tail of a long pile is the
    /// part nobody is deciding on.
    /// </summary>
    private const int MaxRows = 14;

    private const int Columns = 4;

    private static readonly Color Ink = new(0.89f, 0.91f, 0.89f);
    private static readonly Color Dim = new(0.51f, 0.55f, 0.53f);
    private static readonly Color Accent = new(0.42f, 0.78f, 0.70f);
    private static readonly Color Amber = new(0.82f, 0.63f, 0.35f);

    private readonly OssuarySettings _settings;

    private Label? _header;
    private Label? _idle;
    private GridContainer? _drawGrid;
    private Label? _reshuffleHeader;
    private GridContainer? _reshuffleGrid;
    private Label? _types;

    private int _layoutKey = -1;

    internal DeckPanel(OssuarySettings settings) : base("deck")
        => _settings = settings;

    protected override Control BuildRoot()
    {
        // Position only: no explicit size. The panel is collapsed onto its
        // contents whenever the shape changes, so four rows draw a four-row box.
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

        _drawGrid = MakeGrid();
        box.AddChild(_drawGrid);

        _reshuffleHeader = MakeLabel("", Amber, 14);
        box.AddChild(_reshuffleHeader);

        _reshuffleGrid = MakeGrid();
        box.AddChild(_reshuffleGrid);

        _types = MakeLabel("", Dim, 14);
        box.AddChild(_types);

        return panel;
    }

    private static GridContainer MakeGrid()
    {
        // A grid rather than padded text: columns line up under a proportional
        // font, which space-padding cannot do.
        var grid = new GridContainer { Columns = Columns, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 2);
        return grid;
    }

    protected override void OnTick(double delta)
    {
        var state = CombatWatcher.Current;
        var player = CombatWatcher.LocalPlayer;
        if (state is null || player is null)
        {
            // Nothing to say outside a fight, so take up no room.
            if (HideUnlessArranging()) ShowIdle("not in combat");
            return;
        }

        var draw = PileReader.Read(player, PileType.Draw);
        var discard = PileReader.Read(player, PileType.Discard);
        if (draw.Count == 0 && discard.Count == 0)
        {
            if (HideUnlessArranging()) ShowIdle("no cards left");
            return;
        }

        if (Root is not null) Root.Visible = true;

        Render(draw, discard, DrawEstimator.Estimate(state, player));
    }

    private void Render(IReadOnlyList<TrackedCard> draw, IReadOnlyList<TrackedCard> discard, int? estimate)
    {
        if (_header is null || _idle is null || _drawGrid is null
            || _reshuffleHeader is null || _reshuffleGrid is null || _types is null) return;

        _idle.Visible = false;

        // A null estimate means the game would not tell us; say so with a tilde
        // rather than presenting the fallback as a measurement.
        var expected = estimate ?? _settings.DrawLookahead;
        var qualifier = estimate is null ? "~" : "";
        _header.Text = $"DRAW PILE  {draw.Count}   ·   draws {qualifier}{expected}";

        var drawGroups = DeckGrouping.Group(draw);
        var drawRows = Fill(_drawGrid, drawGroups, draw.Count, expected);
        _drawGrid.Visible = drawRows > 0;

        // Everything left in the draw pile comes out before the discard is
        // reshuffled, so when the draw runs short the rest of the hand comes
        // from the discard — at real odds, not certainty. Kept as its own
        // section so the certain cards stay legible at the top.
        var spill = expected - draw.Count;
        var reshuffling = spill > 0 && discard.Count > 0;

        var reshuffleRows = 0;
        if (reshuffling)
        {
            _reshuffleHeader.Text = $"AFTER RESHUFFLE  {discard.Count}   ·   {spill} more";
            var discardGroups = DeckGrouping.Group(discard);
            reshuffleRows = Fill(_reshuffleGrid, discardGroups, discard.Count, spill);
        }

        _reshuffleHeader.Visible = reshuffling;
        _reshuffleGrid.Visible = reshuffling && reshuffleRows > 0;

        _types.Text = string.Join("   ", DeckGrouping.ByType(draw).Select(t => $"{t.Type} {t.Count}"));

        // Only when the shape changes: resizing every frame would fight a player
        // dragging the panel.
        var key = (drawRows * 397) ^ (reshuffling ? reshuffleRows + 1 : 0);
        if (_layoutKey != key)
        {
            _layoutKey = key;
            ShrinkToFit();
        }
    }

    /// <summary>
    /// Writes one section, returning how many rows are showing.
    /// </summary>
    private int Fill(GridContainer grid, IReadOnlyList<CardGroup> groups, int pileSize, int draws)
    {
        var rows = Math.Min(groups.Count, MaxRows);
        Grow(grid, rows);

        for (var row = 0; row < grid.GetChildCount() / Columns; row++)
        {
            var visible = row < rows;
            for (var c = 0; c < Columns; c++) ((Label)grid.GetChild(row * Columns + c)).Visible = visible;
            if (!visible) continue;

            var g = groups[row];
            Cell(grid, row, 0).Text = g.EnergyCost == TrackedCard.XCost ? "X" : g.EnergyCost.ToString();
            // No upgrade marker is added here: CardModel.Title already carries
            // one - "Bash+", and "Searing Blow+3" for a card that can be
            // upgraded more than once - so appending another produced "Bash++".
            Cell(grid, row, 1).Text = g.Title;
            Cell(grid, row, 2).Text = g.Count > 1 ? $"x{g.Count}" : "";
            Cell(grid, row, 3).Text = $"{g.OddsIn(pileSize, draws):P0}";
        }

        return rows;
    }

    private static Label Cell(GridContainer grid, int row, int column) =>
        (Label)grid.GetChild(row * Columns + column);

    private void Grow(GridContainer grid, int rows)
    {
        var added = false;
        for (var row = grid.GetChildCount() / Columns; row < rows; row++)
        {
            grid.AddChild(MakeLabel("", Dim, 15));   // cost
            grid.AddChild(MakeLabel("", Ink, 15));   // title
            grid.AddChild(MakeLabel("", Dim, 15));   // copies
            var odds = MakeLabel("", Ink, 15);
            odds.HorizontalAlignment = HorizontalAlignment.Right;
            grid.AddChild(odds);
            added = true;
        }

        // Cells created after the panel was last scaled still need the scale.
        if (added) ApplyTextScale(_settings.ClampedTextScale);
    }

    private void ShowIdle(string message)
    {
        if (_idle is null || _drawGrid is null || _reshuffleGrid is null
            || _reshuffleHeader is null || _types is null || _header is null) return;
        if (_layoutKey == 0 && _idle.Text == message) return;

        _header.Text = "DRAW PILE";
        _idle.Text = message;
        _idle.Visible = true;
        _drawGrid.Visible = false;
        _reshuffleGrid.Visible = false;
        _reshuffleHeader.Visible = false;
        _types.Text = "";
        _layoutKey = 0;
        ShrinkToFit();
    }
}
