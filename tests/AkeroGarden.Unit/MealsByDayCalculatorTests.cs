using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the meals-by-day-of-stay rule (requirements.md §4.1, FR-57): arrival day
/// dinner only, middle days lunch + dinner, departure day none — so a one-night stay collects a
/// single dinner. Pure logic, no database (NFR-26).
/// </summary>
public class MealsByDayCalculatorTests
{
    private static readonly DateOnly Day1 = new(2026, 8, 10);
    private static readonly DateOnly Day2 = new(2026, 8, 11);
    private static readonly DateOnly Day3 = new(2026, 8, 12);
    private static readonly DateOnly Day4 = new(2026, 8, 13);

    [Fact]
    public void OneNight_CollectsSingleDinnerOnArrival()
    {
        var slots = MealsByDayCalculator.Slots(Day1, Day2);

        Assert.Equal(new[] { new MealSlot(Day1, MealType.Dinner) }, slots);
    }

    [Fact]
    public void TwoNights_ArrivalDinner_MiddleLunchAndDinner_DepartureNone()
    {
        var slots = MealsByDayCalculator.Slots(Day1, Day3);

        Assert.Equal(new[]
        {
            new MealSlot(Day1, MealType.Dinner),   // arrival
            new MealSlot(Day2, MealType.Lunch),    // middle
            new MealSlot(Day2, MealType.Dinner),
        }, slots);
    }

    [Fact]
    public void ThreeNights_TwoMiddleDays_FiveSlots()
    {
        var slots = MealsByDayCalculator.Slots(Day1, Day4);

        Assert.Equal(new[]
        {
            new MealSlot(Day1, MealType.Dinner),
            new MealSlot(Day2, MealType.Lunch),
            new MealSlot(Day2, MealType.Dinner),
            new MealSlot(Day3, MealType.Lunch),
            new MealSlot(Day3, MealType.Dinner),
        }, slots);
    }

    [Fact]
    public void ArrivalDay_HasNoLunch()
    {
        var slots = MealsByDayCalculator.Slots(Day1, Day3);

        Assert.DoesNotContain(new MealSlot(Day1, MealType.Lunch), slots);
    }

    [Fact]
    public void DepartureDay_HasNoMeals()
    {
        var slots = MealsByDayCalculator.Slots(Day1, Day3);

        Assert.DoesNotContain(slots, s => s.Date == Day3);
    }

    [Fact]
    public void SameDay_ReturnsEmpty()
    {
        Assert.Empty(MealsByDayCalculator.Slots(Day1, Day1));
    }

    [Fact]
    public void CheckOutBeforeCheckIn_ReturnsEmpty()
    {
        Assert.Empty(MealsByDayCalculator.Slots(Day3, Day1));
    }

    [Fact]
    public void Slots_AreChronologicalWithLunchBeforeDinner()
    {
        var slots = MealsByDayCalculator.Slots(Day1, Day4);

        // Each slot is at or after the previous by date; within a day lunch precedes dinner.
        for (var i = 1; i < slots.Count; i++)
        {
            var prev = slots[i - 1];
            var cur = slots[i];
            Assert.True(cur.Date > prev.Date
                || (cur.Date == prev.Date && prev.MealType == MealType.Lunch && cur.MealType == MealType.Dinner));
        }
    }

    // ── ServedOn (which meals the kitchen serves that day, §4.1) ────────────────────

    [Fact]
    public void ServedOn_ArrivalDay_DinnerOnly()
    {
        var served = MealsByDayCalculator.ServedOn(Day1, Day1, Day4);
        Assert.Equal(new MealsServed(Breakfast: false, Lunch: false, Dinner: true), served);
    }

    [Fact]
    public void ServedOn_MiddleDay_AllMeals()
    {
        var served = MealsByDayCalculator.ServedOn(Day2, Day1, Day4);
        Assert.Equal(new MealsServed(true, true, true), served);
    }

    [Fact]
    public void ServedOn_DepartureDay_BreakfastOnly()
    {
        var served = MealsByDayCalculator.ServedOn(Day4, Day1, Day4);
        Assert.Equal(new MealsServed(Breakfast: true, Lunch: false, Dinner: false), served);
    }

    [Fact]
    public void ServedOn_OneNightStay_ArrivalDinner_DepartureBreakfast()
    {
        Assert.Equal(new MealsServed(false, false, true), MealsByDayCalculator.ServedOn(Day1, Day1, Day2)); // arrival
        Assert.Equal(new MealsServed(true, false, false), MealsByDayCalculator.ServedOn(Day2, Day1, Day2)); // departure
    }

    [Fact]
    public void ServedOn_OutsideStay_Nothing()
    {
        var served = MealsByDayCalculator.ServedOn(Day1.AddDays(-1), Day1, Day4);
        Assert.Equal(new MealsServed(false, false, false), served);
    }
}
