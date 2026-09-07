using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the pure completion-form normaliser (NFR-26, §6.6): validation (names, the
/// meals-required rule, allergy "Annat" notes, preference-count and org guards) and the
/// server-authoritative build — the group's one choice per sitting (FR-105), wrong-type/inactive
/// options dropped, snapshots captured, and a roster carrying nothing but name, age and allergies.
/// </summary>
public class CompletionNormalizerTests
{
    private static readonly DateOnly Day1 = new(2026, 8, 10);
    private static readonly DateOnly Day2 = new(2026, 8, 11);
    private static readonly DateOnly Day3 = new(2026, 8, 12); // 2 nights

    private static readonly MealOption Lunch1 = new() { Id = Guid.NewGuid(), Name = "Fisk", MealType = MealType.Lunch, IsActive = true };
    private static readonly MealOption Dinner1 = new() { Id = Guid.NewGuid(), Name = "Kyckling", MealType = MealType.Dinner, IsActive = true };

    private static Dictionary<Guid, MealOption> Options() =>
        new() { [Lunch1.Id] = Lunch1, [Dinner1.Id] = Dinner1 };

    /// <summary>The standard-schedule resolution for the 2-night stay used throughout.
    /// Grid (AllSittings): 0=D1 lunch, 1=D1 dinner, 2=D2 lunch, 3=D2 dinner, 4=D3 lunch, 5=D3 dinner.
    /// Selectable: 1, 2, 3.</summary>
    private static ResolvedMealOccasions Occasions(
        MealPlan plan = MealPlan.FullBoardAll,
        bool arrivalLunch = false, bool departureLunch = false,
        bool dayGuestLunch = false, bool dayGuestDinner = false) =>
        MealOccasionResolver.Resolve(new MealOccasionInput(
            Day1, Day3, plan,
            false, arrivalLunch, true,
            true, departureLunch, false,
            false, dayGuestLunch, dayGuestDinner));

    /// <summary>A meal grid with the three standard-schedule sittings chosen and the rest blank.</summary>
    private static List<string?> StandardChoices() =>
        [null, Dinner1.Id.ToString(), Lunch1.Id.ToString(), Dinner1.Id.ToString(), null, null];

    private static CompletionFormModel FormWith(params string[] names)
    {
        var form = new CompletionFormModel { MealChoices = StandardChoices() };
        foreach (var name in names)
            form.Overnight.Add(new OvernightParticipantEntry { FullName = name, AgeCategory = AgeCategory.Adult });
        return form;
    }

    // ── Allergy consent (FR-110) ──────────────────────────────────────────────────

    [Fact]
    public void AllergyConsent_NoAllergyDeclared_NotRequired()
    {
        // The common case by far: no allergies, so there is no health data to consent to. Demanding
        // consent here would also make it non-freely-given (art. 7.4) for the guests who do declare.
        var form = FormWith("Anna");
        Assert.Empty(CompletionNormalizer.ValidateAllergyConsent(form, dayGuestsApplicable: true));
    }

    [Fact]
    public void AllergyConsent_AllergyDeclaredWithoutConsent_IsRejected()
    {
        var form = FormWith("Anna");
        form.Overnight[0].Allergies = [Allergy.Gluten];

        var errors = CompletionNormalizer.ValidateAllergyConsent(form, dayGuestsApplicable: true);
        Assert.Single(errors);
        Assert.Contains("samtycke", errors[0]);
    }

    [Fact]
    public void AllergyConsent_AllergyDeclaredWithConsent_IsAccepted()
    {
        var form = FormWith("Anna");
        form.Overnight[0].Allergies = [Allergy.Gluten];
        form.AllergyConsent = true;

        Assert.Empty(CompletionNormalizer.ValidateAllergyConsent(form, dayGuestsApplicable: true));
    }

    [Fact]
    public void AllergyConsent_OnlyANoteAndNoTickedBox_StillCountsAsDeclared()
    {
        // "Annat" plus free text is a declared allergy. Keying the rule on the ticked boxes alone
        // would let the most detailed health data on the form through with no consent at all.
        var form = FormWith("Anna");
        form.Overnight[0].AllergyNotes = "Reagerar på selleri";

        Assert.Single(CompletionNormalizer.ValidateAllergyConsent(form, dayGuestsApplicable: true));
    }

    [Fact]
    public void AllergyConsent_DayGuestAllergy_RequiresConsentOnlyWhenDayGuestsApply()
    {
        var form = FormWith("Anna");
        form.DayGuests.Add(new DayGuestEntry
        {
            FullName = "Bo",
            AttendanceDate = Day2.ToString("yyyy-MM-dd"),
            Allergies = [Allergy.Nut],
        });

        // Catered booking: the row will be stored, so its allergy needs consent.
        Assert.Single(CompletionNormalizer.ValidateAllergyConsent(form, dayGuestsApplicable: true));

        // Self-catering: day-guest rows are dropped server-side whatever was posted (§4.2), so this
        // allergy is never processed and must not demand consent for processing that never happens.
        Assert.Empty(CompletionNormalizer.ValidateAllergyConsent(form, dayGuestsApplicable: false));
    }

    // ── Validation ────────────────────────────────────────────────────────────────

    [Fact]
    public void Validate_ValidForm_NoErrors()
    {
        Assert.Empty(CompletionNormalizer.Validate(FormWith("Anna"), Occasions(), 1));
    }

    [Fact]
    public void Validate_MissingName_ReportsError()
    {
        var errors = CompletionNormalizer.Validate(FormWith("   "), Occasions(), 1);
        Assert.Contains(errors, e => e.Contains("namn", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_TooLongName_ReportsError()
    {
        var errors = CompletionNormalizer.Validate(FormWith(new string('x', 201)), Occasions(), 1);
        Assert.Contains(errors, e => e.Contains("200 tecken", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_MissingMealChoiceForATakenSitting_ReportsError()
    {
        var form = FormWith("Anna");
        form.MealChoices[2] = null; // drop the middle-day lunch
        var errors = CompletionNormalizer.Validate(form, Occasions(), 1);
        Assert.Contains(errors, e => e.Contains("måltid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_BlankChoiceForASittingTheGroupDidNotTake_IsNotAnError()
    {
        // Index 0 is the arrival-day lunch, which this group is not taking — the grid always posts
        // it, so a blank there must not block submission.
        var form = FormWith("Anna");
        Assert.Null(form.MealChoices[0]);
        Assert.Empty(CompletionNormalizer.Validate(form, Occasions(), 1));
    }

    [Fact]
    public void Validate_MealChosenForAnEdgeDayTheGroupAdded_IsRequired()
    {
        // Ticking arrival lunch makes index 0 a real sitting, so leaving it blank now *is* an error.
        var form = FormWith("Anna");
        var errors = CompletionNormalizer.Validate(form, Occasions(arrivalLunch: true), 1);
        Assert.Contains(errors, e => e.Contains("måltid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_SelfCatering_RequiresNoMeals()
    {
        var form = new CompletionFormModel { MealChoices = [] };
        form.Overnight.Add(new OvernightParticipantEntry { FullName = "Bo" });

        Assert.Empty(CompletionNormalizer.Validate(form, Occasions(MealPlan.SelfCateringAll), 1));
    }

    [Fact]
    public void Validate_WrongParticipantCount_ReportsMismatchAndStops()
    {
        var errors = CompletionNormalizer.Validate(FormWith("Anna"), Occasions(), 3);

        var error = Assert.Single(errors);
        Assert.Contains("Antalet deltagare", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_OtherAllergyWithoutNote_ReportsError()
    {
        var form = FormWith("Anna");
        form.Overnight[0].Allergies = [Allergy.Other];

        var errors = CompletionNormalizer.Validate(form, Occasions(), 1);

        Assert.Contains(errors, e => e.Contains("Annat", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_TooLongAllergyNote_ReportsError()
    {
        var form = FormWith("Anna");
        form.Overnight[0].AllergyNotes = new string('x', 201);

        var errors = CompletionNormalizer.Validate(form, Occasions(), 1);

        Assert.Contains(errors, e => e.Contains("200 tecken", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NullAllergyList_IsTreatedAsNone()
    {
        // An unticked checkbox group binds to null on an SSR post.
        var form = FormWith("Anna");
        form.Overnight[0].Allergies = null!;

        Assert.Empty(CompletionNormalizer.Validate(form, Occasions(), 1));
    }

    [Fact]
    public void Validate_OrganisationWithoutName_ReportsError()
    {
        var form = FormWith("Anna");
        form.IsOrganisation = true;

        var errors = CompletionNormalizer.Validate(form, Occasions(), 1);

        Assert.Contains(errors, e => e.Contains("Organisationsnamn", StringComparison.Ordinal));
    }

    // ── Dietary-preference counts (§4.6) ──────────────────────────────────────────

    [Fact]
    public void Validate_PreferenceCountsWithinHeadcount_NoError()
    {
        var form = FormWith("Anna", "Bo", "Cecilia");
        form.VegetarianCount = 2;
        form.VeganCount = 1;

        Assert.Empty(CompletionNormalizer.Validate(form, Occasions(), 3));
    }

    [Fact]
    public void Validate_PreferenceCountsExceedingHeadcount_ReportsError()
    {
        var form = FormWith("Anna", "Bo");
        form.VegetarianCount = 2;
        form.VeganCount = 1; // 3 portions for 2 people

        var errors = CompletionNormalizer.Validate(form, Occasions(), 2);

        Assert.Contains(errors, e => e.Contains("överstiga", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_PreferenceCountsMayCoverDayGuestsToo()
    {
        // Day guests eat what the group eats, so they count towards the portion ceiling.
        var form = FormWith("Anna");
        form.DayGuests.Add(new DayGuestEntry { FullName = "Britt", AttendanceDate = "2026-08-11" });
        form.VegetarianCount = 2;

        Assert.Empty(CompletionNormalizer.Validate(form, Occasions(), 1));
    }

    [Fact]
    public void Validate_SelfCatering_IgnoresPreferenceCountsEntirely()
    {
        // No meals means no portions to adapt, so a posted count is meaningless rather than wrong —
        // it is dropped on save. Validating it would block a legitimate self-catering submission.
        var form = new CompletionFormModel { MealChoices = [], VegetarianCount = 99 };
        form.Overnight.Add(new OvernightParticipantEntry { FullName = "Bo" });

        Assert.Empty(CompletionNormalizer.Validate(form, Occasions(MealPlan.SelfCateringAll), 1));
    }

    [Fact]
    public void Validate_NegativePreferenceCount_ReportsError()
    {
        var form = FormWith("Anna");
        form.VegetarianCount = -1;

        var errors = CompletionNormalizer.Validate(form, Occasions(), 1);

        Assert.Contains(errors, e => e.Contains("negativt", StringComparison.Ordinal));
    }

    // ── Building the group's meal selections (FR-105) ─────────────────────────────

    [Fact]
    public void BuildMealSelections_OneRowPerSittingTheGroupTakes()
    {
        var selections = CompletionNormalizer.BuildMealSelections(FormWith("Anna"), Occasions(), Options());

        Assert.Equal(3, selections.Count);
        Assert.Equal(
            new[]
            {
                (Day1, MealType.Dinner),
                (Day2, MealType.Lunch),
                (Day2, MealType.Dinner),
            },
            selections.Select(s => (s.Date, s.MealType)));
    }

    [Fact]
    public void BuildMealSelections_CapturesTheOptionNameSnapshot()
    {
        var selections = CompletionNormalizer.BuildMealSelections(FormWith("Anna"), Occasions(), Options());

        var lunch = selections.Single(s => s.MealType == MealType.Lunch);
        Assert.Equal(Lunch1.Id, lunch.MealOptionId);
        Assert.Equal("Fisk", lunch.MealOptionSnapshot);
    }

    [Fact]
    public void BuildMealSelections_IgnoresChoicesForSittingsTheGroupDidNotTake()
    {
        // A leftover value at the arrival-day lunch index must be discarded, not shifted onto a
        // different day — that is what indexing by the full grid protects against.
        var form = FormWith("Anna");
        form.MealChoices[0] = Lunch1.Id.ToString();

        var selections = CompletionNormalizer.BuildMealSelections(form, Occasions(), Options());

        Assert.Equal(3, selections.Count);
        Assert.DoesNotContain(selections, s => s.Date == Day1 && s.MealType == MealType.Lunch);
    }

    [Fact]
    public void BuildMealSelections_KeepsAnEdgeDayMealTheGroupAdded()
    {
        var form = FormWith("Anna");
        form.MealChoices[4] = Lunch1.Id.ToString(); // departure-day lunch

        var selections = CompletionNormalizer.BuildMealSelections(form, Occasions(departureLunch: true), Options());

        Assert.Contains(selections, s => s.Date == Day3 && s.MealType == MealType.Lunch);
    }

    [Fact]
    public void BuildMealSelections_DropsWrongTypeOption()
    {
        var form = FormWith("Anna");
        form.MealChoices[2] = Dinner1.Id.ToString(); // a dinner option posted for a lunch sitting

        var selections = CompletionNormalizer.BuildMealSelections(form, Occasions(), Options());

        Assert.DoesNotContain(selections, s => s.MealType == MealType.Lunch);
        Assert.Equal(2, selections.Count);
    }

    [Fact]
    public void BuildMealSelections_DropsInactiveOrUnknownOption()
    {
        var form = FormWith("Anna");
        form.MealChoices[2] = Guid.NewGuid().ToString(); // not in the active list

        var selections = CompletionNormalizer.BuildMealSelections(form, Occasions(), Options());

        Assert.DoesNotContain(selections, s => s.MealType == MealType.Lunch);
    }

    [Fact]
    public void BuildMealSelections_SelfCateringYieldsNothing()
    {
        var form = FormWith("Anna");

        var selections = CompletionNormalizer.BuildMealSelections(
            form, Occasions(MealPlan.SelfCateringAll), Options());

        Assert.Empty(selections);
    }

    [Fact]
    public void BuildMealSelections_NullChoiceListYieldsNothing()
    {
        var form = FormWith("Anna");
        form.MealChoices = null!;

        Assert.Empty(CompletionNormalizer.BuildMealSelections(form, Occasions(), Options()));
    }

    // ── Building the roster ───────────────────────────────────────────────────────

    [Fact]
    public void BuildParticipants_TrimsNameAndSetsType()
    {
        var form = FormWith("  Anna  ");

        var participant = Assert.Single(CompletionNormalizer.BuildParticipants(form));

        Assert.Equal("Anna", participant.FullName);
        Assert.Equal(ParticipantType.Overnight, participant.ParticipantType);
        Assert.Null(participant.AttendanceDate);
    }

    [Fact]
    public void BuildParticipants_KeepsAllergiesAndNotes()
    {
        var form = FormWith("Anna");
        form.Overnight[0].Allergies = [Allergy.Nut, Allergy.Other];
        form.Overnight[0].AllergyNotes = "  Undviker fläskkött.  ";

        var participant = Assert.Single(CompletionNormalizer.BuildParticipants(form));

        Assert.Equal([Allergy.Nut, Allergy.Other], participant.Allergies);
        Assert.Equal("Undviker fläskkött.", participant.AllergyNotes);
    }

    [Fact]
    public void BuildParticipants_DedupesAllergiesAndDropsNoneWhenARealOneIsPresent()
    {
        var form = FormWith("Anna");
        form.Overnight[0].Allergies = [Allergy.None, Allergy.Nut, Allergy.Nut];

        var participant = Assert.Single(CompletionNormalizer.BuildParticipants(form));

        Assert.Equal([Allergy.Nut], participant.Allergies);
    }

    [Fact]
    public void BuildParticipants_BlankAllergyNoteBecomesNull()
    {
        var form = FormWith("Anna");
        form.Overnight[0].AllergyNotes = "   ";

        var participant = Assert.Single(CompletionNormalizer.BuildParticipants(form));

        Assert.Null(participant.AllergyNotes);
    }

    // ── Day guests (§4.2) ─────────────────────────────────────────────────────────

    private static List<DayGuestEntry> DayGuests(params DayGuestEntry[] entries) => [.. entries];

    [Fact]
    public void ValidateDayGuests_ValidRow_NoErrors()
    {
        var errors = CompletionNormalizer.ValidateDayGuests(
            DayGuests(new DayGuestEntry { FullName = "Britt", AttendanceDate = "2026-08-11" }), Day1, Day3);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateDayGuests_BlankRowIsIgnored()
    {
        var errors = CompletionNormalizer.ValidateDayGuests(DayGuests(new DayGuestEntry()), Day1, Day3);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateDayGuests_DateOutsideStay_ReportsError()
    {
        var errors = CompletionNormalizer.ValidateDayGuests(
            DayGuests(new DayGuestEntry { FullName = "Britt", AttendanceDate = "2026-09-01" }), Day1, Day3);

        Assert.Contains(errors, e => e.Contains("inom vistelsen", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDayGuests_MissingDate_ReportsError()
    {
        var errors = CompletionNormalizer.ValidateDayGuests(
            DayGuests(new DayGuestEntry { FullName = "Britt" }), Day1, Day3);

        Assert.Contains(errors, e => e.Contains("vilken dag", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDayGuests_OtherAllergyWithoutNote_ReportsError()
    {
        var errors = CompletionNormalizer.ValidateDayGuests(
            DayGuests(new DayGuestEntry
            {
                FullName = "Britt",
                AttendanceDate = "2026-08-11",
                Allergies = [Allergy.Other],
            }), Day1, Day3);

        Assert.Contains(errors, e => e.Contains("Annat", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildDayGuests_SetsTypeDateAndAllergies()
    {
        var participants = CompletionNormalizer.BuildDayGuests(DayGuests(new DayGuestEntry
        {
            FullName = " Britt ",
            AttendanceDate = "2026-08-11",
            AgeCategory = AgeCategory.Child,
            Allergies = [Allergy.Gluten],
        }));

        var dayGuest = Assert.Single(participants);
        Assert.Equal("Britt", dayGuest.FullName);
        Assert.Equal(ParticipantType.DayGuest, dayGuest.ParticipantType);
        Assert.Equal(Day2, dayGuest.AttendanceDate);
        Assert.Equal(AgeCategory.Child, dayGuest.AgeCategory);
        Assert.Equal([Allergy.Gluten], dayGuest.Allergies);
    }

    [Fact]
    public void BuildDayGuests_SkipsBlankAndUndatedRows()
    {
        var participants = CompletionNormalizer.BuildDayGuests(DayGuests(
            new DayGuestEntry(),
            new DayGuestEntry { FullName = "Britt" },
            new DayGuestEntry { FullName = "Kerstin", AttendanceDate = "2026-08-11" }));

        Assert.Equal("Kerstin", Assert.Single(participants).FullName);
    }
}
