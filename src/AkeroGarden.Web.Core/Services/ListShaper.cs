using System.Globalization;
using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Shapes a booking's participants + the group's meal selections into the two admin download lists
/// (requirements.md FR-44/45), built entirely off the <b>snapshot</b> fields
/// (<see cref="BookingMealSelection.MealOptionSnapshot"/>) so a downloaded list reflects exactly
/// what was agreed even if reference data changed since (NFR-15/16). Pure and unit-testable; the
/// PDF/Excel generators render the returned rows.
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

    /// <summary>Caption for the dietary-preference counts (§4.6). Says the same thing as
    /// <see cref="SpecialDietNote"/> for a different reason: these are portions of the group's own
    /// dish prepared differently, not extra plates. The group is deliberately not named — the whole
    /// point of storing a count is that nobody needs to know which individuals they are.</summary>
    public const string PreferenceNote =
        "Antalet ingår i tillagningsantalen ovan — så många portioner ska tillagas vegetariskt respektive veganskt, inte utöver.";

    /// <summary>
    /// The statuses a list exists for (FR-44/45) — the single definition of "a booking with lists".
    /// It is also the exact set of bookings <see cref="Data.AdminRoles.Coordinator"/> may see
    /// (FR-104), which is why it is an array and not just the pattern below: the restricted list
    /// query filters on it in SQL. Widening this widens that role's reach — check both.
    /// </summary>
    public static readonly BookingStatus[] AvailableStatuses =
        [BookingStatus.DetailsComplete, BookingStatus.Active, BookingStatus.Completed];

    /// <summary>Lists are downloadable once the guest has submitted their details and the booking is
    /// still live or finished (FR-44/45) — DetailsComplete, Active, or Completed.</summary>
    public static bool IsAvailableFor(BookingStatus status) =>
        AvailableStatuses.Contains(status);

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
            Dietary: BookingDisplay.AllergySummary(p.Allergies, p.AllergyNotes))
        ).ToList();

        return new BookingListData(
            BookingReference: booking.BookingReference,
            ContactName: booking.ContactName,
            CheckIn: booking.CheckIn,
            CheckOut: booking.CheckOut,
            Participants: participantRows,
            Prep: BuildPrep(booking),
            SpecialDiets: BuildSpecialDiets(ordered),
            VegetarianCount: booking.VegetarianCount,
            VeganCount: booking.VeganCount);
    }

    /// <summary>The kitchen prep summary (FR-45): one row per (day, meal, option) with a headcount —
    /// "Tis 21/7 · Lunch · Köttbullar · 78" — plus a per-day breakfast headcount. Counts include
    /// everyone (the special-diets list is the cross-reference for which portions need adapting).
    ///
    /// <para>The group eats one dish per sitting (FR-105), so each meal yields exactly one row: the
    /// dish, and how many are sitting down to it. That headcount is the point — the choice moved to
    /// booking level, but the kitchen still needs the number, which is now computed from who is
    /// present rather than counted off individual selections.</para></summary>
    private static IReadOnlyList<PrepRow> BuildPrep(Booking booking)
    {
        var rows = new List<PrepRow>();
        if (booking.CheckIn is not { } checkIn || booking.CheckOut is not { } checkOut || checkOut <= checkIn)
            return rows;

        var occasions = MealOccasionResolver.Resolve(booking);
        var chosen = booking.MealSelections.ToDictionary(
            m => new MealSlot(m.Date, m.MealType),
            m => string.IsNullOrWhiteSpace(m.MealOptionSnapshot) ? "—" : m.MealOptionSnapshot);

        for (var date = checkIn; date <= checkOut; date = date.AddDays(1))
        {
            var served = occasions.ServedOn(date);
            if (!served.Any)
                continue;

            var dayGuestMeals = occasions.DayGuestMealsOn(date);
            var dayLabel = MealDateHeader(date);

            // Breakfast is a buffet (no option) — just a headcount of who eats it that day.
            if (served.Breakfast)
            {
                var breakfastCount = CountEating(booking, date, dayGuestMeals.Breakfast);
                if (breakfastCount > 0)
                    rows.Add(new PrepRow(dayLabel, "Frukost", "Frukostbuffé", breakfastCount));
            }

            // Lunch then dinner: the group's one dish, and how many eat it.
            foreach (var (mealType, label, isServed, dayGuestsEat) in new[]
            {
                (MealType.Lunch, "Lunch", served.Lunch, dayGuestMeals.Lunch),
                (MealType.Dinner, "Middag", served.Dinner, dayGuestMeals.Dinner),
            })
            {
                if (!isServed)
                    continue;

                var count = CountEating(booking, date, dayGuestsEat);
                if (count == 0)
                    continue;

                // A sitting the group takes but has not chosen a dish for yet (the admin can add or
                // drop an edge-day meal after submission, FR-48) still needs its headcount shown —
                // an absent row would read as "nobody eats", which is a different and wrong claim.
                var option = chosen.TryGetValue(new MealSlot(date, mealType), out var name) ? name : "Ej vald";
                rows.Add(new PrepRow(dayLabel, label, option, count));
            }
        }

        return rows;
    }

    /// <summary>How many people eat a given meal on <paramref name="date"/>: every overnight guest
    /// present, plus the day guests attending that date when day guests eat that sitting (§4.2).</summary>
    private static int CountEating(Booking booking, DateOnly date, bool dayGuestsEat) =>
        booking.Participants.Count(p => p.ParticipantType == ParticipantType.Overnight)
        + (dayGuestsEat
            ? booking.Participants.Count(p =>
                p.ParticipantType == ParticipantType.DayGuest && p.AttendanceDate == date)
            : 0);

    /// <summary>The allergy list (FR-45): only participants with an allergy or a note, named, with
    /// the days they are present, so the kitchen knows who needs an adapted plate and when.
    ///
    /// <para>The per-person meal column is gone: everybody eats the same dish at each sitting
    /// (FR-105), so it would repeat the prep summary on every row. What the kitchen needs from this
    /// list is <i>who</i> and <i>which days</i>.</para></summary>
    private static IReadOnlyList<SpecialDietRow> BuildSpecialDiets(IReadOnlyList<Participant> ordered) =>
        ordered
            .Where(HasAllergy)
            .Select(p => new SpecialDietRow(
                Name: p.FullName,
                Type: BookingDisplay.ParticipantTypeLabel(p.ParticipantType),
                Dietary: AllergyListSummary(p),
                Attendance: p.ParticipantType == ParticipantType.DayGuest && p.AttendanceDate is { } d
                    ? MealDateHeader(d)
                    : "Hela vistelsen"))
            .ToList();

    private static bool HasAllergy(Participant p) =>
        (p.Allergies?.Any(a => a != Allergy.None) ?? false)
        || !string.IsNullOrWhiteSpace(p.AllergyNotes);

    /// <summary>The allergy summary for the kitchen list. <see cref="BookingDisplay.AllergySummary"/>
    /// only folds the free-text note into the "Annat" entry; here — a safety list — any note is
    /// surfaced even when it stands alone or accompanies a different allergy.</summary>
    private static string AllergyListSummary(Participant p)
    {
        var summary = BookingDisplay.AllergySummary(p.Allergies, p.AllergyNotes);
        var note = p.AllergyNotes?.Trim();
        if (string.IsNullOrEmpty(note) || summary.Contains(note, StringComparison.Ordinal))
            return summary;
        return summary == "—" ? note : $"{summary} · {note}";
    }

    /// <summary>Compact day label, e.g. "Mån 1/6".</summary>
    public static string MealDateHeader(DateOnly date)
    {
        var label = date.ToString("ddd d/M", Sv);
        return label.Length == 0 ? label : char.ToUpper(label[0], Sv) + label[1..];
    }
}

/// <summary>A row in the participant list (FR-44) — headcount/attendance/allergy oriented.</summary>
public record ParticipantRow(string Name, string Type, string Age, string Attendance, string Dietary);

/// <summary>A row in the Matlista kitchen prep summary (FR-45): a dish to prepare and how many —
/// <c>(Day, Meal, Option, Count)</c>. No names (see <see cref="SpecialDietRow"/> for those).</summary>
public record PrepRow(string Day, string Meal, string Option, int Count);

/// <summary>A named participant with an allergy (FR-45): their allergy/notes summary and when they
/// are present, so the kitchen knows who needs an adapted plate and on which days.</summary>
public record SpecialDietRow(string Name, string Type, string Dietary, string Attendance);

/// <summary>The shaped data behind both downloadable lists for one booking.</summary>
public record BookingListData(
    string BookingReference,
    string ContactName,
    DateOnly? CheckIn,
    DateOnly? CheckOut,
    IReadOnlyList<ParticipantRow> Participants,
    IReadOnlyList<PrepRow> Prep,
    IReadOnlyList<SpecialDietRow> SpecialDiets,
    int VegetarianCount,
    int VeganCount);
