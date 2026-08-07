using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// One meal that must be chosen for a given day of a stay — a (date, lunch|dinner) pair. Breakfast
/// is a buffet and never appears here, because there is nothing to choose.
/// </summary>
public readonly record struct MealSlot(DateOnly Date, MealType MealType);

/// <summary>Which meals the kitchen serves on a given day of a stay (§4.1).</summary>
public readonly record struct MealsServed(bool Breakfast, bool Lunch, bool Dinner)
{
    /// <summary>Nothing served — an out-of-stay date, or a self-catering booking.</summary>
    public static readonly MealsServed None = new(false, false, false);

    /// <summary>True if at least one meal is served.</summary>
    public bool Any => Breakfast || Lunch || Dinner;

    /// <summary>The meals served by both — used to constrain day-guest attendance to what the
    /// group actually takes that day.</summary>
    public MealsServed Intersect(MealsServed other) =>
        new(Breakfast && other.Breakfast, Lunch && other.Lunch, Dinner && other.Dinner);
}

/// <summary>
/// The inputs the meal occasions of a booking are resolved from (§4.1, §4.2). A plain record rather
/// than the <see cref="Booking"/> entity, because the completion form must resolve occasions from
/// in-progress form state — the guest ticks "arrival lunch" and that day's picker has to appear
/// before anything is saved. <see cref="FromBooking"/> is the stored-booking case.
/// </summary>
public readonly record struct MealOccasionInput(
    DateOnly CheckIn,
    DateOnly CheckOut,
    MealPlan MealPlan,
    bool ArrivalBreakfast,
    bool ArrivalLunch,
    bool ArrivalDinner,
    bool DepartureBreakfast,
    bool DepartureLunch,
    bool DepartureDinner,
    bool DayGuestBreakfast,
    bool DayGuestLunch,
    bool DayGuestDinner)
{
    /// <summary>Reads the inputs off a stored stay request. Returns null for a booking with no
    /// dates or no meal plan (a general inquiry), which has no meal occasions at all.</summary>
    public static MealOccasionInput? FromBooking(Booking booking)
    {
        if (booking.CheckIn is not { } checkIn || booking.CheckOut is not { } checkOut ||
            booking.MealPlan is not { } plan)
            return null;

        return new MealOccasionInput(
            checkIn, checkOut, plan,
            booking.ArrivalBreakfast, booking.ArrivalLunch, booking.ArrivalDinner,
            booking.DepartureBreakfast, booking.DepartureLunch, booking.DepartureDinner,
            booking.DayGuestBreakfast, booking.DayGuestLunch, booking.DayGuestDinner);
    }
}

/// <summary>
/// The single resolved answer to "which meals occur on each day of this stay, and which of them
/// need a choice". Every consumer — the completion form, <see cref="ListShaper"/>, the prep counts,
/// the reminder email — reads this and never re-derives it from the dates.
/// </summary>
public sealed class ResolvedMealOccasions
{
    private readonly Dictionary<DateOnly, MealsServed> _served;
    private readonly MealsServed _dayGuestFlags;

    internal ResolvedMealOccasions(
        Dictionary<DateOnly, MealsServed> served,
        MealsServed dayGuestFlags,
        IReadOnlyList<MealSlot> allSittings,
        IReadOnlyList<MealSlot> selectionSlots)
    {
        _served = served;
        _dayGuestFlags = dayGuestFlags;
        AllSittings = allSittings;
        SelectionSlots = selectionSlots;
    }

    /// <summary>An empty resolution — no dates, no meals. Used for general inquiries.</summary>
    public static readonly ResolvedMealOccasions Empty =
        new([], MealsServed.None, [], []);

    /// <summary>
    /// Every lunch and dinner the stay <i>could</i> have — all days, both sittings — regardless of
    /// what the group chose for its edge days. <b>This, not <see cref="SelectionSlots"/>, is what
    /// the completion form's meal grid is indexed by.</b>
    ///
    /// <para>The reason is Static SSR: ticking "lunch on the arrival day" cannot re-render the
    /// slot list without a round-trip, so the form posts the full grid and the server keeps the
    /// subset that <see cref="SelectionSlots"/> resolves to. Indexing the posted values by the
    /// resolved subset instead would shift every choice by one the moment an edge-day box changed.
    /// Empty for a self-catering booking.</para>
    /// </summary>
    public IReadOnlyList<MealSlot> AllSittings { get; }

    /// <summary>The sittings needing a group meal choice, chronologically and lunch before dinner
    /// within a day — a subset of <see cref="AllSittings"/>. Empty for a self-catering booking.</summary>
    public IReadOnlyList<MealSlot> SelectionSlots { get; }

    /// <summary>Is the sitting at <paramref name="sittingIndex"/> of <see cref="AllSittings"/> one
    /// the group actually takes, and therefore one that needs a dish chosen?</summary>
    public bool IsSelectable(int sittingIndex) =>
        sittingIndex >= 0 && sittingIndex < AllSittings.Count
        && ServedOn(AllSittings[sittingIndex].Date) is var served
        && (AllSittings[sittingIndex].MealType == MealType.Lunch ? served.Lunch : served.Dinner);

    /// <summary>Which meals the kitchen serves on <paramref name="date"/>. Nothing outside the
    /// stay, and nothing at all for a self-catering booking.</summary>
    public MealsServed ServedOn(DateOnly date) =>
        _served.TryGetValue(date, out var served) ? served : MealsServed.None;

    /// <summary>Which meals a day guest attending <paramref name="date"/> eats — the booking's
    /// day-guest attendance setting (§4.2) <b>intersected</b> with what the group actually takes
    /// that day. A day guest cannot be counted for a meal nobody is cooking.</summary>
    public MealsServed DayGuestMealsOn(DateOnly date) => ServedOn(date).Intersect(_dayGuestFlags);
}

/// <summary>
/// Resolves the meal occasions of a booking (requirements.md §4.1, §4.2) into one answer every
/// consumer shares. Pure and unit-testable (NFR-26) — no database, no clock.
///
/// <para><b>Policy is data with a computed default, not a rule baked into a function.</b> This
/// replaced <c>MealsByDayCalculator</c>, which derived the edge days from the fixed 14:00/12:00
/// schedule: arrival day dinner only, departure day breakfast only. That welded a business policy
/// to a computation, so the policy could not vary per booking — which is exactly why letting guests
/// state their own times (§2.8) was disruptive rather than additive.</para>
///
/// <para>The rule now: <b>the arrival and departure day are stored choices</b> (the group takes any
/// combination, including all three meals); <b>the middle days are derived</b> (always breakfast,
/// lunch and dinner). The standard schedule survives only in
/// <see cref="StandardEdgeDefaults"/>, which pre-fills those stored flags at creation. It is never
/// consulted when reading an existing booking — doing so is the two-sources-of-truth failure this
/// design exists to prevent, and it is why the defaults live on this same type rather than in a
/// separate helper a future reader could mistake for the answer.</para>
/// </summary>
public static class MealOccasionResolver
{
    /// <summary>Standard check-in time (§2.8) — the default for <see cref="Booking.CheckInTime"/>
    /// and the baseline the admin non-standard-time warning compares against (FR-101).</summary>
    public static readonly TimeOnly StandardCheckInTime = new(14, 0);

    /// <summary>Standard check-out time (§2.8). See <see cref="StandardCheckInTime"/>.</summary>
    public static readonly TimeOnly StandardCheckOutTime = new(12, 0);

    /// <summary>
    /// The edge-day meals implied by the standard 14:00/12:00 schedule — arrival day dinner only,
    /// departure day breakfast only. <b>A pre-fill for the stored flags, never an answer.</b>
    /// Applied when a booking is created so it always has a coherent meal state before the guest
    /// reaches the completion form; from then on the stored flags are the truth, even when they
    /// contradict this.
    /// </summary>
    public static (MealsServed Arrival, MealsServed Departure) StandardEdgeDefaults() =>
        (new MealsServed(Breakfast: false, Lunch: false, Dinner: true),
         new MealsServed(Breakfast: true, Lunch: false, Dinner: false));

    /// <summary>Applies <see cref="StandardEdgeDefaults"/> to a booking being created (FR-105).</summary>
    public static void ApplyStandardEdgeDefaults(Booking booking)
    {
        var (arrival, departure) = StandardEdgeDefaults();
        booking.ArrivalBreakfast = arrival.Breakfast;
        booking.ArrivalLunch = arrival.Lunch;
        booking.ArrivalDinner = arrival.Dinner;
        booking.DepartureBreakfast = departure.Breakfast;
        booking.DepartureLunch = departure.Lunch;
        booking.DepartureDinner = departure.Dinner;
    }

    /// <summary>Resolves a stored booking. A general inquiry, or a stay with no dates or no meal
    /// plan, resolves to <see cref="ResolvedMealOccasions.Empty"/>.</summary>
    public static ResolvedMealOccasions Resolve(Booking booking) =>
        MealOccasionInput.FromBooking(booking) is { } input
            ? Resolve(input)
            : ResolvedMealOccasions.Empty;

    /// <summary>
    /// Resolves the meal occasions for <paramref name="input"/>. Returns an empty resolution when
    /// the stay is non-positive (<c>CheckOut</c> at or before <c>CheckIn</c>) or the booking is
    /// self-catering — a self-catering booking has no catered meals at all, and centralising that
    /// here is why no consumer needs to re-check the meal plan.
    /// </summary>
    public static ResolvedMealOccasions Resolve(MealOccasionInput input)
    {
        if (input.CheckOut <= input.CheckIn || input.MealPlan == MealPlan.SelfCateringAll)
            return ResolvedMealOccasions.Empty;

        var served = new Dictionary<DateOnly, MealsServed>();
        var allSittings = new List<MealSlot>();
        var slots = new List<MealSlot>();

        // Arrival day: whatever the group chose (stored, §4.1).
        var arrival = new MealsServed(input.ArrivalBreakfast, input.ArrivalLunch, input.ArrivalDinner);
        served[input.CheckIn] = arrival;

        // Middle days: always all three — derived, not chosen.
        for (var day = input.CheckIn.AddDays(1); day < input.CheckOut; day = day.AddDays(1))
            served[day] = new MealsServed(Breakfast: true, Lunch: true, Dinner: true);

        // Departure day: whatever the group chose (stored, §4.1).
        served[input.CheckOut] =
            new MealsServed(input.DepartureBreakfast, input.DepartureLunch, input.DepartureDinner);

        // The full grid is every day × (lunch, dinner); the selection slots are the subset the group
        // actually takes. Breakfast is a buffet — served, but never selected, so it is in neither.
        for (var day = input.CheckIn; day <= input.CheckOut; day = day.AddDays(1))
        {
            allSittings.Add(new MealSlot(day, MealType.Lunch));
            allSittings.Add(new MealSlot(day, MealType.Dinner));
            if (served[day].Lunch) slots.Add(new MealSlot(day, MealType.Lunch));
            if (served[day].Dinner) slots.Add(new MealSlot(day, MealType.Dinner));
        }

        var dayGuestFlags =
            new MealsServed(input.DayGuestBreakfast, input.DayGuestLunch, input.DayGuestDinner);

        return new ResolvedMealOccasions(served, dayGuestFlags, allSittings, slots);
    }
}
