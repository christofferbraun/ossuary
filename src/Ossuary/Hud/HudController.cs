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
/// <see cref="_Process"/> and <see cref="_UnhandledKeyInput"/>. If that bridge
/// is ever missing, those methods are silently never called — so attachment and
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
    private OssuarySettings _settings = new();

    /// <summary>
    /// Builds the HUD under <paramref name="parent"/>, unless one is already
    /// there. Safe to call more than once for the same run.
    /// </summary>
    internal static void Attach(Node parent, OssuarySettings settings)
    {
        try
        {
            if (parent.GetNodeOrNull(new NodePath(NodeName)) is not null) return;

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
                // The whole HUD is transparent to input. Ossuary reads and
                // draws; it must never swallow a click meant for the game.
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(_root);

            Add(new StatusPanel());
            if (_settings.CanaryPanel) Add(new CanaryPanel());

            Visible = _settings.HudVisible;
            Log.Info($"HUD showing {_panels.Count} panel(s); toggle with {_settings.ToggleKey}");
        }
        catch (Exception ex)
        {
            Log.Error("HUD failed during setup and will not draw", ex);
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;

        // Panels isolate their own failures, so one bad panel cannot stop the
        // loop that updates the others.
        for (var i = 0; i < _panels.Count; i++) _panels[i].Tick(delta);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        // _Unhandled* runs only after the game's own UI has declined the key, so
        // the hotkey can never steal input from a menu or a text field.
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (key.Keycode != _settings.ToggleKeyCode) return;

        Visible = !Visible;
        _settings.HudVisible = Visible;
        _settings.Save();
        GetViewport().SetInputAsHandled();
        Log.Info($"HUD {(Visible ? "shown" : "hidden")}");
    }

    private void Add(HudPanel panel)
    {
        if (!panel.TryBuild() || panel.Root is null) return;
        _root?.AddChild(panel.Root);
        _panels.Add(panel);
    }
}
