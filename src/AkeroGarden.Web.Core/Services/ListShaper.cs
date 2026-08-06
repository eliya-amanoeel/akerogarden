using System.Globalization;
using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Shapes a booking's participants + meal preferences into the two admin download lists
/// (requirements.md FR-44/45), built entirely off the <b>snapshot</b> fields
/// (<see cref="MealPreference.MealOptionSnapshot"/>) so a downloaded list reflects exactly what was
/// agreed even if reference data changed since (NFR-15/16). Pure and unit-testable; the PDF/Excel
/// generators render the returned rows.
///
/// <para>The per-participant <b>Rum</b> column was removed 2026-08-06: the system no longer records
/// who sleeps in which room (FR-56 tombstoned). Lodging is a booking-level fact now — the assigned
/// buildings (§8.16, FR-103) — not a property of each row.</para>
///
/// <para>The <b>Deltagarlista</b> (FR-44) is the named roster (headcount/dietary). The
/// <b>Matlista</b> (FR-45) is redesigned from a per-person table into a <b>kitchen prep summary</b>
/// (post-demo, Phase 8): a caterer needs "how many of each dish per meal per day", not one row per
/// guest. So it carries no names for the bulk — just per-day, per-meal option counts
/// (<see cref="BookingListData.Prep"/>) — and names appear only for the few people who need special
/// handling (<see cref="BookingListData.SpecialDiets"/>). The full named roster lives in the
/// Deltagarlista, so nothing is lost and PII is not duplicated across documents.</para>
/// </summary>
public static class ListShaper
{
    private static readonly CultureInfo Sv = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>Caption for the special-diets section, spelling out that these portions are already
    /// counted in the tillagningsöversikt so the kitchen adapts them rather than cooking extra.
    /// Worded to read correctly both under the PDF's counts and on the Excel's separate sheet.</summary>
    public const string SpecialDietNote =
        "Obs: personerna nedan ingår redan i tillagningsantalen — deras portion ska anpassas, inte läggas till.";

    /// <summary>Lists are downloadable once the guest has submitted their details and the booking is
    /// still live or finished (FR-44/45) — DetailsComplete, Active, or Completed.</summary>
    public static bool IsAvailableFor(BookingStatus status) =>
        status is BookingStatus.DetailsComplete or BookingStatus.Active or BookingStatus.Completed;

    public static BookingListData Shape(Booking booking)
    {
        // Overnight first, then day guests; each group alphabetical for a readable roster.
        var ordered = booking.Participants
            .OrderBy(p => p.ParticipantType == ParticipantType.DayGuest) // Overnight (false) first
            .ThenBy(p => p.FullName, StringComparer.CurrentCulture)
            .ToList();

        var participantRows = ordered.Select(p => new ParticipantRow(
            Name: p.FullName,
            Type: BookingDisplay.ParticipantTypeLabel(p.ParticipantType),
            Age: BookingDisplay.AgeLabel(p.AgeCategory),
            Attendance: p.ParticipantType == ParticipantType.DayGuest && p.AttendanceDate is { } d
                ? d.ToString("yyyy-MM-dd", Sv)
                : "—",
            Dietary: BookingDisplay.DietarySummary(p.DietaryRequirements, p.DietaryNotes))
        ).ToList();

        return new BookingListData(
            BookingReference: booking.BookingReference,
            ContactName: booking.ContactName,
            CheckIn: booking.CheckIn,
            CheckOut: booking.CheckOut,
            Participants: participantRows,
            Prep: BuildPrep(booking),
            SpecialDiets: BuildSpecialDiets(ordered));
    }

    /// <summary>The kitchen prep summary (FR-45): one row per (day, meal, option) with a headcount —
    /// "Tis 21/7 · Lunch · Köttbullar · 78" — plus a per-day breakfast headcount. Counts include
    /// everyone (the special-diets list is the cross-reference for which portions need adapting).</summary>
    private static IReadOnlyList<PrepRow> BuildPrep(Booking booking)
    {
        var rows = new List<PrepRow>();
        if (booking.CheckIn is not { } checkIn || booking.CheckOut is not { } checkOut || checkOut <= checkIn)
            return rows;

        // Aggregate the chosen lunch/dinner options across all participants: (date, meal, option) → count.
        var mealCounts = booking.Participants
            .SelectMany(p => p.MealPreferences)
            .GroupBy(m => (m.Date, m.MealType, Option: m.MealOptionSnapshot ?? "—"))
            .ToDictionary(g => g.Key, g => g.Count());

        for (var date = checkIn; date <= checkOut; date = date.AddDays(1))
        {
            var served = MealsByDayCalculator.ServedOn(date, checkIn, checkOut);
            var dayLabel = MealDateHeader(date);

            // Breakfast is a buffet (no option) — just a headcount of who attends that day.
            if (served.Breakfast)
            {
                var breakfastCount = booking.Participants.Count(p => p.AttendsBreakfast && AttendsOn(p, date, checkIn, checkOut));
                if (breakfastCount > 0)
                    rows.Add(new PrepRow(dayLabel, "Frukost", "Frukostbuffé", breakfastCount));
            }

            // Lunch then dinner: one row per chosen option, most-ordered first.
            foreach (var (mealType, label, isServed) in new[]
            {
                (MealType.Lunch, "Lunch", served.Lunch),
                (MealType.Dinner, "Middag", served.Dinner),
            })
            {
                if (!isServed)
                    continue;

                var options = mealCounts
                    .Where(kv => kv.Key.Date == date && kv.Key.MealType == mealType)
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key.Option, StringComparer.CurrentCulture);
                foreach (var kv in options)
                    rows.Add(new PrepRow(dayLabel, label, kv.Key.Option, kv.Value));
            }
        }

        return rows;
    }

    /// <summary>The special-diets list (FR-45): only participants with a dietary requirement or note,
    /// named, with their per-day meals so the kitchen can prepare the right alternative.</summary>
    private static IReadOnlyList<SpecialDietRow> BuildSpecialDiets(IReadOnlyList<Participant> ordered) =>
        ordered
            .Where(HasSpecialDiet)
            .Select(p => new SpecialDietRow(
                Name: p.FullName,
                Type: BookingDisplay.ParticipantTypeLabel(p.ParticipantType),
                Dietary: SpecialDietSummary(p),
                Meals: FormatPersonMeals(p.MealPreferences)))
            .ToList();

    private static bool HasSpecialDiet(Participant p) =>
        (p.DietaryRequirements?.Any(d => d != DietaryRequirement.None) ?? false)
        || !string.IsNullOrWhiteSpace(p.DietaryNotes);

    /// <summary>The dietary summary for the kitchen list. <see cref="BookingDisplay.DietarySummary"/>
    /// only folds the free-text note into the "Annat" requirement; here — a safety list — any note is
    /// surfaced even when it stands alone or accompanies a different requirement.</summary>
    private static string SpecialDietSummary(Participant p)
    {
        var summary = BookingDisplay.DietarySummary(p.DietaryRequirements, p.DietaryNotes);
        var note = p.DietaryNotes?.Trim();
        if (string.IsNullOrEmpty(note) || summary.Contains(note, StringComparison.Ordinal))
            return summary;
        return summary == "—" ? note : $"{summary} · {note}";
    }

    /// <summary>Is this participant present for meals on <paramref name="date"/>? Overnight guests are
    /// present the whole stay; a day guest only on their single attendance date.</summary>
    private static bool AttendsOn(Participant p, DateOnly date, DateOnly checkIn, DateOnly checkOut) =>
        p.ParticipantType == ParticipantType.DayGuest
            ? p.AttendanceDate == date
            : date >= checkIn && date <= checkOut;

    /// <summary>One person's meals as compact per-day lines: "Tis 21/7: Lunch Köttbullar, Middag Lax".</summary>
    private static string FormatPersonMeals(IEnumerable<MealPreference> meals)
    {
        var lines = meals
            .GroupBy(m => m.Date)
            .OrderBy(g => g.Key)
            .Select(g => $"{MealDateHeader(g.Key)}: " + string.Join(", ", g
                .OrderBy(m => m.MealType)
                .Select(m => $"{BookingDisplay.MealTypeLabel(m.MealType)} {m.MealOptionSnapshot}")));

        var joined = string.Join("\n", lines);
        return joined.Length == 0 ? "—" : joined;
    }

    /// <summary>Compact day label, e.g. "Mån 1/6".</summary>
    public static string MealDateHeader(DateOnly date)
    {
        var label = date.ToString("ddd d/M", Sv);
        return label.Length == 0 ? label : char.ToUpper(label[0], Sv) + label[1..];
    }
}

/// <summary>A row in the participant list (FR-44) — headcount/room/dietary oriented.</summary>
public record ParticipantRow(string Name, string Type, string Age, string Attendance, string Dietary);

/// <summary>A row in the Matlista kitchen prep summary (FR-45): a dish to prepare and how many —
/// <c>(Day, Meal, Option, Count)</c>. No names (see <see cref="SpecialDietRow"/> for those).</summary>
public record PrepRow(string Day, string Meal, string Option, int Count);

/// <summary>A named participant with a special dietary need (FR-45): their requirement/notes summary
/// and their per-day meals, so the kitchen knows who needs an adapted plate and of what.</summary>
public record SpecialDietRow(string Name, string Type, string Dietary, string Meals);

/// <summary>The shaped data behind both downloadable lists for one booking.</summary>
public record BookingListData(
    string BookingReference,
    string ContactName,
    DateOnly? CheckIn,
    DateOnly? CheckOut,
    IReadOnlyList<ParticipantRow> Participants,
    IReadOnlyList<PrepRow> Prep,
    IReadOnlyList<SpecialDietRow> SpecialDiets);
