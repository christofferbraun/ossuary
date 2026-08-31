using Godot;

namespace Ossuary.Hud;

/// <summary>
/// One box of information drawn over the game.
/// </summary>
/// <remarks>
/// A panel is a plain C# object that <em>owns</em> a tree of stock Godot
/// controls; it is deliberately not a <see cref="Node"/> subclass. Only
/// <see cref="HudController"/> subclasses a node, so the interop bridge that
/// Godot's source generators emit is depended on in exactly one place instead of
/// once per panel.
///
/// Every entry point is wrapped: a panel that throws disables itself, logs once,
/// and leaves the rest of the HUD running. Ossuary only reads and draws, so no
/// failure here is worth interrupting a run over.
/// </remarks>
internal abstract class HudPanel
{
    protected HudPanel(string name) => Name = name;

    internal string Name { get; }

    /// <summary>The control to parent into the HUD, or null if building failed.</summary>
    internal Control? Root { get; private set; }

    /// <summary>Set once this panel has thrown. A failed panel is never called again.</summary>
    internal bool Failed { get; private set; }

    private bool _arranging;
    private Label? _toggle;

    /// <summary>
    /// Whether the player has switched this panel off.
    /// </summary>
    /// <remarks>
    /// A hidden panel still exists and is still arranged: it is shown while
    /// layout mode is on, because otherwise the only control for turning it
    /// back on would be inside the thing that is off.
    /// </remarks>
    internal bool Hidden { get; private set; }

    /// <summary>Applies a saved on/off state, without writing settings back.</summary>
    internal void SetHidden(bool hidden)
    {
        Hidden = hidden;
        UpdateToggle();
        if (Root is not null && !Failed && !_arranging) Root.Visible = !hidden;
    }

    /// <summary>Flips this panel on or off. Returns the new state.</summary>
    internal bool ToggleHidden()
    {
        SetHidden(!Hidden);
        return Hidden;
    }

    /// <summary>
    /// The clickable region of this panel's on/off control, in screen
    /// coordinates, or null when there is nothing to click.
    /// </summary>
    internal Rect2? ToggleRect =>
        _arranging && _toggle is not null && !Failed ? _toggle.GetGlobalRect() : null;

    /// <summary>
    /// Tells the panel the HUD is being arranged.
    /// </summary>
    /// <remarks>
    /// A panel that hides itself outside combat cannot be dragged into place
    /// outside combat either, which is exactly when someone would want to. While
    /// layout mode is on, panels stay on screen regardless - and so does a panel
    /// the player has switched off, so it can be switched back on.
    /// </remarks>
    internal void SetArranging(bool arranging)
    {
        _arranging = arranging;
        if (_toggle is not null)
        {
            _toggle.Visible = arranging;
            if (arranging) PositionToggle();
        }

        if (Root is null || Failed) return;
        if (arranging) Root.Visible = true;
        else if (Hidden) Root.Visible = false;
    }

    /// <summary>
    /// Builds this panel's on/off control on the HUD layer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately <em>not</em> a child of the panel. Every panel root is a
    /// <see cref="PanelContainer"/>, and a container lays out all of its
    /// children — a second child is stretched to fill the panel rather than
    /// sitting in its corner, which would make the whole panel one large
    /// toggle and eat every drag.
    /// </para>
    /// <para>
    /// So it lives beside the panels and is positioned from the panel's own
    /// rect by <see cref="PositionToggle"/>, the same approach the offer badges
    /// take. It is not a <see cref="Button"/> either: the whole HUD is
    /// <see cref="Control.MouseFilterEnum.Ignore"/> and stays that way, and
    /// clicks are hit-tested in <c>HudController._Input</c> against
    /// <see cref="ToggleRect"/>, so no part of the HUD can ever swallow input
    /// meant for the game.
    /// </para>
    /// </remarks>
    internal void AttachToggle(Control layer)
    {
        if (Root is null || Failed) return;

        _toggle = new Label
        {
            Name = $"OssuaryToggle_{Name}",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            ZIndex = 20,
        };
        _toggle.SetMeta(BaseSizeMeta, 13);

        var plate = new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.12f, 0.12f, 0.95f),
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
        };
        plate.SetContentMarginAll(4);
        _toggle.AddThemeStyleboxOverride("normal", plate);

        layer.AddChild(_toggle);
        UpdateToggle();
        PositionToggle();
    }

    /// <summary>
    /// Puts the on/off control on the panel's top-right corner.
    /// </summary>
    /// <remarks>
    /// Called when layout mode opens and while a panel is being dragged, which
    /// are the only two moments a panel can move. Both the panel and the toggle
    /// are children of the same unlaid-out control, so their positions are
    /// directly comparable and no transform maths is needed.
    /// </remarks>
    internal void PositionToggle()
    {
        if (_toggle is null || Root is null) return;

        _toggle.ResetSize();
        _toggle.Position = Root.Position + new Vector2(Root.Size.X - _toggle.Size.X - 4f, 4f);
    }

    private void UpdateToggle()
    {
        if (_toggle is null) return;

        _toggle.Text = Hidden ? "OFF" : "ON";
        _toggle.AddThemeColorOverride(
            "font_color",
            Hidden ? new Color(0.85f, 0.36f, 0.31f) : new Color(0.55f, 0.80f, 0.55f));
    }

    /// <summary>
    /// Takes the panel off screen when it has nothing to say, unless the HUD is
    /// being arranged.
    /// </summary>
    protected bool HideUnlessArranging()
    {
        if (Root is not null) Root.Visible = _arranging;
        return _arranging;
    }

    internal bool TryBuild()
    {
        try
        {
            Root = BuildRoot();
            MakeInputTransparent(Root);
            return true;
        }
        catch (Exception ex)
        {
            Fail("could not be built", ex);
            return false;
        }
    }

    /// <summary>
    /// Makes an entire control subtree transparent to the mouse.
    /// </summary>
    /// <remarks>
    /// This has to walk the tree. <c>MouseFilter.Ignore</c> on a parent does not
    /// propagate — Godot hit-tests every control independently — and while
    /// <see cref="Label"/> happens to default to <c>Ignore</c>, containers such
    /// as <see cref="PanelContainer"/> and <see cref="VBoxContainer"/> default
    /// to <c>Stop</c>. Setting only the panel's root therefore leaves its own
    /// containers eating hovers and clicks meant for the game underneath.
    /// </remarks>
    private static void MakeInputTransparent(Node node)
    {
        if (node is Control control) control.MouseFilter = Control.MouseFilterEnum.Ignore;
        foreach (var child in node.GetChildren()) MakeInputTransparent(child);
    }

    internal void Tick(double delta)
    {
        if (Failed || Root is null) return;

        // A panel switched off does no work at all, rather than working and
        // drawing nothing - the point of switching it off is usually that you
        // do not want it costing anything either.
        if (Hidden && !_arranging)
        {
            Root.Visible = false;
            return;
        }

        try
        {
            ShrinkIfDue();
            OnTick(delta);
        }
        catch (Exception ex)
        {
            Fail("threw while updating", ex);
        }
    }

    /// <summary>Builds the panel's control tree. Called once, before it is shown.</summary>
    protected abstract Control BuildRoot();

    /// <summary>
    /// Creates a label that remembers the size it was designed at.
    /// </summary>
    /// <remarks>
    /// The intended size is stashed on the node itself so
    /// <see cref="ApplyTextScale"/> can rescale from the original every time
    /// rather than compounding — scaling an already-scaled size drifts, and the
    /// drift is only visible after several adjustments, which is the worst way
    /// to find a bug.
    /// </remarks>
    private protected static Label MakeLabel(string text, Color colour, int baseSize = 16)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeColorOverride("font_color", colour);
        label.SetMeta(BaseSizeMeta, baseSize);
        return label;
    }

    private const string BaseSizeMeta = "ossuary_base_font_size";

    /// <summary>Re-sizes every label in the panel, relative to its design size.</summary>
    internal void ApplyTextScale(double scale)
    {
        if (Failed || Root is null) return;

        try
        {
            Rescale(Root, scale);

            // The toggle is not under Root - see AttachToggle - so the walk
            // from Root does not reach it, and it would keep its built size
            // while everything else changed.
            if (_toggle is not null) Rescale(_toggle, scale);

            // The panel is only as big as its contents, so a smaller font must
            // shrink the box rather than leave it padded with empty space.
            ShrinkToFit();
        }
        catch (Exception ex)
        {
            Fail("could not be rescaled", ex);
        }
    }

    private int _shrinkIn;

    /// <summary>
    /// Collapses the panel onto its contents, once the layout has caught up.
    /// </summary>
    /// <remarks>
    /// Panels are parented into a plain <see cref="Control"/> rather than a
    /// container, so nothing lays them out and their size is whatever it was
    /// built as. Without this a four-row draw pile occupies the same box as a
    /// twenty-row one.
    ///
    /// The delay matters. A container's minimum size is recomputed during the
    /// layout pass, not when a child's font changes, so calling
    /// <c>ResetSize</c> in the same frame measures the <em>old</em> contents.
    /// Growing hid this - a box too large for its text still fits - but
    /// shrinking showed it plainly: the text got smaller on every keypress
    /// while the box only caught up on the next one.
    /// </remarks>
    private protected void ShrinkToFit() => _shrinkIn = 2;

    /// <summary>Performs a deferred resize once the layout pass has run.</summary>
    private void ShrinkIfDue()
    {
        if (_shrinkIn <= 0 || Root is null) return;
        if (--_shrinkIn == 0) Root.ResetSize();
    }

    private static void Rescale(Node node, double scale)
    {
        if (node is Label label && label.HasMeta(BaseSizeMeta))
        {
            var baseSize = (int)label.GetMeta(BaseSizeMeta);
            label.AddThemeFontSizeOverride("font_size", Math.Max(6, (int)Math.Round(baseSize * scale)));
        }

        foreach (var child in node.GetChildren()) Rescale(child, scale);
    }

    /// <summary>Refreshes the panel. Called every frame the HUD is visible.</summary>
    protected virtual void OnTick(double delta) { }

    private void Fail(string what, Exception ex)
    {
        Failed = true;
        if (Root is not null) Root.Visible = false;
        Log.Error($"panel '{Name}' {what} and is disabled for this session; other panels are unaffected", ex);
    }
}
