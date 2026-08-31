using Godot;

namespace Ossuary.Hud;

/// <summary>
/// The HUD itself: a <see cref="CanvasLayer"/> parented into the run, holding
/// every panel Ossuary draws.
/// </summary>
/// <remarks>
/// This is the only type in Ossuary that subclasses a Godot node, and it exists
/// as a <c>partial</c> class because Godot's source generators complete it with
/// the interop bridge that lets the engine invoke <see cref="_Ready"/>,
/// <see cref="_Process"/> and the input callbacks. If that bridge is ever
/// missing, those methods are silently never called — so attachment and
/// readiness are logged separately, and seeing the first without the second is
/// the signature of that failure.
///
/// It is parented to <c>NRun</c> rather than to the scene root so that its
/// lifetime is the run's lifetime: abandoning a run frees the HUD with it, and
/// the next run builds a fresh one.
/// </remarks>
public partial class HudController : CanvasLayer
{
    private const string NodeName = "OssuaryHud";

    /// <summary>Above the game's own UI, below Godot's absolute ceiling of 128.</summary>
    private const int OverlayLayer = 100;

    private readonly List<HudPanel> _panels = new();
    private Control? _root;
    private Label? _hint;
    private OssuarySettings _settings = new();

    private OfferBadges? _badges;

    private bool _layoutMode;
    private HudPanel? _dragging;
    private Vector2 _dragGrip;

    /// <summary>
    /// Builds the HUD under <paramref name="parent"/>, unless one is already
    /// there. Safe to call more than once for the same run.
    /// </summary>
    internal static void Attach(Node parent, OssuarySettings settings)
    {
        try
        {
            if (parent.GetNodeOrNull(new NodePath(NodeName)) is not null) return;

            // A session plays many runs. Anything latched during the last one -
            // a cached combat, a failure that disabled a reader - would
            // otherwise carry into this one.
            State.CombatWatcher.Reset();
            State.DrawEstimator.Reset();
            State.IntentReader.Reset();
            State.TeamReader.Reset();

            var hud = new HudController { Name = NodeName, Layer = OverlayLayer, _settings = settings };
            parent.AddChild(hud);
            Log.Info($"HUD attached to {parent.GetType().Name}; awaiting _Ready");
        }
        catch (Exception ex)
        {
            Log.Error("HUD could not be attached; the game is unaffected", ex);
        }
    }

    public override void _Ready()
    {
        try
        {
            // Reaching this line at all is the proof that the generated interop
            // bridge is present: Godot cannot call it otherwise.
            Log.Info("HUD ready — Godot interop bridge is live");

            _root = new Control
            {
                Name = "Panels",
                // The whole HUD is transparent to input, always. Dragging is
                // handled in _Input by reading the mouse directly, so no panel
                // ever has to become clickable and there is no state in which
                // the HUD can swallow something meant for the game.
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(_root);

            Add(new StatusPanel(_settings));
            Add(new DeckPanel(_settings));
            Add(new ForecastPanel(_settings));
            if (_settings.TeamPanel) Add(new TeamPanel(_settings));

            _badges = new OfferBadges(_settings, _root);
            if (_settings.CanaryPanel) Add(new CanaryPanel());

            _hint = new Label
            {
                Name = "LayoutHint",
                Visible = false,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Position = new Vector2(24, 940),
                Text = $"OSSUARY LAYOUT MODE — drag panels · click ON/OFF to show or hide one · - and + resize text · {_settings.LayoutKey} to finish",
            };
            _hint.AddThemeColorOverride("font_color", new Color(0.42f, 0.78f, 0.70f));
            _root.AddChild(_hint);

            ApplyTextScale();

            Visible = _settings.HudVisible;
            Log.Info(
                $"HUD showing {_panels.Count} panel(s); {_settings.ToggleKey} toggles, "
                + $"{_settings.LayoutKey} moves");
        }
        catch (Exception ex)
        {
            Log.Error("HUD failed during setup and will not draw", ex);
        }
    }

    public override void _Process(double delta)
    {
        // Badges are drawn on this layer, so they are hidden with it — but the
        // call still runs while hidden, cheaply, so the label set is dropped
        // rather than left holding references to nodes the game has freed.
        //
        // Scanned from the viewport root rather than from NRun. Not everything
        // that offers you something lives under the run - Neow's opening relic
        // choice is presented before the map exists - and a scan rooted at the
        // run silently misses those. The candidate cap is what keeps a wider
        // walk safe.
        var scanRoot = GetTree()?.Root ?? GetParent();
        if (scanRoot is not null) _badges?.Tick(scanRoot, Visible);

        if (!Visible) return;

        // Panels isolate their own failures, so one bad panel cannot stop the
        // loop that updates the others.
        for (var i = 0; i < _panels.Count; i++) _panels[i].Tick(delta);

        // A panel resizes as its contents change - a longer draw pile, a new
        // enemy - so in layout mode the corners are recomputed every frame
        // rather than left where they were when it opened.
        if (_layoutMode) RepositionToggles();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        // _Unhandled* runs only after the game's own UI has declined the key, so
        // the hotkeys can never steal input from a menu or a text field.
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;

        if (key.Keycode == _settings.ToggleKeyCode)
        {
            SetHudVisible(!Visible);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (key.Keycode == _settings.LayoutKeyCode)
        {
            SetLayoutMode(!_layoutMode);
            GetViewport().SetInputAsHandled();
            return;
        }

        // Text size is adjustable only while arranging the HUD. Outside layout
        // mode these are ordinary keys the game may want.
        if (!_layoutMode) return;

        var step = key.Keycode switch
        {
            Key.Minus or Key.KpSubtract => -0.1,
            Key.Equal or Key.Plus or Key.KpAdd => 0.1,
            _ => 0.0,
        };

        if (step == 0.0) return;

        _settings.TextScale = Math.Round(_settings.ClampedTextScale + step, 2);
        _settings.Save();
        ApplyTextScale();
        GetViewport().SetInputAsHandled();
        Log.Info($"text scale {_settings.ClampedTextScale:0.0}x");
    }

    private void ApplyTextScale()
    {
        foreach (var panel in _panels) panel.ApplyTextScale(_settings.ClampedTextScale);

        // Resizing happens a frame or two later, so the corners are stale until
        // then; the next frame in layout mode puts them right.
        RepositionToggles();
    }

    /// <summary>Puts every on/off control back on its panel's corner.</summary>
    private void RepositionToggles()
    {
        foreach (var panel in _panels) panel.PositionToggle();
    }

    /// <summary>
    /// Drags panels while layout mode is on.
    /// </summary>
    /// <remarks>
    /// Reading the mouse here rather than through control hit-testing is what
    /// lets every panel stay <c>MouseFilter.Ignore</c> permanently. Events are
    /// marked handled only when a drag actually consumes them, so an ordinary
    /// click in layout mode still reaches the game.
    /// </remarks>
    public override void _Input(InputEvent @event)
    {
        if (!_layoutMode || !Visible) return;

        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } down:
            {
                // The on/off control wins over dragging. It sits inside the
                // panel, so without this every click on it would also start a
                // drag and the panel would jump as you toggled it.
                if (ToggleAt(down.Position) is { } toggled)
                {
                    var off = toggled.ToggleHidden();
                    SavePlacements();
                    Log.Info($"panel '{toggled.Name}' {(off ? "hidden" : "shown")}");
                    GetViewport().SetInputAsHandled();
                    return;
                }

                var panel = PanelAt(down.Position);
                if (panel?.Root is null) return;
                _dragging = panel;
                _dragGrip = down.Position - panel.Root.GlobalPosition;
                GetViewport().SetInputAsHandled();
                break;
            }

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
            {
                if (_dragging is null) return;
                _dragging = null;
                SavePlacements();
                GetViewport().SetInputAsHandled();
                break;
            }

            case InputEventMouseMotion motion:
            {
                if (_dragging?.Root is null) return;
                _dragging.Root.GlobalPosition = motion.Position - _dragGrip;
                _dragging.PositionToggle();
                GetViewport().SetInputAsHandled();
                break;
            }
        }
    }

    /// <summary>The panel whose on/off control is under this point, if any.</summary>
    private HudPanel? ToggleAt(Vector2 point)
    {
        // Topmost first, to match PanelAt: where two panels overlap, the one
        // you can see is the one you are clicking.
        for (var i = _panels.Count - 1; i >= 0; i--)
        {
            if (_panels[i].ToggleRect is { } rect && rect.HasPoint(point)) return _panels[i];
        }

        return null;
    }

    private HudPanel? PanelAt(Vector2 point)
    {
        // Last drawn wins, so the panel visually on top is the one grabbed.
        for (var i = _panels.Count - 1; i >= 0; i--)
        {
            var root = _panels[i].Root;
            if (root is not null && root.GetGlobalRect().HasPoint(point)) return _panels[i];
        }

        return null;
    }

    private void SetHudVisible(bool visible)
    {
        Visible = visible;
        _settings.HudVisible = visible;
        // Layout mode on an invisible HUD would silently eat clicks.
        if (!visible && _layoutMode) SetLayoutMode(false);
        _settings.Save();
        Log.Info($"HUD {(visible ? "shown" : "hidden")}");
    }

    private void SetLayoutMode(bool on)
    {
        // Entering layout mode on a hidden HUD would be a no-op the player
        // cannot see, so show it instead of doing nothing.
        if (on && !Visible) SetHudVisible(true);

        _layoutMode = on;
        foreach (var panel in _panels) panel.SetArranging(on);
        if (on) RepositionToggles();
        if (_hint is not null) _hint.Visible = on;
        if (_root is not null) _root.Modulate = on ? new Color(1f, 1f, 1f, 0.85f) : Colors.White;

        if (!on)
        {
            _dragging = null;
            SavePlacements();
        }

        Log.Info($"layout mode {(on ? "on — drag panels, click ON/OFF to hide one" : "off")}");
    }

    private void SavePlacements()
    {
        foreach (var panel in _panels)
        {
            if (panel.Root is null) continue;
            var at = panel.Root.Position;
            _settings.Panels[panel.Name] = new PanelPlacement { X = at.X, Y = at.Y, Hidden = panel.Hidden };
        }

        _settings.Save();
    }

    private void Add(HudPanel panel)
    {
        if (!panel.TryBuild() || panel.Root is null) return;

        // A saved position and on/off state win over the panel's defaults.
        if (_settings.Panels.TryGetValue(panel.Name, out var placement))
        {
            panel.Root.Position = new Vector2(placement.X, placement.Y);
            panel.SetHidden(placement.Hidden);
        }

        _root?.AddChild(panel.Root);
        _panels.Add(panel);

        // After the panel is in the tree, so the toggle can be placed from the
        // panel's real rect rather than from the size it was built at.
        if (_root is not null) panel.AttachToggle(_root);
    }
}
