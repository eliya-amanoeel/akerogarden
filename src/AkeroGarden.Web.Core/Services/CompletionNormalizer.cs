using System.Globalization;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Pure, server-authoritative validation + construction of what the completion form submits
/// (NFR-26, §6.6): the group's meal selections, the participant roster, and the booking-level
/// dietary-preference counts. Mirrors <see cref="InquiryNormalizer"/> — the browser shows and hides
/// controls, but these rules re-apply on the server regardless of what the client posts. A meal
/// option of the wrong type, or one that is not currently active, is dropped; meals are accepted
/// only for the sittings <see cref="MealOccasionResolver"/> resolved for the booking; a
/// self-catering booking gets no meals at all. Snapshots
/// (<see cref="BookingMealSelection.MealOptionSnapshot"/>) are captured here at build time (NFR-15).
///
/// <para><b>Meals are chosen once for the whole group (FR-105)</b>, not per person. Participants
/// carry only name, type, age and allergies (§8.5); dietary preferences are counts on the booking
/// (§4.6); and participants carry no room assignment (FR-56 tombstoned) — the group's lodging is
/// recorded per booking against buildings by the admin before approval (FR-103, §8.16).</para>
/// </summary>
public static class CompletionNormalizer
{
    /// <summary>
    /// Validates the submitted form against the booking's expected overnight count and the resolved
    /// meal occasions. Returns Swedish error messages (empty ⇒ valid). Kept separate from the
    /// builders so the page can re-render errors without persisting.
    /// </summary>
    public static List<string> Validate(
        CompletionFormModel form,
        ResolvedMealOccasions occasions,
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

            errors.AddRange(ValidateAllergies(entry, label));
        }

        // Every sitting the group said it would take needs a dish chosen for it (§4.1). The posted
        // grid covers every possible sitting; only the selectable ones are required.
        var choices = form.MealChoices ?? [];
        for (var j = 0; j < occasions.AllSittings.Count; j++)
        {
            if (!occasions.IsSelectable(j)) continue;
            var choice = j < choices.Count ? choices[j] : null;
            if (string.IsNullOrEmpty(choice))
            {
                errors.Add("Välj måltid för alla dagar.");
                break;
            }
        }

        // Only meaningful when the booking has meals at all — a self-catering booking has no
        // portions to adapt, and its counts are zeroed on save whatever the form posted.
        if (occasions.AllSittings.Count > 0)
            errors.AddRange(ValidatePreferenceCounts(form, expectedOvernightCount));

        if (form.IsOrganisation && string.IsNullOrWhiteSpace(form.OrgName))
            errors.Add("Organisationsnamn krävs när fakturan ska ställas till en organisation.");

        return errors;
    }

    /// <summary>
    /// The dietary-preference counts are portions, so they cannot exceed the people present (§4.6),
    /// and they cannot be negative. Day guests count too — they eat what the group eats — so the
    /// ceiling is the overnight count plus the non-blank day-guest rows.
    /// </summary>
    private static IEnumerable<string> ValidatePreferenceCounts(CompletionFormModel form, int expectedOvernightCount)
    {
        var dayGuestCount = form.DayGuests.Count(g => !IsBlank(g));
        var ceiling = expectedOvernightCount + dayGuestCount;

        if (form.VegetarianCount < 0 || form.VeganCount < 0)
            yield return "Antalet specialkostportioner kan inte vara negativt.";
        else if (form.VegetarianCount + form.VeganCount > ceiling)
            yield return $"Antalet vegetariska och veganska portioner kan inte överstiga antalet gäster ({ceiling}).";
    }

    /// <summary>
    /// Builds the overnight <see cref="Participant"/> roster from a form assumed to have passed
    /// <see cref="Validate"/>. No meals and no room here — both are booking-level facts now.
    /// </summary>
    public static List<Participant> BuildParticipants(CompletionFormModel form)
    {
        var participants = new List<Participant>(form.Overnight.Count);

        foreach (var entry in form.Overnight)
        {
            participants.Add(new Participant
            {
                ParticipantType = ParticipantType.Overnight,
                FullName = (entry.FullName ?? string.Empty).Trim(),
                AgeCategory = entry.AgeCategory,
                Allergies = NormalizeAllergies(entry.Allergies ?? []),
                AllergyNotes = string.IsNullOrWhiteSpace(entry.AllergyNotes) ? null : entry.AllergyNotes.Trim(),
            });
        }

        return participants;
    }

    /// <summary>
    /// Builds the group's <see cref="BookingMealSelection"/> rows (§8.6, FR-105) — one per resolved
    /// sitting, with the option-name snapshot (NFR-15). Reads the posted grid by
    /// <see cref="ResolvedMealOccasions.AllSittings"/> index and keeps only the sittings the group
    /// actually takes, so a choice left over for a meal the group dropped is discarded rather than
    /// silently reassigned to a different day. A choice that does not parse, is not an active
    /// option, or is of the wrong meal type is dropped; a self-catering booking resolves to no
    /// sittings at all and therefore yields nothing.
    /// </summary>
    public static List<BookingMealSelection> BuildMealSelections(
        CompletionFormModel form,
        ResolvedMealOccasions occasions,
        IReadOnlyDictionary<Guid, MealOption> activeOptionsById)
    {
        var selections = new List<BookingMealSelection>();
        var choices = form.MealChoices ?? [];

        for (var j = 0; j < occasions.AllSittings.Count; j++)
        {
            if (!occasions.IsSelectable(j)) continue;                        // meal not taken → drop
            var choice = j < choices.Count ? choices[j] : null;
            if (!Guid.TryParse(choice, out var optionId)) continue;          // unselected / unparseable
            if (!activeOptionsById.TryGetValue(optionId, out var option)) continue;
            if (option.MealType != occasions.AllSittings[j].MealType) continue; // wrong-type → drop

            selections.Add(new BookingMealSelection
            {
                Date = occasions.AllSittings[j].Date,
                MealType = occasions.AllSittings[j].MealType,
                MealOptionId = option.Id,
                MealOptionSnapshot = option.Name,
            });
        }

        return selections;
    }

    /// <summary>
    /// Validates the day guests (§4.2, FR-58): each non-empty row needs a name and an attendance
    /// date within the stay; a fully-blank row (an accidental "add") is ignored. Returns Swedish
    /// errors (empty ⇒ valid). Callers only invoke this when day guests are applicable — see
    /// <see cref="InquiryNormalizer.DayGuestsApplicable"/>.
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

            errors.AddRange(ValidateAllergies(entry, label));
        }
        return errors;
    }

    /// <summary>
    /// Builds the day-guest <see cref="Participant"/> records (§4.2, §8.5). Each has a single
    /// <see cref="Participant.AttendanceDate"/> and their allergies — nothing else. Which meals they
    /// eat is one setting for every day guest on the booking, and what they eat is the group's
    /// choice, so neither is stored per person. Blank rows are skipped.
    /// </summary>
    public static List<Participant> BuildDayGuests(IReadOnlyList<DayGuestEntry> dayGuests)
    {
        var participants = new List<Participant>();

        foreach (var entry in dayGuests)
        {
            if (IsBlank(entry) || !TryParseDate(entry.AttendanceDate, out var date)) continue;

            participants.Add(new Participant
            {
                ParticipantType = ParticipantType.DayGuest,
                FullName = (entry.FullName ?? string.Empty).Trim(),
                AgeCategory = entry.AgeCategory,
                AttendanceDate = date,
                Allergies = NormalizeAllergies(entry.Allergies ?? []),
                AllergyNotes = string.IsNullOrWhiteSpace(entry.AllergyNotes) ? null : entry.AllergyNotes.Trim(),
            });
        }

        return participants;
    }

    /// <summary>The allergy rules shared by both participant kinds: "Annat" needs its free text, and
    /// the note is length-bounded. An unchecked checkbox group binds to null on SSR post — treated
    /// as no allergies.</summary>
    private static IEnumerable<string> ValidateAllergies(IAllergyEntry entry, string label)
    {
        if (entry.Allergies?.Contains(Allergy.Other) == true && string.IsNullOrWhiteSpace(entry.AllergyNotes))
            yield return $"{label}: beskriv allergin i fältet för 'Annat'.";

        if (!string.IsNullOrEmpty(entry.AllergyNotes) && entry.AllergyNotes.Length > 200)
            yield return $"{label}: allergianteckningen får vara högst 200 tecken.";
    }

    /// <summary>A day-guest row with neither a name nor a date is treated as not filled in.</summary>
    private static bool IsBlank(DayGuestEntry entry) =>
        string.IsNullOrWhiteSpace(entry.FullName) && string.IsNullOrWhiteSpace(entry.AttendanceDate);

    private static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParse(value, CultureInfo.InvariantCulture, out date);

    /// <summary>Dedupes allergies and drops the meaningless <see cref="Allergy.None"/> when any real
    /// allergy is present.</summary>
    private static List<Allergy> NormalizeAllergies(IEnumerable<Allergy> allergies)
    {
        var set = allergies.Distinct().ToList();
        if (set.Count > 1)
            set.Remove(Allergy.None);
        return set;
    }
}
