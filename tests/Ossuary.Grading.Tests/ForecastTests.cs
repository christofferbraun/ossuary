using Ossuary.Combat;
using Xunit;

namespace Ossuary.Grading.Tests;

public class AttackForecastTests
{
    private static IncomingIntent Attack(string source, int damage, int hits = 1) =>
        new(source, "Attack", damage, hits);

    private static IncomingIntent NonAttack(string source, string kind) =>
        new(source, kind, 0, 0);

    [Fact]
    public void SumsDamageAndHitsAcrossEnemies()
    {
        var f = AttackForecast.Of([Attack("Cultist", 6), Attack("Jaw Worm", 11)], block: 0, currentHp: 70);

        Assert.Equal(17, f.Damage);
        Assert.Equal(2, f.Hits);
    }

    [Fact]
    public void MultiHitCountsEveryHit()
    {
        var f = AttackForecast.Of([Attack("Louse", 4, hits: 3)], block: 0, currentHp: 70);

        Assert.Equal(12, f.Damage);
        Assert.Equal(3, f.Hits);
    }

    /// <summary>
    /// A turn where everything buffs is zero incoming, not "no information".
    /// </summary>
    [Fact]
    public void NonAttackIntentsAddNoDamage()
    {
        var f = AttackForecast.Of(
            [NonAttack("Cultist", "Buff"), NonAttack("Slime", "Defend")], block: 0, currentHp: 70);

        Assert.Equal(0, f.Damage);
        Assert.Equal(0, f.Hits);
        Assert.False(f.IsLethal);
    }

    [Fact]
    public void BlockAbsorbsWhatItCan()
    {
        var f = AttackForecast.Of([Attack("Jaw Worm", 11)], block: 8, currentHp: 70);

        Assert.Equal(3, f.HpLoss);
        Assert.Equal(67, f.HpAfter);
    }

    [Fact]
    public void SurplusBlockDoesNotHeal()
    {
        var f = AttackForecast.Of([Attack("Cultist", 6)], block: 20, currentHp: 70);

        Assert.Equal(0, f.HpLoss);
        Assert.Equal(70, f.HpAfter);
    }

    /// <summary>
    /// The forecast's whole reason for existing: the difference between
    /// blocking and playing for tempo.
    /// </summary>
    [Fact]
    public void ReportsLethal()
    {
        var lethal = AttackForecast.Of([Attack("Boss", 30)], block: 0, currentHp: 30);
        var survived = AttackForecast.Of([Attack("Boss", 30)], block: 1, currentHp: 30);

        Assert.True(lethal.IsLethal);
        Assert.Equal(0, lethal.HpAfter);

        Assert.False(survived.IsLethal);
        Assert.Equal(1, survived.HpAfter);
    }

    [Fact]
    public void ExactlyEnoughBlockIsNotLethal()
    {
        var f = AttackForecast.Of([Attack("Boss", 30)], block: 30, currentHp: 5);

        Assert.Equal(0, f.HpLoss);
        Assert.False(f.IsLethal);
    }

    [Fact]
    public void ReportsHowMuchMoreBlockIsNeeded()
    {
        var f = AttackForecast.Of([Attack("Cultist", 6), Attack("Jaw Worm", 11)], block: 5, currentHp: 70);

        Assert.Equal(12, f.BlockShortfall);
    }

    [Fact]
    public void NothingIncomingIsSurvivable()
    {
        var f = AttackForecast.Of([], block: 0, currentHp: 1);

        Assert.Equal(0, f.Damage);
        Assert.False(f.IsLethal);
        Assert.Equal(1, f.HpAfter);
    }

    /// <summary>
    /// Negative block or health would come from a misread rather than from the
    /// game; the forecast clamps instead of producing nonsense.
    /// </summary>
    [Fact]
    public void ClampsImplausibleInputs()
    {
        var f = AttackForecast.Of([Attack("Cultist", 6)], block: -5, currentHp: -2);

        Assert.Equal(0, f.Block);
        Assert.Equal(0, f.CurrentHp);
        Assert.Equal(6, f.HpLoss);
        Assert.True(f.IsLethal);
    }

    [Fact]
    public void ADeadEnemysIntentStillSumsIfPassedIn()
    {
        // Filtering the dead is the reader's job, not the arithmetic's; this
        // pins the split so neither side assumes the other did it.
        var f = AttackForecast.Of([Attack("Cultist", 6), Attack("Cultist", 6)], block: 0, currentHp: 70);

        Assert.Equal(12, f.Damage);
    }
}

public class IncomingIntentTests
{
    [Fact]
    public void TotalIsDamageTimesHits()
    {
        Assert.Equal(12, new IncomingIntent("Louse", "Attack", 4, 3).Total);
    }

    [Fact]
    public void ZeroDamageOrZeroHitsIsNotAnAttack()
    {
        Assert.False(new IncomingIntent("Cultist", "Buff", 0, 0).IsAttack);
        Assert.False(new IncomingIntent("Odd", "Attack", 5, 0).IsAttack);
        Assert.False(new IncomingIntent("Odd", "Attack", 0, 2).IsAttack);
        Assert.True(new IncomingIntent("Jaw Worm", "Attack", 11, 1).IsAttack);
    }
}
