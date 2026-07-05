using System.Globalization;
using AkeroGarden.Web.Data.Entities;

namespace AkeroGarden.Web.Services;

/// <summary>
/// Shapes a booking's participants + meal preferences into the two admin download lists
/// (requirements.md FR-44/45), built entirely off the <b>snapshot</b> fields
/// (<see cref="Participant.RoomAssignmentSnapshot"/>, <see cref="MealPreference.MealOptionSnapshot"/>)
/// so a downloaded list reflects exactly what was agreed even if reference data changed since
/// (NFR-15/16). Pure and unit-testable; the PDF/Excel generators render the returned rows.
/// </summary>
public static class ListShaper
{
    private static readonly CultureInfo Sv = CultureInfo.GetCultureInfo("sv-SE");

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
            Room: string.IsNullOrWhiteSpace(p.RoomAssignmentSnapshot) ? "—" : p.RoomAssignmentSnapshot!,
            Attendance: p.ParticipantType == ParticipantType.DayGuest && p.AttendanceDate is { } d
                ? d.ToString("yyyy-MM-dd", Sv)
                : "—",
            Dietary: BookingDisplay.DietarySummary(p.DietaryRequirements, p.DietaryNotes))
        ).ToList();

        var foodRows = ordered.Select(p => new FoodRow(
            Name: p.FullName,
            Type: BookingDisplay.ParticipantTypeLabel(p.ParticipantType),
            FullBoard: p.IsFullBoard ? "Ja" : "Nej",
            Breakfast: p.AttendsBreakfast ? "Ja" : "Nej",
            Meals: FormatMeals(p.MealPreferences),
            Dietary: BookingDisplay.DietarySummary(p.DietaryRequirements, p.DietaryNotes))
        ).ToList();

        return new BookingListData(
            ContactName: booking.ContactName,
            CheckIn: booking.CheckIn,
            CheckOut: booking.CheckOut,
            Participants: participantRows,
            Food: foodRows);
    }

    private static string FormatMeals(IEnumerable<MealPreference> meals)
    {
        var lines = meals
            .OrderBy(m => m.Date)
            .ThenBy(m => m.MealType)
            .Select(m => $"{m.Date.ToString("yyyy-MM-dd", Sv)} {BookingDisplay.MealTypeLabel(m.MealType)}: {m.MealOptionSnapshot}")
            .ToList();
        return lines.Count == 0 ? "—" : string.Join("; ", lines);
    }
}

/// <summary>A row in the participant list (FR-44) — headcount/room/dietary oriented.</summary>
public record ParticipantRow(string Name, string Type, string Age, string Room, string Attendance, string Dietary);

/// <summary>A row in the food-preference list (FR-45) — catering/dietary oriented.</summary>
public record FoodRow(string Name, string Type, string FullBoard, string Breakfast, string Meals, string Dietary);

/// <summary>The shaped data behind both downloadable lists for one booking.</summary>
public record BookingListData(
    string ContactName,
    DateOnly? CheckIn,
    DateOnly? CheckOut,
    IReadOnlyList<ParticipantRow> Participants,
    IReadOnlyList<FoodRow> Food);
