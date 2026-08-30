namespace Ossuary.Combat;

/// <summary>
/// What one enemy is about to do, reduced to the facts the forecast needs.
/// </summary>
/// <remarks>
/// A flat record rather than the game's intent objects, so the summing and the
/// block arithmetic can be tested in CI on a machine with no copy of the game.
/// </remarks>
/// <param name="Source">The enemy's name, for the row label.</param>
/// <param name="Kind">
/// The intent as the game classifies it — <c>Attack</c>, <c>Defend</c>,
/// <c>Buff</c>, <c>Debuff</c>, <c>Unknown</c>. Carried as text so an intent type
/// added by a patch shows under its own name rather than being silently dropped.
/// </param>
/// <param name="DamagePerHit">
/// Damage of a single hit, already through the game's own modifier pipeline —
/// so strength, vulnerable and weak are accounted for rather than reimplemented.
/// Zero for anything that is not an attack.
/// </param>
/// <param name="Hits">Number of hits. Zero for anything that is not an attack.</param>
public readonly record struct IncomingIntent(
    string Source,
    string Kind,
    int DamagePerHit,
    int Hits)
{
    /// <summary>Total damage from this intent, before block.</summary>
    public int Total => DamagePerHit * Hits;

    public bool IsAttack => Hits > 0 && DamagePerHit > 0;
}
