using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Models;
using AkeroGarden.Web.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the day-guest half of the completion normaliser (§4.2, FR-58): validation
/// (name + in-range date, blank rows ignored) and the server-authoritative build (single
/// attendance date, breakfast flag, up to a lunch + dinner meal preference, never full-board,
/// wrong-type/inactive options dropped).
/// </summary>
public class DayGuestNormalizerTests
{
    private static readonly DateOnly CheckIn = new(2026, 8, 10);
    private static readonly DateOnly CheckOut = new(2026, 8, 12);

    private static readonly MealOption Lunch = new() { Id = Guid.NewGuid(), Name = "Fisk", MealType = MealType.Lunch, IsActive = true };
    private static readonly MealOption Dinner = new() { Id = Guid.NewGuid(), Name = "Kyckling", MealType = MealType.Dinner, IsActive = true };

    private static Dictionary<Guid, MealOption> Options() => new() { [Lunch.Id] = Lunch, [Dinner.Id] = Dinner };

    private static DayGuestEntry Guest(string? name = "Kalle", string? date = "2026-08-11") => new()
    {
        FullName = name,
        AgeCategory = AgeCategory.Adult,
        AttendanceDate = date,
        AttendsBreakfast = true,
        LunchChoice = Lunch.Id.ToString(),
        DinnerChoice = Dinner.Id.ToString(),
    };

    // ── Validation ────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidGuest_NoErrors() =>
        Assert.Empty(CompletionNormalizer.ValidateDayGuests([Guest()], CheckIn, CheckOut));

    [Fact]
    public void Validate_BlankRow_IsIgnored()
    {
        var blank = new DayGuestEntry(); // no name, no date — an accidental "add"
        Assert.Empty(CompletionNormalizer.ValidateDayGuests([blank], CheckIn, CheckOut));
    }

    [Fact]
    public void Validate_NameMissing_ReportsError()
    {
        var errors = CompletionNormalizer.ValidateDayGuests([Guest(name: "  ")], CheckIn, CheckOut);
        Assert.Contains(errors, e => e.Contains("namn", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_DateMissing_ReportsError()
    {
        var errors = CompletionNormalizer.ValidateDayGuests([Guest(date: "")], CheckIn, CheckOut);
        Assert.Contains(errors, e => e.Contains("dag", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_DateOutsideStay_ReportsError()
    {
        var errors = CompletionNormalizer.ValidateDayGuests([Guest(date: "2026-09-01")], CheckIn, CheckOut);
        Assert.Contains(errors, e => e.Contains("inom vistelsen"));
    }

    [Fact]
    public void Validate_OtherDietaryWithoutNotes_ReportsError()
    {
        var g = Guest();
        g.Dietary = [DietaryRequirement.Other];
        var errors = CompletionNormalizer.ValidateDayGuests([g], CheckIn, CheckOut);
        Assert.Contains(errors, e => e.Contains("Annat", StringComparison.OrdinalIgnoreCase));
    }

    // ── Build ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_Guest_IsDayGuest_WithDateBreakfastAndMeals()
    {
        var p = Assert.Single(CompletionNormalizer.BuildDayGuests([Guest()], Options()));

        Assert.Equal(ParticipantType.DayGuest, p.ParticipantType);
        Assert.Equal(new DateOnly(2026, 8, 11), p.AttendanceDate);
        Assert.True(p.AttendsBreakfast);
        Assert.False(p.IsFullBoard);
        Assert.Null(p.RoomId);
        Assert.Equal(2, p.MealPreferences.Count); // lunch + dinner, both for the attendance date
        Assert.All(p.MealPreferences, mp => Assert.Equal(new DateOnly(2026, 8, 11), mp.Date));
        Assert.All(p.MealPreferences, mp => Assert.False(string.IsNullOrEmpty(mp.MealOptionSnapshot)));
    }

    [Fact]
    public void Build_BreakfastOnlyGuest_HasNoMeals()
    {
        var g = Guest();
        g.LunchChoice = "";
        g.DinnerChoice = "";
        var p = Assert.Single(CompletionNormalizer.BuildDayGuests([g], Options()));

        Assert.True(p.AttendsBreakfast);
        Assert.Empty(p.MealPreferences);
    }

    [Fact]
    public void Build_BlankRow_IsSkipped() =>
        Assert.Empty(CompletionNormalizer.BuildDayGuests([new DayGuestEntry()], Options()));

    [Fact]
    public void Build_WrongTypeOption_IsDropped()
    {
        var g = Guest();
        g.LunchChoice = Dinner.Id.ToString(); // a dinner option in the lunch slot
        var p = Assert.Single(CompletionNormalizer.BuildDayGuests([g], Options()));

        Assert.DoesNotContain(p.MealPreferences, mp => mp.MealType == MealType.Lunch);
        Assert.Single(p.MealPreferences); // only the valid dinner remains
    }
}
