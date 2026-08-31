using Godot;
using Ossuary.State;
using Ossuary.Team;

namespace Ossuary.Hud;

/// <summary>
/// Whether anyone in the party is holding Vulnerable or Weak <em>this turn</em>.
/// </summary>
/// <remarks>
/// <para>
/// The question this answers is one you cannot see the answer to: whether a
/// teammate has drawn a way to apply Vulnerable or Weak. Everybody assumes
/// somebody else has it, nobody can read three other hands, and the turn is
/// planned around a debuff that never lands.
/// </para>
/// <para>
/// Deliberately about the hand rather than the deck. "Somebody owns a card
/// that applies Vulnerable" is true all run and useful on almost none of the
/// turns in it.
/// </para>
/// <para>
/// A potion is reported separately rather than counted as a yes. It is an
/// escape hatch that is gone once used, so it answers a different question from
/// a card sitting in hand.
/// </para>
/// </remarks>
internal sealed class TeamPanel : HudPanel
{
    private const int Columns = 3;
    private const int MaxRows = 4;

    private static readonly Color Ink = new(0.89f, 0.91f, 0.89f);
    private static readonly Color Dim = new(0.51f, 0.55f, 0.53f);
    private static readonly Color Accent = new(0.42f, 0.78f, 0.70f);
    private static readonly Color Yes = new(0.55f, 0.80f, 0.55f);
    private static readonly Color Maybe = new(0.92f, 0.80f, 0.48f);
    private static readonly Color No = new(0.85f, 0.36f, 0.31f);

    private readonly OssuarySettings _settings;

    private Label? _header;
    private Label? _idle;
    private GridContainer? _grid;
    private Label? _summary;

    private int _rows = -1;

    internal TeamPanel(OssuarySettings settings) : base("team") => _settings = settings;

    protected override Control BuildRoot()
    {
        var panel = new PanelContainer
        {
            Name = "OssuaryTeam",
            Position = new Vector2(24, 320),
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

        _header = MakeLabel("PARTY  ·  THIS TURN", Accent);
        box.AddChild(_header);

        _idle = MakeLabel("not in combat", Dim, 15);
        box.AddChild(_idle);

        _grid = new GridContainer { Columns = Columns, MouseFilter = Control.MouseFilterEnum.Ignore };
        _grid.AddThemeConstantOverride("h_separation", 14);
        _grid.AddThemeConstantOverride("v_separation", 2);
        box.AddChild(_grid);

        _summary = MakeLabel("", Dim, 15);
        box.AddChild(_summary);

        return panel;
    }

    protected override void OnTick(double delta)
    {
        var party = TeamReader.Party();

        // In single player the panel answers a question nobody asked: there is
        // no "somebody else" whose hand you cannot see. Layout mode still shows
        // it so it can be positioned before a co-op run starts.
        if (party.Count < 2 && !_settings.TeamPanelInSinglePlayer)
        {
            if (HideUnlessArranging()) ShowIdle(party.Count == 0 ? "not in combat" : "single player");
            return;
        }

        if (party.Count == 0)
        {
            if (HideUnlessArranging()) ShowIdle("not in combat");
            return;
        }

        if (Root is not null) Root.Visible = true;
        Render(party);
    }

    private void Render(IReadOnlyList<TeamMemberAccess> party)
    {
        if (_header is null || _idle is null || _grid is null || _summary is null) return;

        _idle.Visible = false;
        _grid.Visible = true;

        var shown = Math.Min(party.Count, MaxRows);
        Layout(shown + 1);

        Cell(0, 0).Text = "";
        Cell(0, 1).Text = "VULN";
        Cell(0, 2).Text = "WEAK";
        Tint(Cell(0, 1), Dim);
        Tint(Cell(0, 2), Dim);

        for (var i = 0; i < shown; i++)
        {
            var member = party[i];
            var row = i + 1;

            Cell(row, 0).Text = member.IsYou ? $"{member.Name} (you)" : member.Name;
            Tint(Cell(row, 0), member.IsYou ? Ink : Dim);

            Write(Cell(row, 1), member.AnswerFor(Debuffs.Vulnerable));
            Write(Cell(row, 2), member.AnswerFor(Debuffs.Weak));
        }

        _summary.Text = Summarise(party);
    }

    private static void Write(Label cell, Answer answer)
    {
        cell.Text = answer switch
        {
            Answer.Yes => "yes",
            Answer.PotionOnly => "potion",
            _ => "no",
        };

        Tint(cell, answer switch
        {
            Answer.Yes => Yes,
            Answer.PotionOnly => Maybe,
            _ => No,
        });
    }

    private static void Tint(Label label, Color colour) => label.AddThemeColorOverride("font_color", colour);

    /// <summary>
    /// The party-level answer, which is the one that actually matters.
    /// </summary>
    /// <remarks>
    /// It does not matter which player drew Vulnerable, only that somebody did
    /// — so the per-player rows are the detail and this is the finding.
    /// </remarks>
    private static string Summarise(IReadOnlyList<TeamMemberAccess> party)
    {
        var missing = TeamDebuffs.MissingFromParty(party);
        var potionOnly = TeamDebuffs.PotionOnlyForParty(party);

        if (missing == Debuffs.None && potionOnly == Debuffs.None) return "both in hand somewhere";

        var parts = new List<string>(2);
        if (missing != Debuffs.None) parts.Add($"nobody drew {Describe(missing)}");
        if (potionOnly != Debuffs.None) parts.Add($"{Describe(potionOnly)} only from a potion");

        return string.Join("  ·  ", parts);
    }

    private static string Describe(Debuffs debuffs) => debuffs switch
    {
        Debuffs.Both => "Vulnerable or Weak",
        Debuffs.Vulnerable => "Vulnerable",
        _ => "Weak",
    };

    private void ShowIdle(string message)
    {
        if (_idle is null || _grid is null || _summary is null || _header is null) return;

        _header.Text = "PARTY  ·  THIS TURN";
        _idle.Text = message;
        _idle.Visible = true;
        _grid.Visible = false;
        _summary.Text = "";
    }

    /// <summary>
    /// Builds exactly the labels this many rows needs, once.
    /// </summary>
    private void Layout(int rows)
    {
        if (_grid is null || _rows == rows) return;

        foreach (var child in _grid.GetChildren()) child.QueueFree();

        for (var i = 0; i < rows * Columns; i++)
        {
            var label = MakeLabel("", Ink, 15);
            label.MouseFilter = Control.MouseFilterEnum.Ignore;
            _grid.AddChild(label);
        }

        _rows = rows;
        ShrinkToFit();
    }

    private Label Cell(int row, int column) => (Label)_grid!.GetChild(row * Columns + column);
}
