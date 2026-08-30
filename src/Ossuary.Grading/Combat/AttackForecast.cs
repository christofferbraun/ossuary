namespace Ossuary.Combat;

/// <summary>
/// What this turn is about to cost, given what the enemies intend.
/// </summary>
/// <param name="Damage">Total incoming damage across every attacking enemy, before block.</param>
/// <param name="Hits">
/// Number of separate hits. Worth showing next to the total because they are
/// different questions: eight damage in one hit and eight in four hits are the
/// same number and very different decisions when something triggers per hit.
/// </param>
/// <param name="Block">Block the player currently holds.</param>
/// <param name="CurrentHp">Health before the turn resolves.</param>
public readonly record struct Forecast(int Damage, int Hits, int Block, int CurrentHp)
{
    /// <summary>
    /// Health actually lost, after block absorbs what it can.
    /// </summary>
    /// <remarks>
    /// Block is a single pool spent across every hit in the turn, so absorbing
    /// hit by hit and subtracting the total give the same answer. Subtraction is
    /// used because it is the one a reader can check.
    /// </remarks>
    public int HpLoss => Math.Max(0, Damage - Block);

    /// <summary>Health remaining if nothing else changes, floored at zero.</summary>
    public int HpAfter => Math.Max(0, CurrentHp - HpLoss);

    /// <summary>
    /// Whether the turn kills the player as things currently stand.
    /// </summary>
    /// <remarks>
    /// The single most valuable thing the forecast can say, and the reason it
    /// exists: it is the difference between blocking and playing for tempo.
    /// </remarks>
    public bool IsLethal => HpLoss >= CurrentHp;

    /// <summary>Block still needed to take nothing at all.</summary>
    public int BlockShortfall => Math.Max(0, Damage - Block);
}

/// <summary>Sums what the enemies intend into a single forecast.</summary>
public static class AttackForecast
{
    /// <summary>
    /// Builds the forecast for a turn.
    /// </summary>
    /// <remarks>
    /// Non-attack intents are counted but contribute no damage, so a turn where
    /// every enemy buffs reads as zero incoming rather than as no information.
    /// </remarks>
    public static Forecast Of(IEnumerable<IncomingIntent> intents, int block, int currentHp)
    {
        ArgumentNullException.ThrowIfNull(intents);

        var damage = 0;
        var hits = 0;

        foreach (var intent in intents)
        {
            if (!intent.IsAttack) continue;
            damage += intent.Total;
            hits += intent.Hits;
        }

        return new Forecast(damage, hits, Math.Max(0, block), Math.Max(0, currentHp));
    }
}
