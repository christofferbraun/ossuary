using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Rewards;
using Ossuary.Grading;

namespace Ossuary.Hud;

/// <summary>
/// Puts a community rating on the cards, relics and potions you are offered.
/// </summary>
/// <remarks>
/// <para>
/// The badge is parented to the game's own node and anchored across its bottom
/// edge, so it moves with the thing it annotates, grows with it when you hover,
/// and disappears when the offer does. That is the whole reason for building
/// this as a mod rather than an overlay: an external tool has to guess where a
/// card is and re-guess every time the game's layout or its hover animation
/// changes.
/// </para>
/// <para>
/// <b>Which nodes get a badge.</b> Not by screen — enumerating every screen that
/// can offer something is a list that would silently go stale. By what the model
/// is doing instead:
/// </para>
/// <list type="bullet">
/// <item>a <see cref="CardModel"/> in no pile at all is being offered rather
/// than held — cards in hand, draw, discard, exhaust or the deck all report
/// their pile</item>
/// <item>and its type is one you can actually be offered. A Status or a Curse
/// is put into your deck <em>against your will</em>, so rating it is noise: it
/// was never a choice. This is also what stops a Slimed handed to you mid-combat
/// from being annotated as though you had picked it.</item>
/// <item>a relic outside <c>NRelicInventory</c> is not yet yours</item>
/// <item>a potion outside <c>NPotionContainer</c> is not yet in your belt</item>
/// </list>
/// <para>
/// <b>Safety.</b> This adds children to game-owned nodes, which is the most
/// invasive thing Ossuary does, so it is bounded on every side: it only ever
/// adds a <see cref="Label"/>, it never changes a property of a game node, it
/// gives up if it finds an implausible number of candidates, it can be turned
/// off in settings, and hiding the HUD removes every badge.
/// </para>
/// </remarks>
internal sealed class OfferBadges
{
    private const string BadgeName = "OssuaryRating";

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
    /// <remarks>
    /// Everything else — Status, Curse, Quest — arrives without being picked,
    /// so a rating on it answers a question nobody asked.
    /// </remarks>
    private static readonly CardType[] Offerable = [CardType.Attack, CardType.Skill, CardType.Power];

    private readonly OssuarySettings _settings;
    private readonly List<Node> _found = new();
    private readonly List<Node> _stale = new();

    private DateTime _lastScan = DateTime.MinValue;
    private bool _announced;
    private bool _failed;

    internal OfferBadges(OssuarySettings settings) => _settings = settings;

    /// <summary>
    /// Refreshes badges under <paramref name="root"/>, or strips them when the
    /// HUD is hidden or the feature is off.
    /// </summary>
    internal void Tick(Node root, bool hudVisible)
    {
        if (_failed) return;

        var now = DateTime.UtcNow;
        if (now - _lastScan < Interval) return;
        _lastScan = now;

        var wanted = hudVisible && _settings.OfferRatings;

        try
        {
            _found.Clear();
            _stale.Clear();
            Collect(root, _found, _stale);
            DropRedundantRows(_found);

            // Recycled nodes first, always - a badge on something that is no
            // longer an offer is wrong whether or not the feature is on.
            foreach (var node in _stale) Remove(node);

            if (!wanted || _found.Count > ImplausibleCandidates)
            {
                foreach (var node in _found) Remove(node);
                return;
            }

            foreach (var node in _found) Apply(node);
        }
        catch (Exception ex)
        {
            _failed = true;
            Log.Error("offer ratings failed and are disabled for this session", ex);
        }
    }

    /// <summary>
    /// Walks the scene collecting things on offer, and anything still wearing a
    /// badge that should not be.
    /// </summary>
    /// <remarks>
    /// The second list is not an afterthought. <c>NCard</c> is
    /// <c>IPoolable</c> — the game recycles card nodes — so a badge added to a
    /// card on a reward screen rides that node into whatever it is reused for
    /// next. Without sweeping them, badges leak onto screens that never offered
    /// anything.
    /// </remarks>
    private static void Collect(Node node, List<Node> offers, List<Node> stale)
    {
        // Our own HUD holds no offers and walking it wastes the budget.
        if (node.Name == "OssuaryHud") return;

        if (IsOffered(node)) offers.Add(node);
        else if (node.GetNodeOrNull(new NodePath(BadgeName)) is not null) stale.Add(node);

        foreach (var child in node.GetChildren()) Collect(child, offers, stale);
    }

    private static bool IsOffered(Node node) => node switch
    {
        // Pile is null for a card you have not been dealt yet. DeckVersion is
        // set on a card that is a *view* of one already in your deck, which is
        // what the upgrade and deck-inspection screens show - those are not
        // offers, and were being annotated as though they were.
        NCard card => card.Model is { Pile: null, DeckVersion: null } model
                      && Array.IndexOf(Offerable, model.Type) >= 0,
        NRelic relic => relic.Model is not null && !HasAncestor<NRelicInventory>(relic),
        NPotion potion => potion.Model is not null && !HasAncestor<NPotionContainer>(potion),

        // A reward row, for the cases where the reward draws no node of its own.
        // PotionReward overrides CreateIcon and produces an NPotion, which the
        // case above already catches; RelicReward does not, so a relic offered
        // as a reward - Neow's opening choice among them - had nothing to badge.
        NRewardButton button => button.Reward is RelicReward or PotionReward,
        _ => false,
    };

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

    private static bool HasAncestor<T>(Node node) where T : Node
    {
        for (var parent = node.GetParent(); parent is not null; parent = parent.GetParent())
        {
            if (parent is T) return true;
        }

        return false;
    }

    private void Apply(Node node)
    {
        var (kind, id) = Identify(node);
        if (id is null) return;

        var text = Describe(kind, id);
        var size = Math.Max(9, (int)Math.Round(15 * _settings.ClampedTextScale));

        if (node.GetNodeOrNull(new NodePath(BadgeName)) is Label existing)
        {
            // Every visual property is re-applied, not just the text. NCard is
            // pooled, so a node that showed a B-tier card and is recycled for an
            // F-tier one would otherwise keep the old colour while the numbers
            // updated - identical cards rendering in different colours, which is
            // exactly how this was noticed.
            if (existing.Text != text) existing.Text = text;
            existing.AddThemeColorOverride("font_color", Tint(kind, id));
            existing.AddThemeFontSizeOverride("font_size", size);
            Place(existing, node);
            return;
        }

        if (node is not Control host) return;

        var badge = new Label
        {
            Name = BadgeName,
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ZIndex = 50,
        };

        // Anchored across the bottom edge rather than positioned. The card grows
        // when hovered and the badge grows with it, centred, without any of this
        // code knowing the card's size.
        Place(badge, node);

        badge.AddThemeColorOverride("font_color", Tint(kind, id));
        badge.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.95f));
        badge.AddThemeConstantOverride("outline_size", 5);
        badge.AddThemeFontSizeOverride("font_size", size);

        // A backing plate, because the card art underneath is arbitrary and an
        // outline alone was not enough to read against all of it.
        var plate = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.05f, 0.05f, 0.72f),
            CornerRadiusTopLeft = 3,
            CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3,
            CornerRadiusBottomRight = 3,
        };
        plate.SetContentMarginAll(2);
        badge.AddThemeStyleboxOverride("normal", plate);

        host.AddChild(badge);

        if (!_announced)
        {
            _announced = true;
            Log.Info($"offer ratings: first badge attached to {node.GetType().Name}");
        }
    }

    /// <summary>
    /// Puts the badge where it belongs on this kind of host.
    /// </summary>
    /// <remarks>
    /// A card is not laid out the way its control rect suggests. <c>NCard</c>
    /// draws a fixed <c>defaultSize</c> of 300x422 and grows by changing
    /// <c>Scale</c> — <c>GetCurrentSize()</c> is literally
    /// <c>defaultSize * Scale</c> — so its own rect is not the visible card, and
    /// anchoring to the bottom of that rect landed the badge in the middle of
    /// the artwork. Positioning in the card's own coordinates against the game's
    /// constant puts it on the real bottom edge, and because a child inherits
    /// the parent's scale it still grows with the card on hover.
    /// </remarks>
    private static void Place(Label badge, Node host)
    {
        const float band = 30f;

        if (host is NRewardButton)
        {
            // A reward row is wide and already carries its own label, so the
            // badge sits at the right-hand end rather than across the bottom
            // where it would land on the row's own text.
            badge.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.OffsetLeft = -270;
            badge.OffsetRight = -12;
            badge.OffsetTop = -13;
            badge.OffsetBottom = 13;
            return;
        }

        if (host is NCard)
        {
            var card = NCard.defaultSize;
            badge.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            badge.Position = new Vector2(6, card.Y - band - 6);
            badge.Size = new Vector2(card.X - 12, band);
            return;
        }

        // Relics and potions are plain icons laid out by their container, so
        // their control rect is the thing you see.
        badge.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        badge.OffsetLeft = 2;
        badge.OffsetRight = -2;
        badge.OffsetTop = -24;
        badge.OffsetBottom = -1;
    }

    private static void Remove(Node node)
    {
        if (node.GetNodeOrNull(new NodePath(BadgeName)) is not Node badge) return;
        badge.QueueFree();
    }

    private static (RatingKind Kind, string? Id) Identify(Node node) => node switch
    {
        NCard card => (RatingKind.Card, card.Model?.Id.ToString()),
        NRelic relic => (RatingKind.Relic, relic.Model?.Id.ToString()),
        NPotion potion => (RatingKind.Potion, potion.Model?.Id.ToString()),
        NRewardButton { Reward: RelicReward relic } => (RatingKind.Relic, relic.Relic?.Id.ToString()),
        NRewardButton { Reward: PotionReward potion } => (RatingKind.Potion, potion.Potion?.Id.ToString()),
        _ => (RatingKind.Card, null),
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
}
