using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the resolved meal-occasion concept (requirements.md §4.1, §4.2, FR-105, and
/// the §7.1 design correction): the arrival and departure day are <b>stored choices</b>, the middle
/// days are derived, and every consumer reads one resolved answer. Pure logic, no database (NFR-26).
/// </summary>
public class MealOccasionResolverTests
{
    private static readonly DateOnly Day1 = new(2026, 8, 10);
    private static readonly DateOnly Day2 = new(2026, 8, 11);
    private static readonly DateOnly Day3 = new(2026, 8, 12);
    private static readonly DateOnly Day4 = new(2026, 8, 13);

    /// <summary>A booking on the standard schedule: arrival dinner only, departure breakfast only.</summary>
    private static MealOccasionInput Standard(
        DateOnly checkIn, DateOnly checkOut,
        MealPlan plan = MealPlan.FullBoardAll,
        bool arrivalBreakfast = false, bool arrivalLunch = false, bool arrivalDinner = true,
        bool departureBreakfast = true, bool departureLunch = false, bool departureDinner = false,
        bool dayGuestBreakfast = false, bool dayGuestLunch = false, bool dayGuestDinner = false) =>
        new(checkIn, checkOut, plan,
            arrivalBreakfast, arrivalLunch, arrivalDinner,
            departureBreakfast, departureLunch, departureDinner,
            dayGuestBreakfast, dayGuestLunch, dayGuestDinner);

    // ── Middle days are derived ─────────────────────────────────────────────────

    [Fact]
    public void MiddleDays_AlwaysServeAllThreeMeals()
    {
        var occasions = MealOccasionResolver.Resolve(Standard(Day1, Day4));

        Assert.Equal(new MealsServed(true, true, true), occasions.ServedOn(Day2));
        Assert.Equal(new MealsServed(true, true, true), occasions.ServedOn(Day3));
    }

    [Fact]
    public void MiddleDays_ContributeLunchAndDinnerSlots()
    {
        var occasions = MealOccasionResolver.Resolve(Standard(Day1, Day3));

        Assert.Contains(new MealSlot(Day2, MealType.Lunch), occasions.SelectionSlots);
        Assert.Contains(new MealSlot(Day2, MealType.Dinner), occasions.SelectionSlots);
    }

    // ── Edge days are stored choices, not derived ───────────────────────────────

    [Fact]
    public void StandardSchedule_ArrivalIsDinnerOnly_DepartureIsBreakfastOnly()
    {
        var occasions = MealOccasionResolver.Resolve(Standard(Day1, Day3));

        Assert.Equal(new MealsServed(false, false, true), occasions.ServedOn(Day1));
        Assert.Equal(new MealsServed(true, false, false), occasions.ServedOn(Day3));
    }

    [Fact]
    public void ArrivalDay_HonoursAllThreeMealsWhenChosen()
    {
        // A group arriving early may take breakfast, lunch and dinner on its arrival day (§4.1) —
        // the choice is never constrained by the stated check-in time.
        var occasions = MealOccasionResolver.Resolve(Standard(
            Day1, Day3, arrivalBreakfast: true, arrivalLunch: true, arrivalDinner: true));

        Assert.Equal(new MealsServed(true, true, true), occasions.ServedOn(Day1));
        Assert.Contains(new MealSlot(Day1, MealType.Lunch), occasions.SelectionSlots);
        Assert.Contains(new MealSlot(Day1, MealType.Dinner), occasions.SelectionSlots);
    }

    [Fact]
    public void DepartureDay_HonoursLunchWhenChosen()
    {
        var occasions = MealOccasionResolver.Resolve(Standard(Day1, Day3, departureLunch: true));

        Assert.Equal(new MealsServed(true, true, false), occasions.ServedOn(Day3));
        Assert.Contains(new MealSlot(Day3, MealType.Lunch), occasions.SelectionSlots);
    }

    [Fact]
    public void EdgeDays_TakingNoMealsYieldsNoSlotsForThoseDays()
    {
        var occasions = MealOccasionResolver.Resolve(Standard(
            Day1, Day3,
            arrivalDinner: false, departureBreakfast: false));

        Assert.Equal(MealsServed.None, occasions.ServedOn(Day1));
        Assert.Equal(MealsServed.None, occasions.ServedOn(Day3));
        Assert.DoesNotContain(occasions.SelectionSlots, s => s.Date == Day1 || s.Date == Day3);
    }

    [Fact]
    public void OneNightStay_OnStandardSchedule_CollectsExactlyOneDinner()
    {
        var occasions = MealOccasionResolver.Resolve(Standard(Day1, Day2));

        Assert.Equal(new[] { new MealSlot(Day1, MealType.Dinner) }, occasions.SelectionSlots);
    }

    // ── Selection slots ─────────────────────────────────────────────────────────

    [Fact]
    public void SelectionSlots_AreChronological_LunchBeforeDinnerWithinADay()
    {
        var occasions = MealOccasionResolver.Resolve(Standard(Day1, Day4));

        Assert.Equal(
            new[]
            {
                new MealSlot(Day1, MealType.Dinner),
                new MealSlot(Day2, MealType.Lunch),
                new MealSlot(Day2, MealType.Dinner),
                new MealSlot(Day3, MealType.Lunch),
                new MealSlot(Day3, MealType.Dinner),
            },
            occasions.SelectionSlots);
    }

    [Fact]
    public void Breakfast_IsNeverASelectionSlot()
    {
        // Breakfast is a buffet — served on most days, but there is nothing to choose (§4.1).
        var occasions = MealOccasionResolver.Resolve(Standard(Day1, Day4, arrivalBreakfast: true));

        Assert.True(occasions.ServedOn(Day1).Breakfast);
        Assert.All(occasions.SelectionSlots, s => Assert.True(s.MealType is MealType.Lunch or MealType.Dinner));
    }

    // ── The posted grid vs. the required subset ─────────────────────────────────

    [Fact]
    public void AllSittings_CoversEveryDayAndBothSittings_RegardlessOfEdgeChoices()
    {
        // The completion form's meal grid is indexed by this, so it must not shift when an edge-day
        // box is ticked — that is the whole reason it exists separately from SelectionSlots.
        var lean = MealOccasionResolver.Resolve(Standard(Day1, Day3));
        var full = MealOccasionResolver.Resolve(Standard(
            Day1, Day3, arrivalLunch: true, departureLunch: true, departureDinner: true));

        Assert.Equal(6, lean.AllSittings.Count); // 3 days × (lunch, dinner)
        Assert.Equal(lean.AllSittings, full.AllSittings);
        Assert.NotEqual(lean.SelectionSlots.Count, full.SelectionSlots.Count);
    }

    [Fact]
    public void IsSelectable_MarksOnlyTheSittingsTheGroupTakes()
    {
        var occasions = MealOccasionResolver.Resolve(Standard(Day1, Day3));

        // Grid order: Day1 lunch, Day1 dinner, Day2 lunch, Day2 dinner, Day3 lunch, Day3 dinner.
        Assert.False(occasions.IsSelectable(0)); // arrival lunch — not taken
        Assert.True(occasions.IsSelectable(1));  // arrival dinner — taken
        Assert.True(occasions.IsSelectable(2));
        Assert.True(occasions.IsSelectable(3));
        Assert.False(occasions.IsSelectable(4)); // departure lunch — not taken
        Assert.False(occasions.IsSelectable(5)); // departure dinner — not taken
    }

    [Fact]
    public void IsSelectable_IsFalseOutsideTheGrid()
    {
        var occasions = MealOccasionResolver.Resolve(Standard(Day1, Day3));

        Assert.False(occasions.IsSelectable(-1));
        Assert.False(occasions.IsSelectable(occasions.AllSittings.Count));
    }

    // ── Day guests (§4.2) ───────────────────────────────────────────────────────

    [Fact]
    public void DayGuestMeals_AreTheBookingFlagsOnADayThatServesThem()
    {
        var occasions = MealOccasionResolver.Resolve(Standard(
            Day1, Day4, dayGuestLunch: true, dayGuestDinner: true));

        Assert.Equal(new MealsServed(false, true, true), occasions.DayGuestMealsOn(Day2));
    }

    [Fact]
    public void DayGuestMeals_AreIntersectedWithWhatTheGroupTakesThatDay()
    {
        // The booking says day guests eat breakfast, but the group takes no breakfast on its arrival
        // day — nobody is cooking one, so the day guest is not counted for it.
        var occasions = MealOccasionResolver.Resolve(Standard(
            Day1, Day3, arrivalBreakfast: false, dayGuestBreakfast: true, dayGuestDinner: true));

        Assert.False(occasions.DayGuestMealsOn(Day1).Breakfast);
        Assert.True(occasions.DayGuestMealsOn(Day1).Dinner);
    }

    [Fact]
    public void DayGuestMeals_AreNothingOutsideTheStay()
    {
        var occasions = MealOccasionResolver.Resolve(Standard(
            Day1, Day3, dayGuestBreakfast: true, dayGuestLunch: true, dayGuestDinner: true));

        Assert.Equal(MealsServed.None, occasions.DayGuestMealsOn(Day4));
    }

    // ── Self-catering and degenerate ranges ─────────────────────────────────────

    [Fact]
    public void SelfCatering_ResolvesToNothingAtAll()
    {
        // Centralised here so no consumer re-checks the meal plan — the resolution simply is empty.
        var occasions = MealOccasionResolver.Resolve(Standard(
            Day1, Day4, MealPlan.SelfCateringAll,
            arrivalLunch: true, departureLunch: true, dayGuestLunch: true));

        Assert.Empty(occasions.SelectionSlots);
        Assert.Empty(occasions.AllSittings);
        Assert.Equal(MealsServed.None, occasions.ServedOn(Day2));
        Assert.Equal(MealsServed.None, occasions.DayGuestMealsOn(Day2));
    }

    [Fact]
    public void OutsideTheStay_ServesNothing()
    {
        var occasions = MealOccasionResolver.Resolve(Standard(Day1, Day4));

        Assert.Equal(MealsServed.None, occasions.ServedOn(Day1.AddDays(-1)));
        Assert.Equal(MealsServed.None, occasions.ServedOn(Day4.AddDays(1)));
    }

    [Fact]
    public void NonPositiveRange_ResolvesToNothing()
    {
        Assert.Empty(MealOccasionResolver.Resolve(Standard(Day1, Day1)).SelectionSlots);
        Assert.Empty(MealOccasionResolver.Resolve(Standard(Day3, Day1)).SelectionSlots);
    }

    // ── FromBooking / Resolve(Booking) ──────────────────────────────────────────

    [Fact]
    public void Resolve_ReadsTheStoredFlagsOffTheBooking()
    {
        var booking = new Booking
        {
            CheckIn = Day1,
            CheckOut = Day3,
            MealPlan = MealPlan.FullBoardAll,
            ArrivalDinner = true,
            DepartureBreakfast = true,
            DepartureLunch = true,
            DayGuestLunch = true,
        };

        var occasions = MealOccasionResolver.Resolve(booking);

        Assert.Equal(new MealsServed(true, true, false), occasions.ServedOn(Day3));
        Assert.Contains(new MealSlot(Day3, MealType.Lunch), occasions.SelectionSlots);
        Assert.True(occasions.DayGuestMealsOn(Day2).Lunch);
    }

    [Fact]
    public void Resolve_GeneralInquiryWithNoDates_IsEmpty()
    {
        var occasions = MealOccasionResolver.Resolve(new Booking { BookingType = BookingType.GeneralInquiry });

        Assert.Empty(occasions.SelectionSlots);
        Assert.Empty(occasions.AllSittings);
    }

    [Fact]
    public void Resolve_NeverReDerivesEdgeDaysFromTheStandardSchedule()
    {
        // The §7.1 failure this design exists to prevent: a booking whose stored flags contradict
        // the standard schedule must resolve to the *stored* answer, not the computed default.
        var booking = new Booking
        {
            CheckIn = Day1,
            CheckOut = Day3,
            MealPlan = MealPlan.FullBoardAll,
            ArrivalDinner = false,      // contradicts the standard schedule
            DepartureBreakfast = false, // contradicts the standard schedule
            ArrivalLunch = true,
        };

        var occasions = MealOccasionResolver.Resolve(booking);

        Assert.Equal(new MealsServed(false, true, false), occasions.ServedOn(Day1));
        Assert.Equal(MealsServed.None, occasions.ServedOn(Day3));
    }

    // ── The standard-schedule defaults ──────────────────────────────────────────

    [Fact]
    public void ApplyStandardEdgeDefaults_PrefillsArrivalDinnerAndDepartureBreakfast()
    {
        var booking = new Booking();

        MealOccasionResolver.ApplyStandardEdgeDefaults(booking);

        Assert.False(booking.ArrivalBreakfast);
        Assert.False(booking.ArrivalLunch);
        Assert.True(booking.ArrivalDinner);
        Assert.True(booking.DepartureBreakfast);
        Assert.False(booking.DepartureLunch);
        Assert.False(booking.DepartureDinner);
    }

    [Fact]
    public void StandardTimes_Are1400And1200()
    {
        Assert.Equal(new TimeOnly(14, 0), MealOccasionResolver.StandardCheckInTime);
        Assert.Equal(new TimeOnly(12, 0), MealOccasionResolver.StandardCheckOutTime);
    }

    // ── MealsServed helpers ─────────────────────────────────────────────────────

    [Fact]
    public void MealsServed_Intersect_KeepsOnlyMealsInBoth()
    {
        var a = new MealsServed(true, true, false);
        var b = new MealsServed(false, true, true);

        Assert.Equal(new MealsServed(false, true, false), a.Intersect(b));
    }

    [Fact]
    public void MealsServed_Any_IsFalseOnlyWhenNothingIsServed()
    {
        Assert.False(MealsServed.None.Any);
        Assert.True(new MealsServed(false, false, true).Any);
    }
}
