namespace Ossuary.Grading;

/// <summary>One entity's community numbers, as Spire Codex reports them.</summary>
/// <param name="Id">The game's own id, e.g. <c>BALL_LIGHTNING</c>.</param>
/// <param name="Score">Codex score, 0-100. The value bands are derived from.</param>
/// <param name="WinRate">Win rate as a percentage, e.g. <c>52.6</c>.</param>
/// <param name="PickRate">
/// Pick rate as a percentage, or null. Null for relics and potions, which are
/// not drafted from a ranked offer set — rendering a zero there would read as
/// "picked 0% of the time" rather than "not a meaningful question".
/// </param>
/// <param name="Picks">Times this was picked, across all recorded runs.</param>
public readonly record struct Rating(
    string Id,
    int Score,
    double WinRate,
    double? PickRate,
    long Picks)
{
    public Confidence Confidence => ConfidenceRules.For(Picks);
}
