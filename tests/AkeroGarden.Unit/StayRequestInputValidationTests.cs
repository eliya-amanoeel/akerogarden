using System.ComponentModel.DataAnnotations;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;

namespace AkeroGarden.Unit;

/// <summary>
/// Cross-field rules on the public stay-request form (<see cref="StayRequestInput"/>). Focus here is
/// the Conditional org-name rule (§8): naming the organisation is required only once the guest ticks
/// "the booking is for an organisation".
/// </summary>
public class StayRequestInputValidationTests
{
    /// <summary>A model that passes every property-level and cross-field rule, so a test can flip one
    /// field and assert the effect in isolation.</summary>
    private static StayRequestInput Valid() => new()
    {
        ContactName = "Anna Andersson",
        ContactEmail = "anna@example.se",
        ConfirmEmail = "anna@example.se",
        CheckIn = new DateOnly(2026, 8, 1),
        CheckOut = new DateOnly(2026, 8, 3),
        OvernightGuestCount = 4,
        MealPlan = MealPlan.FullBoardAll,
        AcceptPrivacyPolicy = true,
    };

    private static List<ValidationResult> Validate(StayRequestInput model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Baseline_model_is_valid() => Assert.Empty(Validate(Valid()));

    [Fact]
    public void Not_an_organisation_leaves_org_name_optional()
    {
        var model = Valid();
        model.IsOrganisation = false;
        model.OrgName = null;
        Assert.Empty(Validate(model));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Organisation_without_a_name_is_rejected(string? orgName)
    {
        var model = Valid();
        model.IsOrganisation = true;
        model.OrgName = orgName;

        var results = Validate(model);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(StayRequestInput.OrgName)));
    }

    [Fact]
    public void Organisation_with_a_name_is_valid()
    {
        var model = Valid();
        model.IsOrganisation = true;
        model.OrgName = "Norrtälje Scoutkår";
        // OrgNumber and InvoiceAddress stay optional even for an organisation.
        Assert.Empty(Validate(model));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_email_confirmation_is_rejected(string? confirm)
    {
        var model = Valid();
        model.ConfirmEmail = confirm!;
        Assert.Contains(Validate(model), r => r.MemberNames.Contains(nameof(StayRequestInput.ConfirmEmail)));
    }

    [Fact]
    public void Mismatched_email_confirmation_is_rejected()
    {
        var model = Valid();
        model.ConfirmEmail = "typo@example.se";
        Assert.Contains(Validate(model), r => r.MemberNames.Contains(nameof(StayRequestInput.ConfirmEmail)));
    }

    [Theory]
    [InlineData("ANNA@EXAMPLE.SE")]   // case-insensitive
    [InlineData("  anna@example.se ")] // surrounding whitespace ignored
    public void Email_confirmation_matches_ignoring_case_and_whitespace(string confirm)
    {
        var model = Valid();
        model.ConfirmEmail = confirm;
        Assert.Empty(Validate(model));
    }
}
