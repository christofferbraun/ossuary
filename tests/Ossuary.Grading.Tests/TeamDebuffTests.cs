using Ossuary.Team;
using Xunit;

namespace Ossuary.Grading.Tests;

public class TeamDebuffTests
{
    private static TeamMemberAccess Member(string name, params DebuffSource[] sources) =>
        new(name, IsYou: false, sources);

    private static DebuffSource Card(string title, Debuffs applies) =>
        new(title, SourceKind.Card, applies);

    private static DebuffSource Relic(string title, Debuffs applies) =>
        new(title, SourceKind.Relic, applies);

    private static DebuffSource Potion(string title, Debuffs applies) =>
        new(title, SourceKind.Potion, applies);

    [Fact]
    public void A_member_with_nothing_has_nothing()
    {
        var member = Member("Ironclad");

        Assert.Equal(Debuffs.None, member.Available);
        Assert.False(member.Has(Debuffs.Vulnerable));
        Assert.Equal(Answer.No, member.AnswerFor(Debuffs.Weak));
    }

    [Fact]
    public void Sources_combine()
    {
        var member = Member("Ironclad", Card("Bash", Debuffs.Vulnerable), Relic("Red Mask", Debuffs.Weak));

        Assert.Equal(Debuffs.Both, member.Available);
        Assert.True(member.Has(Debuffs.Vulnerable));
        Assert.True(member.Has(Debuffs.Weak));
    }

    [Fact]
    public void One_source_can_apply_both()
    {
        var member = Member("Ironclad", Card("Uppercut", Debuffs.Both));

        Assert.Equal(Debuffs.Both, member.Available);
        Assert.Equal(Answer.Yes, member.AnswerFor(Debuffs.Vulnerable));
        Assert.Equal(Answer.Yes, member.AnswerFor(Debuffs.Weak));
    }

    /// <summary>
    /// The distinction the whole panel turns on: a potion is one use, so it is
    /// not the same answer as a card that does it every combat.
    /// </summary>
    [Fact]
    public void A_potion_is_not_a_yes()
    {
        var member = Member("Ironclad", Potion("Vulnerable Potion", Debuffs.Vulnerable));

        Assert.True(member.Has(Debuffs.Vulnerable));
        Assert.False(member.HasRepeatable(Debuffs.Vulnerable));
        Assert.Equal(Answer.PotionOnly, member.AnswerFor(Debuffs.Vulnerable));
    }

    [Fact]
    public void A_card_outranks_a_potion_for_the_same_debuff()
    {
        var member = Member(
            "Ironclad",
            Potion("Vulnerable Potion", Debuffs.Vulnerable),
            Card("Bash", Debuffs.Vulnerable));

        Assert.Equal(Answer.Yes, member.AnswerFor(Debuffs.Vulnerable));
        Assert.Equal("Bash", member.SourcesFor(Debuffs.Vulnerable).First().Title);
    }

    [Fact]
    public void Sources_for_a_debuff_exclude_the_other_one()
    {
        var member = Member("Ironclad", Card("Bash", Debuffs.Vulnerable), Relic("Red Mask", Debuffs.Weak));

        Assert.Equal(["Bash"], member.SourcesFor(Debuffs.Vulnerable).Select(s => s.Title));
        Assert.Equal(["Red Mask"], member.SourcesFor(Debuffs.Weak).Select(s => s.Title));
    }

    [Fact]
    public void The_party_is_covered_when_anyone_covers_it()
    {
        TeamMemberAccess[] party =
        [
            Member("Ironclad", Card("Bash", Debuffs.Vulnerable)),
            Member("Silent", Card("Clash", Debuffs.Weak)),
        ];

        Assert.Equal(Debuffs.None, TeamDebuffs.MissingFromParty(party));
    }

    [Fact]
    public void The_party_is_missing_what_nobody_has()
    {
        TeamMemberAccess[] party =
        [
            Member("Ironclad", Card("Bash", Debuffs.Vulnerable)),
            Member("Silent"),
        ];

        Assert.Equal(Debuffs.Weak, TeamDebuffs.MissingFromParty(party));
    }

    [Fact]
    public void A_party_with_nothing_is_missing_both()
    {
        TeamMemberAccess[] party = [Member("Ironclad"), Member("Silent")];

        Assert.Equal(Debuffs.Both, TeamDebuffs.MissingFromParty(party));
    }

    /// <summary>
    /// "Nobody has Vulnerable" is not a useful claim about no players, and
    /// saying it during loading would be a false alarm.
    /// </summary>
    [Fact]
    public void An_empty_party_reports_nothing_rather_than_everything()
    {
        Assert.Equal(Debuffs.None, TeamDebuffs.MissingFromParty([]));
        Assert.Equal(Debuffs.None, TeamDebuffs.PotionOnlyForParty([]));
    }

    [Fact]
    public void A_potion_counts_as_missing_at_party_level_but_is_reported_apart()
    {
        TeamMemberAccess[] party =
        [
            Member("Ironclad", Card("Bash", Debuffs.Vulnerable)),
            Member("Silent", Potion("Weak Potion", Debuffs.Weak)),
        ];

        Assert.Equal(Debuffs.Weak, TeamDebuffs.MissingFromParty(party));
        Assert.Equal(Debuffs.Weak, TeamDebuffs.PotionOnlyForParty(party));
    }

    [Fact]
    public void A_debuff_covered_by_a_card_is_not_reported_as_potion_only()
    {
        TeamMemberAccess[] party =
        [
            Member("Ironclad", Card("Bash", Debuffs.Vulnerable), Potion("Vulnerable Potion", Debuffs.Vulnerable)),
        ];

        Assert.Equal(Debuffs.Weak, TeamDebuffs.MissingFromParty(party));
        Assert.Equal(Debuffs.None, TeamDebuffs.PotionOnlyForParty(party));
    }
}
