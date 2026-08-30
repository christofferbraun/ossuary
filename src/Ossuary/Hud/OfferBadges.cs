using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using Ossuary.Grading;

namespace Ossuary.Hud;

/// <summary>
/// Puts a community rating on the cards, relics and potions you are offered.
/// </summary>
/// <remarks>
/// <para>
/// The badge is parented to the game's own node, so it moves with the thing it
/// annotates, disappears when the offer does, and needs no coordinate maths of
/// its own. That is the whole reason for building this as a mod rather than an
/// overlay: an external tool has to guess where a card is on screen and re-guess
/// every time the game's layout changes.
/// </para>
/// <para>
/// <b>Which nodes get a badge.</b> Not by screen — enumerating every screen that
/// can offer something is a list that would silently go stale. Instead, by what
/// the model is doing:
/// </para>
/// <list type="bullet">
/// <item>a <see cref="CardModel"/> in no pile at all is being offered, not held
/// — cards in hand, draw, discard, exhaust or the deck all report their pile</item>
/// <item>a relic outside <c>NRelicInventory</c> is not yet yours</item>
/// <item>a potion outside <c>NPotionContainer</c> is not yet in your belt</item>
/// </list>
/// <para>
/// That covers reward, choose-a-card, shop and ancient screens without naming
/// any of them, and excludes everything you already own.
/// </para>
/// <para>
/// <b>Safety.</b> This adds children to game-owned nodes, which is the most
/// invasive thing Ossuary does, so it is bounded on every side: it only ever
/// adds a <see cref="Label"/>, it never changes a property of a game node, it
/// gives up if it finds an implausible number of candidates (the signature of
/// the pile rule being wrong after a patch), it can be turned off in settings,
/// and hiding the HUD removes every badge. Nothing here can alter a run.
/// </para>
/// </remarks>
internal sealed class OfferBadges
{
    private const string BadgeName = "OssuaryRating";

    /// <summary>
    /// Above this many candidates, do nothing.
    /// </summary>
    /// <remarks>
    /// An offer screen shows a handful. Finding dozens means the rule for "is
    /// this being offered" has stopped being true — most likely a game update —
    /// and covering the screen in labels is a worse failure than showing none.
    /// </remarks>
    private const int ImplausibleCandidates = 20;

    /// <summary>Scanning the scene four times a second is imperceptible and cheap.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private readonly OssuarySettings _settings;
    private readonly List<Node> _found = new();

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

        var wanted = hudVisible && _settings.OfferRatings;

        var now = DateTime.UtcNow;
        if (now - _lastScan < Interval) return;
        _lastScan = now;

        try
        {
            _found.Clear();
            Collect(root, _found);

            if (!wanted)
            {
                foreach (var node in _found) Remove(node);
                return;
            }

            if (_found.Count > ImplausibleCandidates)
            {
                Log.Warn(
                    $"offer ratings: {_found.Count} candidates is more than an offer screen shows; "
                    + "skipping rather than covering the screen in labels");
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

    /// <summary>Walks the run's scene collecting things currently on offer.</summary>
    private static void Collect(Node node, List<Node> into)
    {
        // Our own HUD holds no offers and walking it wastes the budget.
        if (node.Name == "OssuaryHud") return;

        if (IsOffered(node)) into.Add(node);

        foreach (var child in node.GetChildren()) Collect(child, into);
    }

    private static bool IsOffered(Node node) => node switch
    {
        // A card that reports no pile is not in the deck, the hand, or any
        // combat pile — so it is being offered.
        NCard card => card.Model is { Pile: null },
        NRelic relic => relic.Model is not null && !HasAncestor<NRelicInventory>(relic),
        NPotion potion => potion.Model is not null && !HasAncestor<NPotionContainer>(potion),
        _ => false,
    };

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
        if (node.GetNodeOrNull(new NodePath(BadgeName)) is Label existing)
        {
            if (existing.Text != text) existing.Text = text;
            return;
        }

        if (node is not Control host) return;

        var badge = new Label
        {
            Name = BadgeName,
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            // Just above the node's top-left, which is clear of the art and the
            // title on every surface these appear on.
            Position = new Vector2(0, -24),
            ZIndex = 50,
        };
        badge.AddThemeColorOverride("font_color", Tint(kind, id));
        badge.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        badge.AddThemeConstantOverride("outline_size", 4);
        badge.AddThemeFontSizeOverride("font_size", Math.Max(8, (int)Math.Round(15 * _settings.ClampedTextScale)));

        host.AddChild(badge);

        if (!_announced)
        {
            _announced = true;
            Log.Info($"offer ratings: first badge attached to {node.GetType().Name}");
        }
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

        var parts = new List<string>(4) { $"{entry.Tier}  {entry.Score}" };
        parts.Add($"{entry.WinRate:0.#}% win");
        if (entry.PickRate is { } pick) parts.Add($"{pick:0.#}% pick");
        if (entry.Confidence == Confidence.Low) parts.Add("low sample");

        return string.Join("  ·  ", parts);
    }

    private static Color Tint(RatingKind kind, string id)
    {
        var table = Ratings.Table;
        if (table is null || !table.TryGet(kind, id, out var entry)) return new Color(0.55f, 0.58f, 0.56f);

        return entry.Tier switch
        {
            Tier.S => new Color(0.45f, 0.85f, 0.62f),
            Tier.A => new Color(0.62f, 0.85f, 0.52f),
            Tier.B => new Color(0.80f, 0.84f, 0.52f),
            Tier.C => new Color(0.85f, 0.75f, 0.45f),
            Tier.D => new Color(0.87f, 0.56f, 0.38f),
            _ => new Color(0.85f, 0.40f, 0.36f),
        };
    }
}
