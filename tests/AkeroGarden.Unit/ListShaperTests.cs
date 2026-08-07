using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the download-list shaping (FR-44/45): ordering, snapshot-based fields, the
/// kitchen prep headcounts, and allergy summaries — all built off snapshot values so a list
/// reflects what was agreed (NFR-15/16).
///
/// <para>The group eats one dish per sitting (FR-105), so a prep row is now "the dish, and how many
/// sit down to it" — the headcount is computed from who is present rather than counted off
/// individual selections.</para>
/// </summary>
public class ListShaperTests
{
    private static readonly DateOnly D1 = new(2026, 8, 1); // arrival
    private static readonly DateOnly D2 = new(2026, 8, 2); // middle
    private static readonly DateOnly D3 = new(2026, 8, 3); // departure

    // Day labels are locale-formatted; compute them via the public helper so the tests verify the
    // aggregation logic, not the exact Swedish weekday spelling.
    private static readonly string L1 = ListShaper.MealDateHeader(D1);
    private static readonly string L2 = ListShaper.MealDateHeader(D2);
    private static readonly string L3 = ListShaper.MealDateHeader(D3);

    /// <summary>A full-board booking on the standard schedule (arrival dinner, departure breakfast).</summary>
    private static Booking Booking(params Participant[] participants)
    {
        var b = new Booking
        {
            ContactName = "Förening X",
            CheckIn = D1,
            CheckOut = D3,
            MealPlan = MealPlan.FullBoardAll,
            ArrivalDinner = true,
            DepartureBreakfast = true,
        };
        foreach (var p in participants) b.Participants.Add(p);
        return b;
    }

    private static Participant Overnight(string name) =>
        new() { FullName = name, ParticipantType = ParticipantType.Overnight };

    private static Participant DayGuest(string name, DateOnly date) =>
        new() { FullName = name, ParticipantType = ParticipantType.DayGuest, AttendanceDate = date };

    private static void Choose(Booking b, DateOnly date, MealType type, string option) =>
        b.MealSelections.Add(new BookingMealSelection
        {
            Date = date,
            MealType = type,
            MealOptionSnapshot = option,
        });

    private static (string, string, string, int)[] Tuples(IEnumerable<PrepRow> prep) =>
        prep.Select(r => (r.Day, r.Meal, r.Option, r.Count)).ToArray();

    private static (string, string, string, int)[] ForDay(IEnumerable<PrepRow> prep, string day) =>
        prep.Where(r => r.Day == day).Select(r => (r.Day, r.Meal, r.Option, r.Count)).ToArray();

    // ── Roster ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Shape_OrdersOvernightFirst_ThenAlphabetical()
    {
        var data = ListShaper.Shape(Booking(
            DayGuest("Bo", D2),
            Overnight("Åsa"),
            Overnight("Anna")));

        Assert.Equal(new[] { "Anna", "Åsa", "Bo" }, data.Participants.Select(p => p.Name).ToArray());
    }

    [Fact]
    public void ParticipantRow_ShowsDayGuestAttendanceAndDashForOvernight()
    {
        var data = ListShaper.Shape(Booking(Overnight("Anna"), DayGuest("Bo", D2)));

        Assert.Equal("—", data.Participants[0].Attendance);
        Assert.Equal("2026-08-02", data.Participants[1].Attendance);
    }

    // ── Kitchen prep summary (FR-45) ──────────────────────────────────────────────

    [Fact]
    public void Prep_ShowsTheGroupsDishAndHowManyEatIt()
    {
        var booking = Booking(Overnight("Anna"), Overnight("Bo"));
        Choose(booking, D1, MealType.Dinner, "Köttbullar");
        Choose(booking, D2, MealType.Lunch, "Soppa");
        Choose(booking, D2, MealType.Dinner, "Lax");

        var prep = ListShaper.Shape(booking).Prep;

        // No names anywhere in the prep summary.
        Assert.DoesNotContain(prep, r => r.Option is "Anna" or "Bo");

        // Arrival day: dinner only.
        Assert.Equal(new[] { (L1, "Middag", "Köttbullar", 2) }, ForDay(prep, L1));

        // Middle day: breakfast buffet + the group's lunch and dinner.
        Assert.Equal(new[]
        {
            (L2, "Frukost", "Frukostbuffé", 2),
            (L2, "Lunch", "Soppa", 2),
            (L2, "Middag", "Lax", 2),
        }, ForDay(prep, L2));

        // Departure day: breakfast only.
        Assert.Equal(new[] { (L3, "Frukost", "Frukostbuffé", 2) }, ForDay(prep, L3));
    }

    [Fact]
    public void Prep_FollowsTheEdgeDayChoices_NotTheStandardSchedule()
    {
        // A group taking lunch on its departure day gets a departure lunch row (§4.1, FR-105).
        var booking = Booking(Overnight("Anna"));
        booking.DepartureLunch = true;
        Choose(booking, D3, MealType.Lunch, "Sallad");

        var prep = ListShaper.Shape(booking).Prep;

        Assert.Contains((L3, "Lunch", "Sallad", 1), Tuples(prep));
    }

    [Fact]
    public void Prep_ArrivalDayWithNoMeals_ProducesNoRowsForThatDay()
    {
        var booking = Booking(Overnight("Anna"));
        booking.ArrivalDinner = false;

        var prep = ListShaper.Shape(booking).Prep;

        Assert.Empty(ForDay(prep, L1));
    }

    [Fact]
    public void Prep_DayGuestsCountOnlyOnTheirDate_AndOnlyForMealsTheyAttend()
    {
        var booking = Booking(Overnight("Anna"), DayGuest("Gäst", D2));
        booking.DayGuestLunch = true; // lunch only — no breakfast, no dinner
        Choose(booking, D2, MealType.Lunch, "Soppa");
        Choose(booking, D2, MealType.Dinner, "Lax");

        var prep = ListShaper.Shape(booking).Prep;

        Assert.Contains((L2, "Lunch", "Soppa", 2), Tuples(prep));   // overnight + day guest
        Assert.Contains((L2, "Middag", "Lax", 1), Tuples(prep));    // overnight only
        Assert.Contains((L2, "Frukost", "Frukostbuffé", 1), Tuples(prep));
    }

    [Fact]
    public void Prep_DayGuestNotCountedForAMealTheGroupDoesNotTake()
    {
        // The booking says day guests eat breakfast, but the group takes none on the arrival day.
        // Nobody is cooking one, so the day guest must not appear in a breakfast count.
        var booking = Booking(Overnight("Anna"), DayGuest("Gäst", D1));
        booking.DayGuestBreakfast = true;
        Choose(booking, D1, MealType.Dinner, "Köttbullar");

        var prep = ListShaper.Shape(booking).Prep;

        Assert.DoesNotContain(prep, r => r.Day == L1 && r.Meal == "Frukost");
    }

    [Fact]
    public void Prep_SittingTakenButNoDishChosen_StillShowsTheHeadcount()
    {
        // The admin can add an edge-day meal after submission (FR-48), leaving it unchosen. Omitting
        // the row would read as "nobody eats", which is a different and wrong claim.
        var booking = Booking(Overnight("Anna"), Overnight("Bo"));
        booking.DepartureLunch = true; // no BookingMealSelection for it

        var prep = ListShaper.Shape(booking).Prep;

        Assert.Contains((L3, "Lunch", "Ej vald", 2), Tuples(prep));
    }

    [Fact]
    public void Prep_SelfCatering_ProducesNoRows()
    {
        var booking = Booking(Overnight("Anna"));
        booking.MealPlan = MealPlan.SelfCateringAll;

        Assert.Empty(ListShaper.Shape(booking).Prep);
    }

    // ── Allergy list (FR-45) ──────────────────────────────────────────────────────

    [Fact]
    public void SpecialDiets_OnlyNamesPeopleWithAnAllergyOrNote_WithWhenTheyAttend()
    {
        var plain = Overnight("Anna");                      // no allergy → excluded

        var allergic = Overnight("Bengt");
        allergic.Allergies = [Allergy.Gluten];

        var noted = DayGuest("Cecilia", D2);
        noted.Allergies = [Allergy.None];
        noted.AllergyNotes = "Ingen paprika";

        var special = ListShaper.Shape(Booking(plain, allergic, noted)).SpecialDiets;

        Assert.Equal(new[] { "Bengt", "Cecilia" }, special.Select(s => s.Name).ToArray());

        var bengt = special.First(s => s.Name == "Bengt");
        Assert.Contains("Glutenintolerans", bengt.Dietary);
        Assert.Equal("Hela vistelsen", bengt.Attendance);

        var cecilia = special.First(s => s.Name == "Cecilia");
        Assert.Contains("Ingen paprika", cecilia.Dietary);
        Assert.Equal(L2, cecilia.Attendance);
    }

    [Fact]
    public void AllergySummary_JoinsAndAppendsOtherNotes()
    {
        var p = Overnight("Anna");
        p.Allergies = [Allergy.Nut, Allergy.Other];
        p.AllergyNotes = "Selleri";

        var row = ListShaper.Shape(Booking(p)).Participants[0];

        Assert.Contains("Nötallergi", row.Dietary);
        Assert.Contains("Annat (Selleri)", row.Dietary);
    }

    [Fact]
    public void AllergySummary_None_ShowsDash()
    {
        var p = Overnight("Anna");
        p.Allergies = [Allergy.None];

        Assert.Equal("—", ListShaper.Shape(Booking(p)).Participants[0].Dietary);
    }

    // ── Dietary-preference counts (§4.6, FR-45) ───────────────────────────────────

    [Fact]
    public void Shape_CarriesTheBookingsPreferenceCounts()
    {
        var booking = Booking(Overnight("Anna"));
        booking.VegetarianCount = 3;
        booking.VeganCount = 1;

        var data = ListShaper.Shape(booking);

        Assert.Equal(3, data.VegetarianCount);
        Assert.Equal(1, data.VeganCount);
    }

    // ── Availability ──────────────────────────────────────────────────────────────

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
