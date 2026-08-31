using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using Ossuary.Team;

namespace Ossuary.State;

/// <summary>
/// Works out which debuffs each player in the party can actually apply.
/// </summary>
/// <remarks>
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
/// <b>Cost.</b> Building a model's hover tips allocates, and a party of four
/// with thirty-card decks is a few hundred models. So the answer is cached per
/// card type — whether Bash applies Vulnerable is a fact about Bash, not about
/// this copy of it — and the party is re-read on an interval rather than every
/// frame.
/// </para>
/// </remarks>
internal static class TeamReader
{
    /// <summary>Re-reading the party twice a second is imperceptible.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Whether a given card, relic or potion applies either debuff.
    /// </summary>
    /// <remarks>
    /// Keyed by model id and upgrade level, because an upgrade can change what
    /// a card does. Never cleared: the key space is the game's content, a few
    /// hundred entries at most.
    /// </remarks>
    private static readonly Dictionary<string, Debuffs> Known = new(StringComparer.Ordinal);

    private static RunState? _run;
    private static bool _subscribed;
    private static bool _failed;

    private static DateTime _lastRead = DateTime.MinValue;
    private static IReadOnlyList<TeamMemberAccess> _party = [];

    /// <summary>
    /// Starts listening for the run, so the party can be read outside combat too.
    /// </summary>
    /// <remarks>
    /// <c>RunManager.RunStarted</c> is raised from <c>Launch</c>, which every
    /// setup path funnels through — new, loaded, single player and multiplayer
    /// alike — so one subscription catches every run. The alternative,
    /// <c>DebugOnlyGetState</c>, is public but named to say it is not this.
    /// </remarks>
    internal static void Register()
    {
        if (_subscribed) return;

        try
        {
            RunManager.Instance.RunStarted += OnRunStarted;
            _subscribed = true;
            Log.Info("team reader registered");
        }
        catch (Exception ex)
        {
            _failed = true;
            Log.Error("team reader could not register; the party panel will stay empty", ex);
        }
    }

    private static void OnRunStarted(RunState state)
    {
        _run = state;
        _lastRead = DateTime.MinValue;
    }

    /// <summary>
    /// Drops what was derived from the previous run, and clears a latched
    /// failure so one bad run does not disable the panel for the session.
    /// </summary>
    /// <remarks>
    /// Deliberately does <b>not</b> clear <see cref="_run"/>. This is called
    /// from the HUD attach, which runs on <c>NRun._Ready</c> — after
    /// <c>RunManager.Launch</c> has already raised <c>RunStarted</c> for the
    /// run being set up. Clearing the reference here would throw away the state
    /// we had just been handed, and nothing would give it back until the
    /// <em>next</em> run: the panel would silently work only inside combat, via
    /// the fallback. The run reference has its own lifecycle — every new run
    /// replaces it.
    /// </remarks>
    internal static void Reset()
    {
        _party = [];
        _lastRead = DateTime.MinValue;
        _failed = false;
    }

    /// <summary>
    /// The party, or an empty list outside a run.
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
        // The run knows the party everywhere; the combat only knows it in a
        // fight. Prefer the run, fall back to the combat so the panel still
        // works if the subscription was never reached.
        var players = _run?.Players ?? CombatWatcher.Current?.Players;
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

        foreach (var card in player.Deck.Cards)
        {
            if (card is null) continue;
            Add(sources, card.Title, SourceKind.Card, Applies(card, $"{card.Id}/{card.CurrentUpgradeLevel}"));
        }

        foreach (var relic in player.Relics)
        {
            if (relic is null) continue;
            Add(sources, Text(relic.Title), SourceKind.Relic, Applies(relic, relic.Id.ToString()));
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

        // A deck holds four copies of Bash; the answer is the same for each and
        // the list is read by a person.
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
            // this runs over every card in every deck.
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
            RelicModel relic => relic.HoverTips,
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
        var vars = model switch
        {
            CardModel card => card.DynamicVars,
            RelicModel relic => relic.DynamicVars,
            _ => null,
        };

        if (vars is null) return Debuffs.None;

        var found = Debuffs.None;
        if (vars.ContainsKey(nameof(VulnerablePower))) found |= Debuffs.Vulnerable;
        if (vars.ContainsKey(nameof(WeakPower))) found |= Debuffs.Weak;
        return found;
    }
}
