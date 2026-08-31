using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using Ossuary.Combat;

namespace Ossuary.State;

/// <summary>
/// Reads what the enemies intend to do this turn.
/// </summary>
/// <remarks>
/// <para>
/// The chain is all public: <c>Creature.Monster.NextMove.Intents</c>, then
/// <c>AttackIntent.GetTotalDamage</c> and <c>Repeats</c> for the numbers. No
/// label is parsed and no sprite is inspected — which is what an external
/// overlay is reduced to, and why its forecast drifts whenever the game changes
/// how an intent is drawn.
/// </para>
/// <para>
/// <b>Why calling GetTotalDamage is safe.</b> It dispatches
/// <c>Hook.ModifyDamage</c>, and hook dispatch normally risks running other
/// models' side effects. Two things establish otherwise, both checked before
/// this was written. First, the game itself calls <c>GetTotalDamage</c> from
/// <c>AttackIntent.GetTexture</c> and <c>GetAnimation</c> to choose which intent
/// sprite and animation to show, so it is already a rendering path invoked on
/// every intent update. Second, all 38 damage modifiers were decompiled — 12
/// <c>ModifyDamageAdditive</c>, 26 <c>ModifyDamageMultiplicative</c>, 3
/// <c>ModifyDamageCap</c> — and none assigns to a field or property. Recorded in
/// <c>docs/COMPAT.md</c>.
/// </para>
/// <para>
/// Going through the game's own calculation rather than reimplementing it is
/// what makes strength, vulnerable and weak correct for free: they are modifiers
/// in that pipeline, so the number matches what the enemy will actually deal.
/// </para>
/// </remarks>
internal static class IntentReader
{
    private static bool _failed;

    /// <summary>Clears a latched failure so a new run starts fresh.</summary>
    internal static void Reset() => _failed = false;

    /// <summary>
    /// Every live enemy's intent, or an empty list if they cannot be read.
    /// </summary>
    internal static IReadOnlyList<IncomingIntent> Read(ICombatState state, IReadOnlyList<Creature> targets)
    {
        if (_failed) return [];

        try
        {
            var result = new List<IncomingIntent>();

            foreach (var enemy in state.Enemies)
            {
                // A dead enemy's intent is stale and would inflate the forecast.
                if (enemy is null || enemy.IsDead) continue;

                var intents = enemy.Monster?.NextMove?.Intents;
                if (intents is null) continue;

                foreach (var intent in intents)
                {
                    if (intent is null) continue;
                    result.Add(Convert(intent, enemy, targets));
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            // Once, then never again: this runs every frame, and a throwing hook
            // would otherwise fill the log rather than degrade the panel.
            _failed = true;
            Log.Error("enemy intents could not be read; the forecast is unavailable this session", ex);
            return [];
        }
    }

    private static IncomingIntent Convert(AbstractIntent intent, Creature owner, IReadOnlyList<Creature> targets)
    {
        var kind = intent.IntentType.ToString();

        if (intent is not AttackIntent attack)
        {
            return new IncomingIntent(owner.Name ?? "?", kind, DamagePerHit: 0, Hits: 0);
        }

        // Repeats is 1 for a single attack and the multi-hit count otherwise.
        // Total is taken from the game rather than multiplied here, so a subclass
        // that computes its total differently is still reported correctly.
        var hits = Math.Max(1, attack.Repeats);
        var total = attack.GetTotalDamage(targets, owner);
        var perHit = hits > 0 ? total / hits : total;

        return new IncomingIntent(owner.Name ?? "?", kind, perHit, hits);
    }
}
