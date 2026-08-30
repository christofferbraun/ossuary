using Godot;
using Ossuary.Combat;
using Ossuary.State;

namespace Ossuary.Hud;

/// <summary>
/// How much damage is coming this turn, and whether it kills you.
/// </summary>
/// <remarks>
/// The numbers come from the game's own damage pipeline rather than from
/// re-implementing it, so strength, vulnerable and weak are already applied. See
/// <see cref="IntentReader"/> for why calling into that pipeline is safe.
/// </remarks>
internal sealed class ForecastPanel : HudPanel
{
    private const int MaxRows = 8;
    private const int Columns = 3;

    private static readonly Color Ink = new(0.89f, 0.91f, 0.89f);
    private static readonly Color Dim = new(0.51f, 0.55f, 0.53f);
    private static readonly Color Accent = new(0.42f, 0.78f, 0.70f);
    private static readonly Color Danger = new(0.85f, 0.36f, 0.31f);
    private static readonly Color Safe = new(0.55f, 0.75f, 0.55f);

    private readonly OssuarySettings _settings;

    private Label? _header;
    private Label? _idle;
    private GridContainer? _grid;
    private Label? _summary;

    private int _layoutKey = -1;

    internal ForecastPanel(OssuarySettings settings) : base("forecast")
        => _settings = settings;

    protected override Control BuildRoot()
    {
        var panel = new PanelContainer
        {
            Name = "OssuaryForecast",
            Position = new Vector2(24, 620),
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

        _header = MakeLabel("INCOMING", Accent);
        box.AddChild(_header);

        _idle = MakeLabel("not in combat", Dim, 15);
        box.AddChild(_idle);

        _grid = new GridContainer { Columns = Columns, MouseFilter = Control.MouseFilterEnum.Ignore };
        _grid.AddThemeConstantOverride("h_separation", 12);
        _grid.AddThemeConstantOverride("v_separation", 2);
        box.AddChild(_grid);

        _summary = MakeLabel("", Dim, 15);
        box.AddChild(_summary);

        return panel;
    }

    protected override void OnTick(double delta)
    {
        var state = CombatWatcher.Current;
        var player = CombatWatcher.LocalPlayer;
        var me = player?.Creature;
        if (state is null || me is null)
        {
            ShowIdle("not in combat");
            return;
        }

        var intents = IntentReader.Read(state, state.PlayerCreatures);
        if (intents.Count == 0)
        {
            ShowIdle("nothing incoming");
            return;
        }

        Render(intents, AttackForecast.Of(intents, me.Block, me.CurrentHp));
    }

    private void Render(IReadOnlyList<IncomingIntent> intents, Forecast forecast)
    {
        if (_header is null || _idle is null || _grid is null || _summary is null) return;

        _idle.Visible = false;
        _grid.Visible = true;

        var hits = forecast.Hits == 1 ? "1 hit" : $"{forecast.Hits} hits";
        _header.Text = $"INCOMING  {forecast.Damage}   ·   {hits}";
        _header.AddThemeColorOverride("font_color", forecast.IsLethal ? Danger : Accent);

        var rows = Math.Min(intents.Count, MaxRows);
        Grow(rows);

        for (var row = 0; row < _grid.GetChildCount() / Columns; row++)
        {
            var visible = row < rows;
            for (var c = 0; c < Columns; c++) Cell(row, c).Visible = visible;
            if (!visible) continue;

            var intent = intents[row];
            Cell(row, 0).Text = intent.Source;

            // "6 x2" reads as two hits of six; a bare "12" hides the shape of
            // the turn, which is what matters when something triggers per hit.
            Cell(row, 1).Text = intent.IsAttack
                ? (intent.Hits > 1 ? $"{intent.DamagePerHit} x{intent.Hits}" : $"{intent.DamagePerHit}")
                : intent.Kind.ToLowerInvariant();

            var total = Cell(row, 2);
            total.Text = intent.IsAttack ? intent.Total.ToString() : "";
            total.HorizontalAlignment = HorizontalAlignment.Right;
        }

        if (forecast.IsLethal)
        {
            _summary.Text = $"LETHAL — {forecast.HpLoss} through {forecast.Block} block, {forecast.CurrentHp} hp";
            _summary.AddThemeColorOverride("font_color", Danger);
        }
        else if (forecast.Damage == 0)
        {
            _summary.Text = "no damage incoming";
            _summary.AddThemeColorOverride("font_color", Safe);
        }
        else
        {
            _summary.Text = forecast.HpLoss == 0
                ? $"blocked — {forecast.Block} block holds"
                : $"lose {forecast.HpLoss}   ·   hp {forecast.CurrentHp} → {forecast.HpAfter}"
                  + $"   ·   {forecast.BlockShortfall} more block to take nothing";
            _summary.AddThemeColorOverride("font_color", forecast.HpLoss == 0 ? Safe : Dim);
        }

        var key = (rows * 397) ^ (forecast.IsLethal ? 1 : 0);
        if (_layoutKey != key)
        {
            _layoutKey = key;
            ShrinkToFit();
        }
    }

    private Label Cell(int row, int column) => (Label)_grid!.GetChild(row * Columns + column);

    private void Grow(int rows)
    {
        if (_grid is null) return;

        var added = false;
        for (var row = _grid.GetChildCount() / Columns; row < rows; row++)
        {
            _grid.AddChild(MakeLabel("", Ink, 15));   // enemy
            _grid.AddChild(MakeLabel("", Dim, 15));   // per hit
            _grid.AddChild(MakeLabel("", Ink, 15));   // total
            added = true;
        }

        if (added) ApplyTextScale(_settings.ClampedTextScale);
    }

    private void ShowIdle(string message)
    {
        if (_idle is null || _grid is null || _summary is null || _header is null) return;
        if (_layoutKey == 0 && _idle.Text == message) return;

        _header.Text = "INCOMING";
        _header.AddThemeColorOverride("font_color", Accent);
        _idle.Text = message;
        _idle.Visible = true;
        _grid.Visible = false;
        _summary.Text = "";
        _layoutKey = 0;
        ShrinkToFit();
    }
}
