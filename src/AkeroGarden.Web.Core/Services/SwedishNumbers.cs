namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Small counts written as Swedish words, for copy that states a number counted from the data —
/// "Åtta hus, en gård" — without a page ever stating a count the data no longer bears out.
/// Neuter forms ("ett"), because the counted nouns on the site (hus, rum) are neuter.
/// Pure, so it is unit-testable (NFR-26).
/// </summary>
public static class SwedishNumbers
{
    private static readonly string[] Words =
        ["noll", "ett", "två", "tre", "fyra", "fem", "sex", "sju", "åtta", "nio", "tio", "elva", "tolv"];

    /// <summary>The word for 0–12 ("åtta"); digits above that, where words stop reading naturally.</summary>
    public static string Word(int count) =>
        count >= 0 && count < Words.Length ? Words[count] : count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary><see cref="Word"/> with a capital first letter, for the start of a heading or sentence.</summary>
    public static string CapitalisedWord(int count)
    {
        var word = Word(count);
        return char.ToUpper(word[0], System.Globalization.CultureInfo.InvariantCulture) + word[1..];
    }
}
