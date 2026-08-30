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

        try
        {
            OnTick(delta);
        }
        catch (Exception ex)
        {
            Fail("threw while updating", ex);
        }
    }

    /// <summary>Builds the panel's control tree. Called once, before it is shown.</summary>
    protected abstract Control BuildRoot();

    /// <summary>Refreshes the panel. Called every frame the HUD is visible.</summary>
    protected virtual void OnTick(double delta) { }

    private void Fail(string what, Exception ex)
    {
        Failed = true;
        if (Root is not null) Root.Visible = false;
        Log.Error($"panel '{Name}' {what} and is disabled for this session; other panels are unaffected", ex);
    }
}
