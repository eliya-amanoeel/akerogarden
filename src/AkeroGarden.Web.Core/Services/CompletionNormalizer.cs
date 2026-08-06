using System.Globalization;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Pure, server-authoritative validation + construction of the completion form's overnight
/// participants (NFR-26, §6.6). Mirrors <see cref="InquiryNormalizer"/>: the browser shows/hides
/// the meal grid on the full-board toggle, but these rules re-apply on the server regardless of
/// what the client posts — self-catering participants get no meals, a meal option of the wrong
/// type or one that is not currently active is dropped, and meals are only accepted for the
/// server-derived slots of the stay (§4.1). Snapshots (<see cref="MealPreference.MealOptionSnapshot"/>)
/// are captured here at build time (NFR-15).
/// </summary>
public static class CompletionNormalizer
{
    /// <summary>
    /// Validates the submitted form against the booking's expected overnight count and the
    /// server-derived meal slots. Returns Swedish error messages (empty ⇒ valid). Kept separate
    /// from <see cref="BuildParticipants"/> so the page can re-render errors without persisting.
    /// </summary>
    public static List<string> Validate(
        CompletionFormModel form,
        IReadOnlyList<MealSlot> slots,
        int expectedOvernightCount)
    {
        var errors = new List<string>();

        if (form.Overnight.Count != expectedOvernightCount)
        {
            // A mismatch means the posted rows do not match the booking — do not try to validate
            // per-row against a different shape.
            errors.Add("Antalet deltagare stämmer inte med bokningen. Ladda om sidan och försök igen.");
            return errors;
        }

        for (var i = 0; i < form.Overnight.Count; i++)
        {
            var entry = form.Overnight[i];
            var label = $"Deltagare {i + 1}";

            if (string.IsNullOrWhiteSpace(entry.FullName))
                errors.Add($"{label}: namn krävs.");
            else if (entry.FullName.Trim().Length > 200)
                errors.Add($"{label}: namnet får vara högst 200 tecken.");

            if (entry.IsFullBoard && SlotsMissingChoice(entry, slots))
                errors.Add($"{label}: välj måltid för alla dagar.");

            // An unchecked checkbox group binds to null on SSR post — treat as no requirements.
            if ((entry.Dietary?.Contains(DietaryRequirement.Other) ?? false) && string.IsNullOrWhiteSpace(entry.DietaryNotes))
                errors.Add($"{label}: beskriv kostbehovet i fältet för 'Annat'.");

            if (!string.IsNullOrEmpty(entry.DietaryNotes) && entry.DietaryNotes.Length > 200)
                errors.Add($"{label}: kostanteckningen får vara högst 200 tecken.");
        }

        if (form.IsOrganisation && string.IsNullOrWhiteSpace(form.OrgName))
            errors.Add("Organisationsnamn krävs när fakturan ska ställas till en organisation.");

        return errors;
    }

    /// <summary>
    /// Builds the overnight <see cref="Participant"/> graph (with meal preferences + snapshots) from
    /// a form assumed to have passed <see cref="Validate"/>. Applies the server-authoritative rules:
    /// no meals for self-catering participants; each meal choice must be an active option of the
    /// slot's meal type or it is dropped.
    ///
    /// <para>Participants carry no room assignment (FR-56 tombstoned) — the group's lodging is
    /// recorded per booking against buildings by the admin before approval (FR-103, §8.16), and the
    /// group divides itself among those buildings' rooms offline.</para>
    /// </summary>
    public static List<Participant> BuildParticipants(
        CompletionFormModel form,
        IReadOnlyList<MealSlot> slots,
        IReadOnlyDictionary<Guid, MealOption> activeOptionsById)
    {
        var participants = new List<Participant>(form.Overnight.Count);

        foreach (var entry in form.Overnight)
        {
            var participant = new Participant
            {
                ParticipantType = ParticipantType.Overnight,
                FullName = (entry.FullName ?? string.Empty).Trim(),
                AgeCategory = entry.AgeCategory,
                IsFullBoard = entry.IsFullBoard,
                AttendsBreakfast = true, // overnight guests always — no selection needed (§8.5)
                DietaryRequirements = NormalizeDietary(entry.Dietary ?? []),
                DietaryNotes = string.IsNullOrWhiteSpace(entry.DietaryNotes) ? null : entry.DietaryNotes.Trim(),
            };

            if (entry.IsFullBoard)
            {
                var choices = entry.MealChoices ?? [];
                for (var j = 0; j < slots.Count; j++)
                {
                    var choice = j < choices.Count ? choices[j] : null;
                    if (!Guid.TryParse(choice, out var optionId)) continue; // unselected / unparseable
                    if (!activeOptionsById.TryGetValue(optionId, out var option)) continue;
                    if (option.MealType != slots[j].MealType) continue; // wrong-type option → drop

                    participant.MealPreferences.Add(new MealPreference
                    {
                        Date = slots[j].Date,
                        MealType = slots[j].MealType,
                        MealOptionId = option.Id,
                        MealOptionSnapshot = option.Name,
                    });
                }
            }

            participants.Add(participant);
        }

        return participants;
    }

    /// <summary>
    /// Validates the day guests (§4.2, FR-58): each non-empty row needs a name and an attendance
    /// date within the stay; a fully-blank row (an accidental "add") is ignored. Returns Swedish
    /// errors (empty ⇒ valid). Callers only invoke this when day guests are applicable (the meal
    /// plan is FullBoardAll or Mixed).
    /// </summary>
    public static List<string> ValidateDayGuests(
        IReadOnlyList<DayGuestEntry> dayGuests, DateOnly checkIn, DateOnly checkOut)
    {
        var errors = new List<string>();
        for (var i = 0; i < dayGuests.Count; i++)
        {
            var entry = dayGuests[i];
            if (IsBlank(entry)) continue; // ignore an empty row rather than block submission

            var label = $"Dagsgäst {i + 1}";
            if (string.IsNullOrWhiteSpace(entry.FullName))
                errors.Add($"{label}: namn krävs.");
            else if (entry.FullName.Trim().Length > 200)
                errors.Add($"{label}: namnet får vara högst 200 tecken.");

            if (!TryParseDate(entry.AttendanceDate, out var date))
                errors.Add($"{label}: välj vilken dag gästen deltar.");
            else if (date < checkIn || date > checkOut)
                errors.Add($"{label}: dagen måste ligga inom vistelsen.");

            if ((entry.Dietary?.Contains(DietaryRequirement.Other) ?? false) && string.IsNullOrWhiteSpace(entry.DietaryNotes))
                errors.Add($"{label}: beskriv kostbehovet i fältet för 'Annat'.");

            if (!string.IsNullOrEmpty(entry.DietaryNotes) && entry.DietaryNotes.Length > 200)
                errors.Add($"{label}: kostanteckningen får vara högst 200 tecken.");
        }
        return errors;
    }

    /// <summary>
    /// Builds the day-guest <see cref="Participant"/> records (§4.2, §8.5). Each has a single
    /// <see cref="Participant.AttendanceDate"/>, the breakfast flag, and up to a lunch + a dinner
    /// meal preference for that date (dropped if the chosen option is inactive or the wrong type).
    /// Day guests are never full-board and never get a room. Blank rows are skipped.
    /// </summary>
    public static List<Participant> BuildDayGuests(
        IReadOnlyList<DayGuestEntry> dayGuests,
        IReadOnlyDictionary<Guid, MealOption> activeOptionsById,
        DateOnly checkIn,
        DateOnly checkOut)
    {
        var participants = new List<Participant>();

        foreach (var entry in dayGuests)
        {
            if (IsBlank(entry) || !TryParseDate(entry.AttendanceDate, out var date)) continue;

            // Constrain to the meals actually served on the attended day (§4.1): e.g. no lunch/dinner
            // on the departure day, no breakfast/lunch on the arrival day. Server-authoritative — a
            // choice for a meal not served that day is dropped regardless of what was posted.
            var served = MealsByDayCalculator.ServedOn(date, checkIn, checkOut);

            var participant = new Participant
            {
                ParticipantType = ParticipantType.DayGuest,
                FullName = (entry.FullName ?? string.Empty).Trim(),
                AgeCategory = entry.AgeCategory,
                AttendanceDate = date,
                AttendsBreakfast = entry.AttendsBreakfast && served.Breakfast,
                IsFullBoard = false, // day guests are meals-only, not full-board accommodation
                DietaryRequirements = NormalizeDietary(entry.Dietary ?? []),
                DietaryNotes = string.IsNullOrWhiteSpace(entry.DietaryNotes) ? null : entry.DietaryNotes.Trim(),
            };

            if (served.Lunch)
                AddDayGuestMeal(participant, entry.LunchChoice, MealType.Lunch, date, activeOptionsById);
            if (served.Dinner)
                AddDayGuestMeal(participant, entry.DinnerChoice, MealType.Dinner, date, activeOptionsById);

            participants.Add(participant);
        }

        return participants;
    }

    private static void AddDayGuestMeal(
        Participant participant, string? choice, MealType mealType, DateOnly date,
        IReadOnlyDictionary<Guid, MealOption> activeOptionsById)
    {
        if (!Guid.TryParse(choice, out var optionId)) return;
        if (!activeOptionsById.TryGetValue(optionId, out var option)) return;
        if (option.MealType != mealType) return;

        participant.MealPreferences.Add(new MealPreference
        {
            Date = date,
            MealType = mealType,
            MealOptionId = option.Id,
            MealOptionSnapshot = option.Name,
        });
    }

    /// <summary>A day-guest row with neither a name nor a date is treated as not filled in.</summary>
    private static bool IsBlank(DayGuestEntry entry) =>
        string.IsNullOrWhiteSpace(entry.FullName) && string.IsNullOrWhiteSpace(entry.AttendanceDate);

    private static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, out date);

    /// <summary>True if any applicable slot has no meal option chosen (used for the full-board rule).
    /// A null <see cref="OvernightParticipantEntry.MealChoices"/> (nothing posted) counts as all missing.</summary>
    private static bool SlotsMissingChoice(OvernightParticipantEntry entry, IReadOnlyList<MealSlot> slots)
    {
        var choices = entry.MealChoices ?? [];
        for (var j = 0; j < slots.Count; j++)
        {
            var choice = j < choices.Count ? choices[j] : null;
            if (string.IsNullOrEmpty(choice)) return true;
        }
        return false;
    }

    /// <summary>Dedupes dietary requirements and drops the meaningless <see cref="DietaryRequirement.None"/>
    /// when any real requirement is present.</summary>
    private static List<DietaryRequirement> NormalizeDietary(IEnumerable<DietaryRequirement> dietary)
    {
        var set = dietary.Distinct().ToList();
        if (set.Count > 1)
            set.Remove(DietaryRequirement.None);
        return set;
    }
}
