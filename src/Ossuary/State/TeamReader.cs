using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using Ossuary.Team;

namespace Ossuary.State;

/// <summary>
/// Works out which debuffs each player in the party can apply this turn.
/// </summary>
/// <remarks>
/// <para>
/// <b>This turn, not this run.</b> The sources are the cards in a player's
/// hand right now, plus the potions in their belt. Their deck is deliberately
/// not consulted: a card three shuffles away does not help with the enemy in
/// front of you, and answering "yes" off it would make the panel agree with
/// itself all run while being wrong on most turns. Relics are out for the same
/// reason — a relic that applied Weak at the start of combat has already done
/// it, which is a state the enemy is in rather than something a player can
/// choose to do now.
/// </para>
/// <para>
/// That makes this a combat-only reading. Outside a fight there is no hand, so
/// there is no question to answer.
/// </para>
/// <para>
/// <b>How a source is recognised.</b> Not by reading card text, which would
/// break in every language but English and would confuse "Weak" with
/// "Weakened". Two structured signals, either of which is enough:
/// </para>
/// <list type="number">
/// <item><see cref="IHoverTip.CanonicalModel"/> — every model that applies a
/// power declares a hover tip for it, because that tip is the tooltip the
/// player reads. The tip names the model it is for, so a
/// <see cref="VulnerablePower"/> tip on a card is the card telling us what it
/// does.</item>
/// <item>the model's dynamic vars. <c>PowerVar&lt;T&gt;</c> names itself after
/// its power, so a card carrying a <c>VulnerablePower</c> var applies
/// Vulnerable. This catches anything that applies a power without also
/// declaring a tip for it.</item>
/// </list>
/// <para>
/// Both come from the game's own declarations rather than from a list of card
/// ids maintained here, so a card added in a patch is recognised without
/// Ossuary being changed.
/// </para>
/// <para>
/// <b>Cost.</b> A hand is a handful of cards, so a party of four is a few dozen
/// models rather than a few hundred — but building a model's hover tips
/// allocates, so the answer is still cached per card type. Whether Bash applies
/// Vulnerable is a fact about Bash, not about this copy of it.
/// </para>
/// </remarks>
internal static class TeamReader
{
    /// <summary>
    /// Hands change during a turn, so this is read often — but four times a
    /// second is still far more often than a card can be played.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Whether a given card or potion applies either debuff.
    /// </summary>
    /// <remarks>
    /// Keyed by model id and upgrade level, because an upgrade can change what
    /// a card does. Never cleared: the key space is the game's content, a few
    /// hundred entries at most.
    /// </remarks>
    private static readonly Dictionary<string, Debuffs> Known = new(StringComparer.Ordinal);

    private static bool _failed;
    private static DateTime _lastRead = DateTime.MinValue;
    private static IReadOnlyList<TeamMemberAccess> _party = [];

    /// <summary>
    /// Drops what was derived from the previous run, and clears a latched
    /// failure so one bad run does not disable the panel for the session.
    /// </summary>
    internal static void Reset()
    {
        _party = [];
        _lastRead = DateTime.MinValue;
        _failed = false;
    }

    /// <summary>
    /// The party, or an empty list outside combat.
    /// </summary>
    internal static IReadOnlyList<TeamMemberAccess> Party()
    {
        if (_failed) return [];

        var now = DateTime.UtcNow;
        if (now - _lastRead < Interval) return _party;
        _lastRead = now;

        try
        {
            _party = Read();
        }
        catch (Exception ex)
        {
            _failed = true;
            _party = [];
            Log.Error("reading the party failed; the panel is disabled for this session", ex);
        }

        return _party;
    }

    private static IReadOnlyList<TeamMemberAccess> Read()
    {
        // Combat only. A hand exists nowhere else, and CombatWatcher already
        // knows when a fight is live - including that it has ended, which is
        // the part that is easy to get wrong.
        var state = CombatWatcher.Current;
        var players = state?.Players;
        if (players is not { Count: > 0 }) return [];

        var local = CombatWatcher.LocalPlayer;
        var party = new List<TeamMemberAccess>(players.Count);

        foreach (var player in players)
        {
            if (player is null) continue;
            party.Add(ReadPlayer(player, isYou: local is null ? party.Count == 0 : player.NetId == local.NetId));
        }

        return party;
    }

    private static TeamMemberAccess ReadPlayer(Player player, bool isYou)
    {
        var sources = new List<DebuffSource>();

        var hand = CardPile.Get(PileType.Hand, player)?.Cards;
        if (hand is not null)
        {
            foreach (var card in hand)
            {
                if (card is null) continue;
                Add(sources, card.Title, SourceKind.Hand, Applies(card, $"{card.Id}/{card.CurrentUpgradeLevel}"));
            }
        }

        foreach (var potion in player.Potions)
        {
            if (potion is null) continue;
            Add(sources, Text(potion.Title), SourceKind.Potion, Applies(potion, potion.Id.ToString()));
        }

        return new TeamMemberAccess(Name(player), isYou, sources);
    }

    private static void Add(List<DebuffSource> sources, string? title, SourceKind kind, Debuffs applies)
    {
        if (applies == Debuffs.None) return;

        // Two copies of Bash in hand is the same answer as one, and the list is
        // read by a person.
        var name = title ?? "?";
        if (sources.Any(s => s.Kind == kind && s.Title == name)) return;

        sources.Add(new DebuffSource(name, kind, applies));
    }

    /// <summary>Resolves a localised string, or nothing if it will not resolve.</summary>
    private static string? Text(MegaCrit.Sts2.Core.Localization.LocString? loc)
    {
        try
        {
            return loc?.GetFormattedText();
        }
        catch
        {
            return null;
        }
    }

    private static string Name(Player player)
    {
        // The character is what distinguishes players in co-op and is always
        // known, whereas a Steam display name is only there in a lobby.
        try
        {
            return player.Character?.Title.GetFormattedText() ?? "player";
        }
        catch
        {
            return "player";
        }
    }

    /// <summary>
    /// Which debuffs this model can apply, from the game's own declarations.
    /// </summary>
    private static Debuffs Applies(AbstractModel model, string key)
    {
        if (Known.TryGetValue(key, out var cached)) return cached;

        var found = Debuffs.None;

        try
        {
            found |= FromHoverTips(model);
            found |= FromDynamicVars(model);
        }
        catch (Exception)
        {
            // A model that cannot describe itself is not a source. Not logged:
            // this runs over every card in every hand.
        }

        Known[key] = found;
        return found;
    }

    /// <summary>
    /// Reads the tooltips the model itself declares.
    /// </summary>
    private static Debuffs FromHoverTips(AbstractModel model)
    {
        var tips = model switch
        {
            CardModel card => card.HoverTips,
            PotionModel potion => potion.HoverTips,
            _ => null,
        };

        if (tips is null) return Debuffs.None;

        var found = Debuffs.None;
        foreach (var tip in tips)
        {
            found |= tip?.CanonicalModel switch
            {
                VulnerablePower => Debuffs.Vulnerable,
                WeakPower => Debuffs.Weak,
                _ => Debuffs.None,
            };
        }

        return found;
    }

    /// <summary>
    /// Reads the model's declared values.
    /// </summary>
    /// <remarks>
    /// <c>PowerVar&lt;VulnerablePower&gt;</c> registers itself under
    /// <c>typeof(T).Name</c>, so the var set is keyed by the power's type name.
    /// This is a second, independent signal: a model that applies a power but
    /// declares no tip for it is still caught.
    /// </remarks>
    private static Debuffs FromDynamicVars(AbstractModel model)
    {
        if (model is not CardModel card) return Debuffs.None;

        var found = Debuffs.None;
        if (card.DynamicVars.ContainsKey(nameof(VulnerablePower))) found |= Debuffs.Vulnerable;
        if (card.DynamicVars.ContainsKey(nameof(WeakPower))) found |= Debuffs.Weak;
        return found;
    }
}
