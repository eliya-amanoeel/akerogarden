using AkeroGarden.Web.Data.Entities;

namespace AkeroGarden.Web.Services;

/// <summary>
/// One meal that must be collected on a given day of a stay — a (date, lunch|dinner) pair.
/// </summary>
public readonly record struct MealSlot(DateOnly Date, MealType MealType);

/// <summary>
/// The meals-by-day-of-stay rule of requirements.md §4.1, based on the standard check-in 14:00 /
/// check-out 12:00 schedule (§2.8). Pure and unit-testable (NFR-26) — no database, no clock.
///
/// <list type="bullet">
///   <item>Arrival day: dinner only (guests arrive after lunch).</item>
///   <item>Middle days (full days): lunch and dinner (breakfast is a buffet, no selection).</item>
///   <item>Departure day: none (guests leave before lunch; breakfast is a buffet).</item>
/// </list>
///
/// So a one-night stay (arrival + departure) collects a single dinner. This is the set of meals a
/// full-board overnight participant selects on the completion form (FR-57); self-catering
/// participants and day guests are handled separately (§4.2).
/// </summary>
public static class MealsByDayCalculator
{
    /// <summary>
    /// The ordered meal slots for a full-board overnight stay from <paramref name="checkIn"/>
    /// (inclusive) to <paramref name="checkOut"/> (the departure day, no meals). Returns empty if
    /// the range is non-positive (<paramref name="checkOut"/> at or before <paramref name="checkIn"/>).
    /// Ordered chronologically, and lunch before dinner within a day.
    /// </summary>
    public static IReadOnlyList<MealSlot> Slots(DateOnly checkIn, DateOnly checkOut)
    {
        var slots = new List<MealSlot>();
        if (checkOut <= checkIn)
            return slots;

        // Arrival day: dinner only.
        slots.Add(new MealSlot(checkIn, MealType.Dinner));

        // Middle days (everything between arrival and the departure day): lunch + dinner.
        for (var day = checkIn.AddDays(1); day < checkOut; day = day.AddDays(1))
        {
            slots.Add(new MealSlot(day, MealType.Lunch));
            slots.Add(new MealSlot(day, MealType.Dinner));
        }

        // Departure day (checkOut): no meals collected.
        return slots;
    }
}
