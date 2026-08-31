using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
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
/// the thing it annotates. An earlier version parented a label to the game's
/// own node and anchored it, which failed several ways at once: <c>NCard</c>'s
/// control rect is not the visible card, the hover scale lives on the holder
/// rather than the card, and <c>NCard</c> is <c>IPoolable</c>, so a badge added
/// to a recycled node rode it onto unrelated screens. Drawing into our own
/// layer makes all of that impossible: we never modify the scene, so there is
/// nothing to leave behind.
/// </para>
/// <para>
/// <b>Where the rectangle comes from.</b> Not from a constant. The game wraps
/// anything you can pick in a slot that owns a <c>%Hitbox</c> — an
/// <see cref="NCardHolder"/> on the reward and choice screens, an
/// <see cref="NMerchantSlot"/> in the shop — and that hitbox is the region the
/// player clicks, which makes it the game's own statement of where the thing
/// is. Reading its transform picks up the holder's hover scale and the shop's
/// smaller cards for free.
/// </para>
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

    /// <summary>Consecutive failed ticks before giving up for the session.</summary>
    private const int MaxStrikes = 10;

    /// <summary>Clear space between a badge and the icon it annotates.</summary>
    private const float Gap = 8f;

    /// <summary>Scanning four times a second is imperceptible and cheap.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The only card types you are ever asked to choose.
    /// </summary>
    /// <remarks>
    /// Everything else — Status, Curse, Quest — arrives without being picked,
    /// so a rating on it answers a question nobody asked.
    /// </remarks>
    private static readonly CardType[] Offerable = [CardType.Attack, CardType.Skill, CardType.Power];

    private readonly OssuarySettings _settings;
    private readonly Control _layer;
    private readonly List<Target> _targets = new();
    private readonly List<Node> _found = new();

    private DateTime _lastScan = DateTime.MinValue;
    private int _strikes;
    private bool _announced;
    private bool _measured;
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
    /// animations smoothly rather than stepping a scan at a time.
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
            // An earlier design disabled itself on the first exception, which
            // meant one transient failure silently took offer ratings out for
            // good.
            Log.Error($"offer ratings failed ({++_strikes}/{MaxStrikes})", ex);

            if (_strikes < MaxStrikes) return;

            _failed = true;
            Clear();
            Log.Error("offer ratings are disabled for this session", ex);
        }
    }

    private void Rescan(Node root)
    {
        // The screen the player is actually looking at. Opening the card reward
        // over a loot screen leaves the loot screen in the tree and visible, so
        // its potion went on being annotated underneath the cards.
        var top = NOverlayStack.Instance?.Peek() as Node;

        _found.Clear();
        Collect(root, top, _found);
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
    /// <remarks>
    /// The point size is fixed and the whole label is scaled, rather than the
    /// point size being recomputed from the measured height. Font sizes are
    /// integers, so recomputing one made the label's width jump a whole step at
    /// a time and re-run its layout, which read as jitter while a card grew
    /// under the cursor. Scaling is continuous.
    /// </remarks>
    private void Reposition()
    {
        var viewport = _layer.GetViewportRect();
        var scaleSetting = _settings.ClampedTextScale;

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

            // A card is drawn at a point size chosen for a full-size card and
            // then scaled to whatever the card currently is. Everything else is
            // an icon far smaller than a card carrying the same sentence, so it
            // keeps a fixed size.
            var onCard = target.Anchor == Anchor.CardBottom;
            var font = Math.Max(9, (int)Math.Round((onCard ? 20 : 15) * scaleSetting));
            var scale = onCard ? rect.Size.Y / NCard.defaultSize.Y : 1f;

            target.Refresh(font);

            // Cheap when nothing is dirty - the minimum size is cached - and
            // calling it unconditionally avoids keeping a size that was
            // measured on the frame the label was created, before its font
            // override had resolved.
            label.ResetSize();
            label.Scale = new Vector2(scale, scale);

            var size = label.Size * scale;
            var pos = target.Anchor switch
            {
                // Just inside the card's bottom border, centred.
                Anchor.CardBottom => new Vector2(
                    rect.Position.X + (rect.Size.X - size.X) * 0.5f,
                    rect.End.Y - size.Y - rect.Size.Y * 0.035f),

                // A reward row is wide and already carries its own text, so the
                // badge goes at its right-hand end.
                Anchor.RowRight => new Vector2(
                    rect.End.X - size.X - 12f,
                    rect.Position.Y + (rect.Size.Y - size.Y) * 0.5f),

                // An icon inside a reward row has clear space to its left, and
                // sitting there covers none of the icon.
                Anchor.IconLeft => new Vector2(
                    rect.Position.X - size.X - Gap,
                    rect.Position.Y + (rect.Size.Y - size.Y) * 0.5f),

                // Icons laid out in a grid - the shop - have neighbours to
                // either side but clear space above, so the badge goes there
                // rather than over the artwork.
                _ => new Vector2(
                    rect.Position.X + (rect.Size.X - size.X) * 0.5f,
                    rect.Position.Y - size.Y - Gap),
            };

            // A badge drawn off the edge of the screen is a badge nobody asked
            // for; the subject is mid-animation or parked offstage.
            label.Visible = viewport.Intersects(new Rect2(pos, size));
            label.Position = pos;
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
            // Preferred: the enclosing slot's hitbox, which is what the player
            // clicks. Reward and choice screens hold cards in an NCardHolder;
            // the shop holds them in an NMerchantSlot, which is why shop cards
            // measured wrong while reward cards measured right. Then the card's
            // own body. The class constant is the last resort, and is the only
            // one of the three that can go stale in a patch.
            if (Hitbox(card) is { } hitbox && IsSized(hitbox.Size))
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

        var toLayer = _layer.GetGlobalTransformWithCanvas().AffineInverse() * item.GetGlobalTransformWithCanvas();

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

    /// <summary>
    /// The hitbox of whichever slot holds this node, if it is in one.
    /// </summary>
    private static Control? Hitbox(Node node)
    {
        for (var parent = node.GetParent(); parent is not null; parent = parent.GetParent())
        {
            switch (parent)
            {
                case NCardHolder holder: return holder.Hitbox;
                case NMerchantSlot slot: return slot.Hitbox;
            }
        }

        return null;
    }

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

    private static void Collect(Node node, Node? top, List<Node> offers)
    {
        // Our own HUD holds no offers and walking it wastes the budget.
        if (node.Name == "OssuaryHud") return;

        // One unhappy node must not end the walk. A game node can be in any
        // state at any moment - half-initialised, being pooled, mid-teardown -
        // and a scan that aborts on the first of those stops annotating
        // everything else on screen.
        try
        {
            if (IsOffered(node) && !IsCovered(node, top)) offers.Add(node);
        }
        catch (Exception)
        {
            // Not this node, then. Deliberately not logged: this runs four
            // times a second over the whole tree, so a node the game keeps in
            // an odd state would fill the log rather than tell us anything.
        }

        foreach (var child in node.GetChildren()) Collect(child, top, offers);
    }

    /// <summary>
    /// True when this node belongs to a screen that another screen has been
    /// opened on top of.
    /// </summary>
    /// <remarks>
    /// Overlay screens stack: taking the card reward from a loot screen pushes
    /// the card selection on top while leaving the loot screen in the tree and
    /// still reporting itself visible. Anything under an overlay that is not
    /// the topmost one is behind something and must not be annotated. A node
    /// under no overlay at all — the shop, an event laid out in the room — is
    /// left alone, so this cannot suppress a screen that never joins the stack.
    /// </remarks>
    private static bool IsCovered(Node node, Node? top)
    {
        if (top is null) return false;

        for (var parent = node.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is not IOverlayScreen) continue;
            return !ReferenceEquals(parent, top);
        }

        return false;
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
        // as a reward had nothing to badge.
        NRewardButton button => button.Reward is RelicReward or PotionReward,

        // Neow's blessings. Every one of them is a relic - the model builds
        // them with RelicOption<T> - but they are drawn as event rows rather
        // than as relic nodes, so nothing above sees them.
        NEventOptionButton option => option.Option?.Relic is not null,

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

    private static T? Ancestor<T>(Node node) where T : class
    {
        for (var parent = node.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is T match) return match;
        }

        return null;
    }

    private static bool HasAncestor<T>(Node node) where T : class => Ancestor<T>(node) is not null;

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
                NEventOptionButton option => (RatingKind.Relic, option.Option?.Relic?.Id.ToString()),
                _ => (RatingKind.Card, null),
            };
        }
        catch (InvalidOperationException)
        {
            return (RatingKind.Card, null);
        }
    }

    /// <summary>Where a badge sits relative to the thing it annotates.</summary>
    private enum Anchor
    {
        CardBottom,
        RowRight,
        IconLeft,
        IconAbove,
    }

    private static Anchor AnchorFor(Node node) => node switch
    {
        NCard => Anchor.CardBottom,
        NRewardButton or NEventOptionButton => Anchor.RowRight,

        // A relic or potion drawn inside a reward row has clear space beside
        // it; one in the shop's grid has neighbours either side but nothing
        // above.
        _ => HasAncestor<NRewardButton>(node) ? Anchor.IconLeft : Anchor.IconAbove,
    };

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
    private sealed class Target
    {
        private readonly string _text;
        private readonly Color _tint;
        private int _font;

        internal Target(Node node, Label label, RatingKind kind, string id)
        {
            Node = node;
            Label = label;
            Anchor = AnchorFor(node);
            _text = Describe(kind, id);
            _tint = Tint(kind, id);
        }

        internal Node Node { get; }
        internal Label Label { get; }
        internal Anchor Anchor { get; }

        /// <summary>
        /// Applies the label's appearance, but only when something about it has
        /// actually changed.
        /// </summary>
        /// <remarks>
        /// Setting a theme override marks the label's layout dirty; doing that
        /// every frame is how a badge ends up recomputing its own size sixty
        /// times a second for no reason.
        /// </remarks>
        internal void Refresh(int font)
        {
            if (_font == font && Label.Text == _text) return;

            _font = font;
            Label.Text = _text;
            Label.AddThemeColorOverride("font_color", _tint);
            Label.AddThemeFontSizeOverride("font_size", font);
        }
    }
}
