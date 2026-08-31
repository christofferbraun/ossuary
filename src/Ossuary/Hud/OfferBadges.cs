using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rewards;
using Ossuary.Grading;

namespace Ossuary.Hud;

/// <summary>
/// Puts a community rating on the cards, relics and potions you are offered.
/// </summary>
/// <remarks>
/// <para>
/// <b>Badges are drawn, not attached.</b> Every label lives on Ossuary's own
/// overlay and is positioned each frame from the measured screen rectangle of
/// the thing it annotates. Earlier versions parented a label to the game's own
/// node and anchored it, which failed three separate ways: <c>NCard</c>'s
/// control rect is not the visible card, the hover scale lives on the holder
/// rather than the card, and <c>NCard</c> is <c>IPoolable</c>, so a badge
/// added to a recycled node rode it onto unrelated screens carrying the last
/// card's colour. Drawing into our own layer makes all three impossible: we
/// never modify the scene, so there is nothing to leave behind.
/// </para>
/// <para>
/// <b>Where the rectangle comes from.</b> Not from a constant. Every screen
/// that offers a card wraps it in an <see cref="NGridCardHolder"/>, and the
/// holder's <c>%Hitbox</c> is the game's own statement of where the card is —
/// it is the region the player clicks. Reading its transform picks up the
/// holder's hover scale, the shop's smaller cards and any animation in flight
/// for free, which is also why the badge's own text size is derived from the
/// measured height rather than fixed.
/// </para>
/// <para>
/// <b>Which nodes get a badge.</b> Two tests, because either alone has been
/// wrong in play:
/// </para>
/// <list type="bullet">
/// <item>the model must look like an offer — a <see cref="CardModel"/> in no
/// pile and not a view of a card already in your deck, a relic outside
/// <c>NRelicInventory</c>, a potion outside <c>NPotionContainer</c> — and be a
/// type you can actually choose. A Status or a Curse enters your deck
/// <em>against your will</em>, so rating it answers a question nobody asked.</item>
/// <item>and it must not be on a screen that shows you your own collection.
/// The six "pick one of the cards you already own" screens — upgrade, remove,
/// transform, enchant, pile and simple select — all derive from
/// <see cref="NCardGridSelectionScreen"/>, and none of the screens that offer
/// you something new does. Cards there report no pile, so the model test alone
/// let the upgrade screen grade your own deck.</item>
/// </list>
/// </remarks>
internal sealed class OfferBadges
{
    /// <summary>
    /// Above this many candidates, do nothing.
    /// </summary>
    /// <remarks>
    /// An offer screen shows a handful. Dozens means either the rule for "is
    /// this being offered" has stopped being true, or we are looking at a
    /// compendium screen showing the whole card pool — and covering either in
    /// labels is worse than showing none.
    /// </remarks>
    private const int ImplausibleCandidates = 20;

    /// <summary>Scanning four times a second is imperceptible and cheap.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The only card types you are ever asked to choose.
    /// </summary>
    private static readonly CardType[] Offerable = [CardType.Attack, CardType.Skill, CardType.Power];

    private readonly OssuarySettings _settings;
    private readonly Control _layer;
    private readonly List<Target> _targets = new();
    private readonly List<Node> _found = new();

    /// <summary>Consecutive failed ticks before giving up for the session.</summary>
    private const int MaxStrikes = 10;

    private DateTime _lastScan = DateTime.MinValue;
    private int _strikes;
    private bool _announced;
    private bool _failed;

    internal OfferBadges(OssuarySettings settings, Control host)
    {
        _settings = settings;

        _layer = new Control
        {
            Name = "OfferRatings",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _layer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        host.AddChild(_layer);
    }

    /// <summary>
    /// Called every frame. Rescans the tree occasionally; repositions what it
    /// already knows about every time, so badges track hover and tween
    /// animations exactly rather than lagging a scan behind.
    /// </summary>
    internal void Tick(Node root, bool hudVisible)
    {
        if (_failed) return;

        try
        {
            if (!hudVisible || !_settings.OfferRatings)
            {
                if (_targets.Count > 0) Clear();
                return;
            }

            var now = DateTime.UtcNow;
            if (now - _lastScan >= Interval)
            {
                _lastScan = now;
                Rescan(root);
            }

            Reposition();
            _strikes = 0;
        }
        catch (Exception ex)
        {
            // A single bad frame is not a reason to give up for the session.
            // The previous design disabled itself on the first exception, which
            // meant one transient failure early in a run silently took offer
            // ratings out for good - and, because badges were parented to game
            // nodes back then, left the ones already attached frozen on screen
            // rather than removing them.
            Log.Error($"offer ratings failed ({++_strikes}/{MaxStrikes})", ex);

            if (_strikes < MaxStrikes) return;

            _failed = true;
            Clear();
            Log.Error("offer ratings are disabled for this session", ex);
        }
    }

    private void Rescan(Node root)
    {
        _found.Clear();
        Collect(root, _found);
        DropRedundantRows(_found);

        if (_found.Count > ImplausibleCandidates) _found.Clear();

        // Retire labels whose subject is gone, and reuse the rest. Labels are
        // ours, so this is bookkeeping rather than surgery on the scene.
        for (var i = _targets.Count - 1; i >= 0; i--)
        {
            if (_found.Contains(_targets[i].Node)) continue;
            _targets[i].Label.QueueFree();
            _targets.RemoveAt(i);
        }

        foreach (var node in _found)
        {
            if (_targets.Any(t => t.Node == node)) continue;

            var (kind, id) = Identify(node);
            if (id is null) continue;

            var label = NewLabel();
            _layer.AddChild(label);
            _targets.Add(new Target(node, label, kind, id));

            if (_announced) continue;
            _announced = true;
            Log.Info($"offer ratings: first badge drawn for {node.GetType().Name}");
        }
    }

    /// <summary>
    /// Moves every badge onto its subject's current screen rectangle.
    /// </summary>
    private void Reposition()
    {
        var viewport = _layer.GetViewportRect();

        foreach (var target in _targets)
        {
            var label = target.Label;

            if (!GodotObject.IsInstanceValid(target.Node)
                || target.Node is not CanvasItem item
                || !item.IsInsideTree()
                || !item.IsVisibleInTree()
                || !TryMeasure(target.Node, out var rect))
            {
                label.Visible = false;
                continue;
            }

            // On a card the text scales with the card. A shop card, a reward
            // card and a hovered card are three different sizes on screen, and
            // a fixed point size reads correctly on at most one of them.
            // A relic or potion icon is far smaller than a card and carries the
            // same sentence, so it keeps a fixed size instead.
            var font = target.Node is NCard
                ? Math.Clamp((int)Math.Round(rect.Size.Y * 0.047f * _settings.ClampedTextScale), 9, 48)
                : Math.Max(9, (int)Math.Round(15 * _settings.ClampedTextScale));

            if (label.Text != target.Text) label.Text = target.Text;
            label.AddThemeFontSizeOverride("font_size", font);
            label.AddThemeColorOverride("font_color", target.Tint);
            label.ResetSize();

            var size = label.Size;

            // A reward row is wide and already carries its own text, so the
            // badge goes at its right-hand end rather than across the middle.
            var pos = target.Node is NRewardButton
                ? new Vector2(
                    rect.End.X - size.X - 12f,
                    rect.Position.Y + (rect.Size.Y - size.Y) * 0.5f)
                : new Vector2(
                    rect.Position.X + (rect.Size.X - size.X) * 0.5f,
                    rect.End.Y - size.Y - rect.Size.Y * 0.035f);

            // A badge drawn off the edge of the screen is a badge nobody asked
            // for; the subject is mid-animation or parked offstage.
            label.Visible = viewport.Intersects(new Rect2(pos, size));
            label.Position = pos.Round();
        }
    }

    /// <summary>
    /// The screen-space rectangle of the visible thing, in the overlay's
    /// coordinates.
    /// </summary>
    /// <remarks>
    /// Measured through <c>GetGlobalTransformWithCanvas</c> rather than
    /// <c>GetGlobalRect</c>, because the latter ignores accumulated scale — and
    /// scale is exactly what the game animates when you hover a card.
    /// </remarks>
    private bool TryMeasure(Node node, out Rect2 rect)
    {
        rect = default;

        Control? item;
        Vector2 size;

        if (node is NCard card)
        {
            // Preferred: the holder's hitbox, which is what the player clicks.
            // Then the card's own body. The class constant is the last resort,
            // and is the only one of the three that can go stale in a patch.
            if (Ancestor<NCardHolder>(card)?.Hitbox is { } hitbox && IsSized(hitbox.Size))
            {
                item = hitbox;
                size = hitbox.Size;
            }
            else if (card.Body is { } body && IsSized(body.Size))
            {
                item = body;
                size = body.Size;
            }
            else
            {
                item = card;
                size = NCard.defaultSize;
            }
        }
        else if (node is Control control && IsSized(control.Size))
        {
            item = control;
            size = control.Size;
        }
        else
        {
            return false;
        }

        var toScreen = item.GetGlobalTransformWithCanvas();
        var toLayer = _layer.GetGlobalTransformWithCanvas().AffineInverse() * toScreen;

        var a = toLayer * Vector2.Zero;
        var b = toLayer * new Vector2(size.X, 0);
        var c = toLayer * size;
        var d = toLayer * new Vector2(0, size.Y);

        var min = new Vector2(
            Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)),
            Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)));
        var max = new Vector2(
            Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)),
            Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)));

        rect = new Rect2(min, max - min);
        if (!IsSized(rect.Size)) return false;

        if (!_measured)
        {
            _measured = true;
            Log.Info(
                $"offer ratings: measured {node.GetType().Name} via {item.GetType().Name} "
                + $"local {size} -> screen {rect}");
        }

        return true;
    }

    private bool _measured;

    private static bool IsSized(Vector2 size) => size.X > 1f && size.Y > 1f;

    private Label NewLabel()
    {
        var label = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.95f));
        label.AddThemeConstantOverride("outline_size", 5);

        // A backing plate, because the card art underneath is arbitrary and an
        // outline alone was not enough to read against all of it.
        var plate = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.05f, 0.05f, 0.78f),
            CornerRadiusTopLeft = 4,
            CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4,
            CornerRadiusBottomRight = 4,
        };
        plate.SetContentMarginAll(4);
        label.AddThemeStyleboxOverride("normal", plate);

        return label;
    }

    private void Clear()
    {
        foreach (var target in _targets) target.Label.QueueFree();
        _targets.Clear();
    }

    private static void Collect(Node node, List<Node> offers)
    {
        // Our own HUD holds no offers and walking it wastes the budget.
        if (node.Name == "OssuaryHud") return;

        // One unhappy node must not end the walk. A game node can be in any
        // state at any moment - half-initialised, being pooled, mid-teardown -
        // and a scan that aborts on the first of those stops annotating
        // everything else on screen.
        try
        {
            if (IsOffered(node)) offers.Add(node);
        }
        catch (Exception)
        {
            // Not this node, then. Deliberately not logged: this runs four
            // times a second over the whole tree, so a node the game keeps in
            // an odd state would fill the log rather than tell us anything.
        }

        foreach (var child in node.GetChildren()) Collect(child, offers);
    }

    private static bool IsOffered(Node node) => node switch
    {
        NCard card => card.Model is { Pile: null, DeckVersion: null } model
                      && Array.IndexOf(Offerable, model.Type) >= 0
                      && !IsYourOwnCollection(card),

        // Ancestry is tested before the model, and the model read is guarded.
        // NRelic.Model and NPotion.Model are not nullable properties: their
        // getters *throw* when nothing has been assigned yet. The empty slots
        // in your potion belt are exactly that, so `Model is not null` never
        // returned false here - it raised, took the whole scan down with it,
        // and disabled offer ratings for the rest of the session.
        NRelic relic => !HasAncestor<NRelicInventory>(relic) && HasModel(relic),
        NPotion potion => !HasAncestor<NPotionContainer>(potion) && HasModel(potion),

        // A reward row, for the cases where the reward draws no node of its own.
        // PotionReward overrides CreateIcon and produces an NPotion, which the
        // case above already catches; RelicReward does not, so a relic offered
        // as a reward — Neow's opening choice among them — had nothing to badge.
        NRewardButton button => button.Reward is RelicReward or PotionReward,
        _ => false,
    };

    /// <summary>
    /// Whether this node has been given a model, without asking a getter that
    /// answers "no" by throwing.
    /// </summary>
    /// <remarks>
    /// Reading the private backing field by reflection would avoid the throw,
    /// but silently binds us to a field name; catching is version-proof and the
    /// cost is a handful of exceptions per second in the one case that raises.
    /// </remarks>
    private static bool HasModel(Node node)
    {
        try
        {
            return node switch
            {
                NRelic relic => relic.Model is not null,
                NPotion potion => potion.Model is not null,
                _ => false,
            };
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when this card is being shown to you as something you already have,
    /// rather than something you are being offered.
    /// </summary>
    /// <remarks>
    /// These cards report no pile, so the model alone cannot tell them apart
    /// from an offer — which is how "Choose a card to Upgrade" ended up grading
    /// the deck you had already built.
    /// </remarks>
    private static bool IsYourOwnCollection(Node node)
    {
        for (var parent = node.GetParent(); parent is not null; parent = parent.GetParent())
        {
            switch (parent)
            {
                // Upgrade, remove, transform, enchant, combat-pile and simple
                // select. Every screen that shows your own cards in a grid
                // derives from this; no screen that offers you a new one does.
                case NCardGridSelectionScreen:
                case NInspectCardScreen:
                case NCardLibraryGrid:
                case NUpgradePreview:
                case NGridCardPreviewContainer:
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Drops reward rows whose contents were collected in their own right, so a
    /// potion reward is annotated once rather than twice.
    /// </summary>
    private static void DropRedundantRows(List<Node> found)
    {
        found.RemoveAll(node =>
            node is NRewardButton && found.Any(other => other != node && IsDescendantOf(other, node)));
    }

    private static bool IsDescendantOf(Node node, Node ancestor)
    {
        for (var parent = node.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent == ancestor) return true;
        }

        return false;
    }

    private static T? Ancestor<T>(Node node) where T : Node
    {
        for (var parent = node.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is T match) return match;
        }

        return null;
    }

    private static bool HasAncestor<T>(Node node) where T : Node => Ancestor<T>(node) is not null;

    /// <summary>
    /// What this node is and which rating to look up, or a null id if it cannot
    /// say.
    /// </summary>
    /// <remarks>
    /// The relic and potion arms are inside the guard for the same reason as
    /// <see cref="HasModel"/>: their <c>Model</c> getters throw rather than
    /// return null, and <c>?.</c> does not help with a getter that raises.
    /// </remarks>
    private static (RatingKind Kind, string? Id) Identify(Node node)
    {
        try
        {
            return node switch
            {
                NCard card => (RatingKind.Card, card.Model?.Id.ToString()),
                NRelic relic => (RatingKind.Relic, relic.Model?.Id.ToString()),
                NPotion potion => (RatingKind.Potion, potion.Model?.Id.ToString()),
                NRewardButton { Reward: RelicReward relic } => (RatingKind.Relic, relic.Relic?.Id.ToString()),
                NRewardButton { Reward: PotionReward potion } => (RatingKind.Potion, potion.Potion?.Id.ToString()),
                _ => (RatingKind.Card, null),
            };
        }
        catch (InvalidOperationException)
        {
            return (RatingKind.Card, null);
        }
    }

    /// <summary>
    /// The badge text: grade, then the raw score, then the numbers behind it.
    /// </summary>
    /// <remarks>
    /// The score is shown alongside the letter because the letter is a banded
    /// summary and loses resolution — two cards can both be B with one at the
    /// top of the band and the other scraping the bottom, and that is exactly
    /// what separates them at the moment of choosing.
    ///
    /// A low-confidence rating is marked rather than hidden. Under 300 picks the
    /// number is noise, and saying so is more useful than either suppressing it
    /// or presenting it as though it were solid.
    /// </remarks>
    private static string Describe(RatingKind kind, string id)
    {
        var table = Ratings.Table;
        if (table is null || !table.TryGet(kind, id, out var entry)) return "no data";

        var parts = new List<string>(4) { $"{entry.Tier} {entry.Score}", $"{entry.WinRate:0.#}% win" };
        if (entry.PickRate is { } pick) parts.Add($"{pick:0.#}% pick");
        if (entry.Confidence == Confidence.Low) parts.Add("low sample");

        return string.Join("  ·  ", parts);
    }

    private static Color Tint(RatingKind kind, string id)
    {
        var table = Ratings.Table;
        if (table is null || !table.TryGet(kind, id, out var entry)) return new Color(0.60f, 0.63f, 0.61f);

        return entry.Tier switch
        {
            Tier.S => new Color(0.48f, 0.90f, 0.66f),
            Tier.A => new Color(0.66f, 0.90f, 0.55f),
            Tier.B => new Color(0.85f, 0.89f, 0.55f),
            Tier.C => new Color(0.92f, 0.80f, 0.48f),
            Tier.D => new Color(0.93f, 0.60f, 0.40f),
            _ => new Color(0.92f, 0.44f, 0.39f),
        };
    }

    /// <summary>One badge and the node it belongs to.</summary>
    private readonly struct Target
    {
        internal Target(Node node, Label label, RatingKind kind, string id)
        {
            Node = node;
            Label = label;
            Text = Describe(kind, id);
            Tint = OfferBadges.Tint(kind, id);
        }

        internal Node Node { get; }
        internal Label Label { get; }
        internal string Text { get; }
        internal Color Tint { get; }
    }
}
