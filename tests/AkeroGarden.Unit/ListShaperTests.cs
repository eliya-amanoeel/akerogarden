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
                FullName = "Anna",
                ParticipantType = ParticipantType.Overnight,
                AgeCategory = AgeCategory.Adult,
                RoomAssignmentSnapshot = "Rum 1 — Blidö",
            },
            new Participant
            {
                FullName = "Bo",
                ParticipantType = ParticipantType.DayGuest,
                AgeCategory = AgeCategory.Child,
                AttendanceDate = new DateOnly(2026, 8, 2),
            }));

        var anna = data.Participants[0];
        Assert.Equal("Rum 1 — Blidö", anna.Room);
        Assert.Equal("—", anna.Attendance); // overnight → no single attendance date

        var bo = data.Participants[1];
        Assert.Equal("—", bo.Room);           // day guest → no room
        Assert.Equal("2026-08-02", bo.Attendance);
    }

    // Day labels are locale-formatted; compute them via the public helper so the test verifies the
    // aggregation logic, not the exact Swedish weekday spelling.
    private static readonly DateOnly D1 = new(2026, 8, 1); // arrival — dinner only
    private static readonly DateOnly D2 = new(2026, 8, 2); // middle — breakfast + lunch + dinner
    private static readonly DateOnly D3 = new(2026, 8, 3); // departure — breakfast only
    private static readonly string L1 = ListShaper.MealDateHeader(D1);
    private static readonly string L2 = ListShaper.MealDateHeader(D2);
    private static readonly string L3 = ListShaper.MealDateHeader(D3);

    [Fact]
    public void Prep_AggregatesDishCountsPerDayPerMeal_NoNames()
    {
        // Two overnight full-board guests, both attend breakfast.
        var anna = FullBoard("Anna");
        anna.MealPreferences.Add(new MealPreference { Date = D1, MealType = MealType.Dinner, MealOptionSnapshot = "Köttbullar" });
        anna.MealPreferences.Add(new MealPreference { Date = D2, MealType = MealType.Lunch, MealOptionSnapshot = "Soppa" });
        anna.MealPreferences.Add(new MealPreference { Date = D2, MealType = MealType.Dinner, MealOptionSnapshot = "Lax" });

        var bo = FullBoard("Bo");
        bo.MealPreferences.Add(new MealPreference { Date = D1, MealType = MealType.Dinner, MealOptionSnapshot = "Köttbullar" });
        bo.MealPreferences.Add(new MealPreference { Date = D2, MealType = MealType.Lunch, MealOptionSnapshot = "Soppa" });
        bo.MealPreferences.Add(new MealPreference { Date = D2, MealType = MealType.Dinner, MealOptionSnapshot = "Halloumi" });

        var prep = ListShaper.Shape(Booking(anna, bo)).Prep;

        // No names anywhere in the prep summary.
        Assert.DoesNotContain(prep, r => r.Option is "Anna" or "Bo");

        // 08-01: dinner only → 2× Köttbullar.
        Assert.Equal(new[] { (L1, "Middag", "Köttbullar", 2) }, ForDay(prep, L1));

        // 08-02: breakfast headcount (both), lunch (2× Soppa), dinner (1× Halloumi, 1× Lax — most first, ties alpha).
        Assert.Equal(new[]
        {
            (L2, "Frukost", "Frukostbuffé", 2),
            (L2, "Lunch", "Soppa", 2),
            (L2, "Middag", "Halloumi", 1),
            (L2, "Middag", "Lax", 1),
        }, ForDay(prep, L2));

        // 08-03 (departure): breakfast only.
        Assert.Equal(new[] { (L3, "Frukost", "Frukostbuffé", 2) }, ForDay(prep, L3));
    }

    [Fact]
    public void Prep_DayGuestBreakfast_CountedOnlyOnAttendanceDate()
    {
        var overnight = FullBoard("Anna"); // present the whole stay, attends breakfast on 08-02 and 08-03
        var dayGuest = new Participant
        {
            FullName = "Gäst",
            ParticipantType = ParticipantType.DayGuest,
            AttendanceDate = D2,
            AttendsBreakfast = true,
        };

        var prep = ListShaper.Shape(Booking(overnight, dayGuest)).Prep;

        // 08-02 breakfast counts both; 08-03 breakfast counts only the overnight guest.
        Assert.Contains((L2, "Frukost", "Frukostbuffé", 2), Tuples(prep));
        Assert.Contains((L3, "Frukost", "Frukostbuffé", 1), Tuples(prep));
    }

    [Fact]
    public void Prep_SelfCatering_NoBreakfast_ProducesNoRows()
    {
        var data = ListShaper.Shape(Booking(
            new Participant { FullName = "Anna", ParticipantType = ParticipantType.Overnight, IsFullBoard = false, AttendsBreakfast = false }));
        Assert.Empty(data.Prep);
    }

    [Fact]
    public void SpecialDiets_OnlyNamesPeopleWithRequirementsOrNotes_WithTheirMeals()
    {
        var plain = FullBoard("Anna"); // no dietary → excluded
        plain.MealPreferences.Add(new MealPreference { Date = D1, MealType = MealType.Dinner, MealOptionSnapshot = "Köttbullar" });

        var allergic = FullBoard("Bengt");
        allergic.DietaryRequirements = [DietaryRequirement.GlutenFree];
        allergic.MealPreferences.Add(new MealPreference { Date = D1, MealType = MealType.Dinner, MealOptionSnapshot = "Köttbullar" });
        allergic.MealPreferences.Add(new MealPreference { Date = D2, MealType = MealType.Lunch, MealOptionSnapshot = "Soppa" });

        var noted = FullBoard("Cecilia");
        noted.DietaryRequirements = [DietaryRequirement.None];
        noted.DietaryNotes = "Ingen paprika";

        var special = ListShaper.Shape(Booking(plain, allergic, noted)).SpecialDiets;

        Assert.Equal(new[] { "Bengt", "Cecilia" }, special.Select(s => s.Name).ToArray()); // Anna excluded
        var bengt = special.First(s => s.Name == "Bengt");
        Assert.Contains("Glutenintolerans", bengt.Dietary);
        // Their meals travel with them so the kitchen knows what to adapt.
        Assert.Equal($"{L1}: Middag Köttbullar\n{L2}: Lunch Soppa", bengt.Meals);
        Assert.Contains("Ingen paprika", special.First(s => s.Name == "Cecilia").Dietary);
    }

    private static Participant FullBoard(string name) => new()
    {
        FullName = name,
        ParticipantType = ParticipantType.Overnight,
        IsFullBoard = true,
        AttendsBreakfast = true,
    };

    private static (string, string, string, int)[] Tuples(IEnumerable<PrepRow> prep) =>
        prep.Select(r => (r.Day, r.Meal, r.Option, r.Count)).ToArray();

    private static (string, string, string, int)[] ForDay(IEnumerable<PrepRow> prep, string day) =>
        prep.Where(r => r.Day == day).Select(r => (r.Day, r.Meal, r.Option, r.Count)).ToArray();

    [Fact]
    public void DietarySummary_JoinsAndAppendsOtherNotes()
    {
        var p = new Participant
        {
            FullName = "Anna",
            ParticipantType = ParticipantType.Overnight,
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
