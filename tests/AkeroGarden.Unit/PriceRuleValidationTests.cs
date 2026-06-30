using System.ComponentModel.DataAnnotations;
using AkeroGarden.Web.Data.Entities;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the price-rule season-range rule (NFR-26). The rule is a pure
/// IValidatableObject check, so it is testable without a database.
/// </summary>
public class PriceRuleValidationTests
{
    private static List<ValidationResult> Validate(PriceRule rule) =>
        rule.Validate(new ValidationContext(rule)).ToList();

    [Fact]
    public void SeasonEndBeforeStart_IsInvalid()
    {
        var rule = new PriceRule
        {
            SeasonStart = new DateOnly(2026, 9, 30),
            SeasonEnd = new DateOnly(2026, 4, 1)
        };

        var results = Validate(rule);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(PriceRule.SeasonEnd)));
    }

    [Fact]
    public void SeasonEndAfterStart_IsValid()
    {
        var rule = new PriceRule
        {
            SeasonStart = new DateOnly(2026, 4, 1),
            SeasonEnd = new DateOnly(2026, 9, 30)
        };

        Assert.Empty(Validate(rule));
    }

    [Fact]
    public void NoSeasonDates_IsValid()
    {
        Assert.Empty(Validate(new PriceRule()));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void OnlyOneSeasonDate_IsValid(bool hasStart, bool hasEnd)
    {
        var rule = new PriceRule
        {
            SeasonStart = hasStart ? new DateOnly(2026, 4, 1) : null,
            SeasonEnd = hasEnd ? new DateOnly(2026, 9, 30) : null
        };

        Assert.Empty(Validate(rule));
    }

    [Fact]
    public void NewPriceRule_IsActiveByDefault()
    {
        Assert.True(new PriceRule().IsActive);
    }
}
