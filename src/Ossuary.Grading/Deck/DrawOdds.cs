namespace Ossuary.Deck;

/// <summary>
/// The chance of drawing something, given what is left in the pile.
/// </summary>
/// <remarks>
/// This is the number the tracker exists to show. Counting what is left is
/// arithmetic anyone can do in their head; working out that three copies in a
/// nineteen-card pile is a 71% chance over five draws is not.
/// </remarks>
public static class DrawOdds
{
    /// <summary>
    /// The probability of drawing at least one of <paramref name="copies"/>
    /// within the next <paramref name="draws"/> cards, from a pile of
    /// <paramref name="pileSize"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hypergeometric, computed as the complement of drawing none:
    /// </para>
    /// <code>
    ///   P(none) = ((M-K) / M) · ((M-K-1) / (M-1)) · … for `draws` terms
    /// </code>
    /// <para>
    /// Written as a running product rather than with binomial coefficients on
    /// purpose — <c>C(M, N)</c> overflows a <see cref="long"/> well inside the
    /// range a real draw pile reaches, and the product form stays exact enough
    /// in double precision while never leaving the interval it should.
    /// </para>
    /// <para>
    /// This deliberately ignores anything that changes the pile mid-draw: a
    /// shuffle triggered by running out, a card that draws more cards, a
    /// scry. Those make the true odds better or worse in ways that depend on
    /// what the player does next, and a number that is honest about one turn is
    /// more useful than a guess that pretends to model the whole combat.
    /// </para>
    /// </remarks>
    public static double AtLeastOne(int copies, int pileSize, int draws)
    {
        if (copies <= 0 || draws <= 0 || pileSize <= 0) return 0;

        // Drawing the whole pile, or more of it than remains, is a certainty.
        if (draws >= pileSize) return 1;

        // More copies than the pile holds means the caller and the pile
        // disagree; the honest answer to "will I see one" is still yes.
        if (copies >= pileSize) return 1;

        var missing = pileSize - copies;
        var pNone = 1.0;

        for (var i = 0; i < draws; i++)
        {
            // Once the non-copies run out, drawing none becomes impossible.
            if (missing - i <= 0) return 1;
            pNone *= (double)(missing - i) / (pileSize - i);
        }

        return 1 - pNone;
    }
}
