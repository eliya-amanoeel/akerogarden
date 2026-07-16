using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the pure completion-form normaliser (NFR-26, §6.6): validation (names, the
/// full-board meals-required rule, dietary "Annat" notes, count/org guards) and the
/// server-authoritative build (no meals for self-catering, wrong-type/inactive options dropped,
/// snapshots captured, overnight breakfast always true).
/// </summary>
public class CompletionNormalizerTests
{
    private static readonly DateOnly CheckIn = new(2026, 8, 10);
    private static readonly DateOnly CheckOut = new(2026, 8, 12); // 2 nights → dinner, lunch+dinner
    private static readonly IReadOnlyList<MealSlot> Slots = MealsByDayCalculator.Slots(CheckIn, CheckOut);

    private static readonly MealOption Lunch1 = new() { Id = Guid.NewGuid(), Name = "Fisk", MealType = MealType.Lunch, IsActive = true };
    private static readonly MealOption Dinner1 = new() { Id = Guid.NewGuid(), Name = "Kyckling", MealType = MealType.Dinner, IsActive = true };

    private static Dictionary<Guid, MealOption> Options() =>
        new() { [Lunch1.Id] = Lunch1, [Dinner1.Id] = Dinner1 };

    // Slots for a 2-night stay are [Dinner(day1), Lunch(day2), Dinner(day2)].
    private static OvernightParticipantEntry FullBoardEntry(string name = "Anna") => new()
    {
        FullName = name,
        AgeCategory = AgeCategory.Adult,
        IsFullBoard = true,
        MealChoices = [Dinner1.Id.ToString(), Lunch1.Id.ToString(), Dinner1.Id.ToString()],
    };

    private static CompletionFormModel FormWith(params OvernightParticipantEntry[] entries) =>
        new() { Overnight = [.. entries] };

    // ── Validation ────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidFullBoardForm_NoErrors()
    {
        var errors = CompletionNormalizer.Validate(FormWith(FullBoardEntry()), Slots, 1);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_MissingName_ReportsError()
    {
        var entry = FullBoardEntry(name: "   ");
        var errors = CompletionNormalizer.Validate(FormWith(entry), Slots, 1);
        Assert.Contains(errors, e => e.Contains("namn", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FullBoardWithMissingMealChoice_ReportsError()
    {
        var entry = FullBoardEntry();
        entry.MealChoices[1] = null; // drop the lunch selection
        var errors = CompletionNormalizer.Validate(FormWith(entry), Slots, 1);
        Assert.Contains(errors, e => e.Contains("måltid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_SelfCatering_DoesNotRequireMeals()
    {
        var entry = new OvernightParticipantEntry
        {
            FullName = "Bo",
            IsFullBoard = false,
            MealChoices = [null, null, null],
        };
        var errors = CompletionNormalizer.Validate(FormWith(entry), Slots, 1);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_OtherDietaryWithoutNotes_ReportsError()
    {
        var entry = FullBoardEntry();
        entry.Dietary = [DietaryRequirement.Other];
        var errors = CompletionNormalizer.Validate(FormWith(entry), Slots, 1);
        Assert.Contains(errors, e => e.Contains("Annat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_CountMismatch_ReportsSingleError()
    {
        var errors = CompletionNormalizer.Validate(FormWith(FullBoardEntry()), Slots, 2);
        Assert.Single(errors);
        Assert.Contains(errors, e => e.Contains("Antalet deltagare"));
    }

    [Fact]
    public void Validate_OrganisationWithoutName_ReportsError()
    {
        var form = FormWith(FullBoardEntry());
        form.IsOrganisation = true;
        var errors = CompletionNormalizer.Validate(form, Slots, 1);
        Assert.Contains(errors, e => e.Contains("Organisationsnamn"));
    }

    // ── Build ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_FullBoard_CreatesMealsWithSnapshotsAndCorrectSlots()
    {
        var participants = CompletionNormalizer.BuildParticipants(FormWith(FullBoardEntry()), Slots, Options());

        var p = Assert.Single(participants);
        Assert.Equal(ParticipantType.Overnight, p.ParticipantType);
        Assert.True(p.AttendsBreakfast); // overnight guests always
        Assert.Equal(3, p.MealPreferences.Count);

        // Each meal preference matches its slot's date + type, and snapshots the option name.
        for (var j = 0; j < Slots.Count; j++)
        {
            var mp = p.MealPreferences.Single(m => m.Date == Slots[j].Date && m.MealType == Slots[j].MealType);
            var expected = Slots[j].MealType == MealType.Lunch ? Lunch1 : Dinner1;
            Assert.Equal(expected.Id, mp.MealOptionId);
            Assert.Equal(expected.Name, mp.MealOptionSnapshot);
        }
    }

    [Fact]
    public void Build_SelfCatering_HasNoMeals_EvenIfChoicesSubmitted()
    {
        var entry = FullBoardEntry();
        entry.IsFullBoard = false; // choices still present, but must be ignored

        var participants = CompletionNormalizer.BuildParticipants(FormWith(entry), Slots, Options());

        Assert.Empty(Assert.Single(participants).MealPreferences);
    }

    [Fact]
    public void Build_WrongTypeOption_IsDropped()
    {
        var entry = FullBoardEntry();
        entry.MealChoices[0] = Lunch1.Id.ToString(); // slot 0 is a Dinner slot — a lunch option is invalid here

        var participants = CompletionNormalizer.BuildParticipants(FormWith(entry), Slots, Options());

        var p = Assert.Single(participants);
        Assert.DoesNotContain(p.MealPreferences, m => m.Date == Slots[0].Date && m.MealType == MealType.Dinner);
        Assert.Equal(2, p.MealPreferences.Count); // the two valid slots remain
    }

    [Fact]
    public void Build_UnknownOption_IsDropped()
    {
        var entry = FullBoardEntry();
        entry.MealChoices[0] = Guid.NewGuid().ToString(); // not in the active option set

        var participants = CompletionNormalizer.BuildParticipants(FormWith(entry), Slots, Options());

        Assert.Equal(2, Assert.Single(participants).MealPreferences.Count);
    }

    [Fact]
    public void Validate_NullDietaryAndMealChoices_DoNotThrow()
    {
        // An unchecked checkbox group / un-posted select group binds to null on SSR post.
        var entry = new OvernightParticipantEntry
        {
            FullName = "Cecilia",
            IsFullBoard = false,
            MealChoices = null!,
            Dietary = null!,
        };
        var errors = CompletionNormalizer.Validate(FormWith(entry), Slots, 1);
        Assert.Empty(errors); // self-catering, no name/org issues
    }

    [Fact]
    public void Build_NullCollections_TreatedAsEmpty()
    {
        var entry = new OvernightParticipantEntry
        {
            FullName = "Cecilia",
            IsFullBoard = true,
            MealChoices = null!,
            Dietary = null!,
        };
        var participants = CompletionNormalizer.BuildParticipants(FormWith(entry), Slots, Options());

        var p = Assert.Single(participants);
        Assert.Empty(p.MealPreferences);       // null choices → no meals
        Assert.Empty(p.DietaryRequirements);   // null dietary → none
    }

    [Fact]
    public void EmptyStringChoice_IsTreatedAsUnselected()
    {
        // A placeholder <select> posts "" for an unselected slot — must count as missing, not a meal.
        var entry = FullBoardEntry();
        entry.MealChoices[1] = ""; // unselect the lunch slot

        Assert.Contains(
            CompletionNormalizer.Validate(FormWith(entry), Slots, 1),
            e => e.Contains("måltid", StringComparison.OrdinalIgnoreCase));

        entry.IsFullBoard = false; // now self-catering — the empty string must not crash the build
        var p = Assert.Single(CompletionNormalizer.BuildParticipants(FormWith(entry), Slots, Options()));
        Assert.Empty(p.MealPreferences);
    }

    [Fact]
    public void Validate_FullBoardWithNullMealChoices_ReportsMissingMeal()
    {
        var entry = new OvernightParticipantEntry { FullName = "Dan", IsFullBoard = true, MealChoices = null! };
        var errors = CompletionNormalizer.Validate(FormWith(entry), Slots, 1);
        Assert.Contains(errors, e => e.Contains("måltid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_DietaryNone_DroppedWhenRealRequirementPresent()
    {
        var entry = FullBoardEntry();
        entry.Dietary = [DietaryRequirement.None, DietaryRequirement.Vegan];

        var participants = CompletionNormalizer.BuildParticipants(FormWith(entry), Slots, Options());

        var p = Assert.Single(participants);
        Assert.Equal([DietaryRequirement.Vegan], p.DietaryRequirements);
    }
}
