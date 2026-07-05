using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the download-list shaping (FR-44/45): ordering, snapshot-based fields, meal
/// formatting, and dietary summaries — all built off snapshot values so a list reflects what was
/// agreed (NFR-15/16).
/// </summary>
public class ListShaperTests
{
    private static Booking Booking(params Participant[] participants)
    {
        var b = new Booking
        {
            ContactName = "Förening X",
            CheckIn = new DateOnly(2026, 8, 1),
            CheckOut = new DateOnly(2026, 8, 3),
        };
        foreach (var p in participants) b.Participants.Add(p);
        return b;
    }

    [Fact]
    public void Shape_OrdersOvernightFirst_ThenAlphabetical()
    {
        var data = ListShaper.Shape(Booking(
            new Participant { FullName = "Bo", ParticipantType = ParticipantType.DayGuest, AttendanceDate = new DateOnly(2026, 8, 2) },
            new Participant { FullName = "Åsa", ParticipantType = ParticipantType.Overnight },
            new Participant { FullName = "Anna", ParticipantType = ParticipantType.Overnight }));

        Assert.Equal(new[] { "Anna", "Åsa", "Bo" }, data.Participants.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void ParticipantRow_UsesRoomSnapshot_AndDayGuestAttendance()
    {
        var data = ListShaper.Shape(Booking(
            new Participant
            {
                FullName = "Anna", ParticipantType = ParticipantType.Overnight,
                AgeCategory = AgeCategory.Adult, RoomAssignmentSnapshot = "Rum 1 — Blidö",
            },
            new Participant
            {
                FullName = "Bo", ParticipantType = ParticipantType.DayGuest,
                AgeCategory = AgeCategory.Child, AttendanceDate = new DateOnly(2026, 8, 2),
            }));

        var anna = data.Participants[0];
        Assert.Equal("Rum 1 — Blidö", anna.Room);
        Assert.Equal("—", anna.Attendance); // overnight → no single attendance date

        var bo = data.Participants[1];
        Assert.Equal("—", bo.Room);           // day guest → no room
        Assert.Equal("2026-08-02", bo.Attendance);
    }

    [Fact]
    public void FoodRow_FormatsMealsPerDay_AndFlags()
    {
        var p = new Participant
        {
            FullName = "Anna", ParticipantType = ParticipantType.Overnight,
            IsFullBoard = true, AttendsBreakfast = true,
        };
        p.MealPreferences.Add(new MealPreference { Date = new DateOnly(2026, 8, 2), MealType = MealType.Lunch, MealOptionSnapshot = "Fisk" });
        p.MealPreferences.Add(new MealPreference { Date = new DateOnly(2026, 8, 1), MealType = MealType.Dinner, MealOptionSnapshot = "Köttbullar" });

        var food = ListShaper.Shape(Booking(p)).Food[0];

        Assert.Equal("Ja", food.FullBoard);
        Assert.Equal("Ja", food.Breakfast);
        // Ordered by date then meal: 08-01 Middag before 08-02 Lunch.
        Assert.Equal("2026-08-01 Middag: Köttbullar; 2026-08-02 Lunch: Fisk", food.Meals);
    }

    [Fact]
    public void FoodRow_SelfCatering_HasNoMeals()
    {
        var data = ListShaper.Shape(Booking(
            new Participant { FullName = "Anna", ParticipantType = ParticipantType.Overnight, IsFullBoard = false }));
        Assert.Equal("Nej", data.Food[0].FullBoard);
        Assert.Equal("—", data.Food[0].Meals);
    }

    [Fact]
    public void DietarySummary_JoinsAndAppendsOtherNotes()
    {
        var p = new Participant
        {
            FullName = "Anna", ParticipantType = ParticipantType.Overnight,
            DietaryRequirements = [DietaryRequirement.Vegetarian, DietaryRequirement.Other],
            DietaryNotes = "Selleri",
        };
        var row = ListShaper.Shape(Booking(p)).Participants[0];
        Assert.Contains("Vegetarian", row.Dietary);
        Assert.Contains("Annat (Selleri)", row.Dietary);
    }

    [Fact]
    public void DietarySummary_None_ShowsDash()
    {
        var p = new Participant { FullName = "Anna", ParticipantType = ParticipantType.Overnight, DietaryRequirements = [DietaryRequirement.None] };
        Assert.Equal("—", ListShaper.Shape(Booking(p)).Participants[0].Dietary);
    }

    [Theory]
    [InlineData(BookingStatus.DetailsComplete, true)]
    [InlineData(BookingStatus.Active, true)]
    [InlineData(BookingStatus.Completed, true)]
    [InlineData(BookingStatus.Approved, false)]
    [InlineData(BookingStatus.Pending, false)]
    [InlineData(BookingStatus.Cancelled, false)]
    public void IsAvailableFor_OnlyDetailsCompleteOnward(BookingStatus status, bool expected) =>
        Assert.Equal(expected, ListShaper.IsAvailableFor(status));
}
