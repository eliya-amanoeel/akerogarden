using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// The counted headline "Åtta hus, en gård" on the home page (<see cref="SwedishNumbers"/>): the
/// number is the data's, the word form is this helper's.
/// </summary>
public class SwedishNumbersTests
{
    [Theory]
    [InlineData(0, "noll")]
    [InlineData(1, "ett")] // neuter — "ett hus", "ett rum"
    [InlineData(2, "två")]
    [InlineData(8, "åtta")]
    [InlineData(12, "tolv")]
    public void Small_counts_are_words(int count, string expected)
    {
        Assert.Equal(expected, SwedishNumbers.Word(count));
    }

    [Theory]
    [InlineData(13, "13")]
    [InlineData(79, "79")]
    [InlineData(-1, "-1")]
    public void Counts_outside_zero_to_twelve_are_digits(int count, string expected)
    {
        Assert.Equal(expected, SwedishNumbers.Word(count));
    }

    [Theory]
    [InlineData(8, "Åtta")]
    [InlineData(1, "Ett")]
    [InlineData(14, "14")]
    public void A_capitalised_word_starts_a_heading(int count, string expected)
    {
        Assert.Equal(expected, SwedishNumbers.CapitalisedWord(count));
    }
}
