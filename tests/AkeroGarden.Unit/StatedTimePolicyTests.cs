using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the stated arrival/departure times (§2.8, FR-100/101): which values are
/// offered, how a posted value is validated server-side, and when one warrants the admin deviation
/// warning. Pure logic, no database (NFR-26).
/// </summary>
public class StatedTimePolicyTests
{
    private static readonly TimeOnly StandardIn = MealOccasionResolver.StandardCheckInTime;   // 14:00
    private static readonly TimeOnly StandardOut = MealOccasionResolver.StandardCheckOutTime; // 12:00

    // ── The offered values ────────────────────────────────────────────────────────

    [Fact]
    public void Options_RunFrom0700To2200OnTheHalfHour()
    {
        var options = StatedTimePolicy.Options();

        Assert.Equal(new TimeOnly(7, 0), options[0]);
        Assert.Equal(new TimeOnly(22, 0), options[^1]);
        Assert.Equal(31, options.Count); // 15 hours × 2 + 1
        Assert.All(options, t => Assert.Equal(0, t.Minute % 30));
    }

    [Fact]
    public void Options_IncludeBothStandardTimes()
    {
        // The ordinary case has to be selectable, or the defaults could not round-trip.
        Assert.Contains(StandardIn, StatedTimePolicy.Options());
        Assert.Contains(StandardOut, StatedTimePolicy.Options());
    }

    [Fact]
    public void Options_AreStrictlyIncreasing_AndDoNotWrapPastMidnight()
    {
        var options = StatedTimePolicy.Options();

        for (var i = 1; i < options.Count; i++)
            Assert.True(options[i] > options[i - 1], $"option {i} ({options[i]}) is not after {options[i - 1]}");
    }

    // ── Parsing a posted value ────────────────────────────────────────────────────

    [Theory]
    [InlineData("07:00", 7, 0)]
    [InlineData("14:00", 14, 0)]
    [InlineData("08:30", 8, 30)]
    [InlineData("22:00", 22, 0)]
    public void Normalize_KeepsAnOfferedValue(string posted, int hour, int minute)
    {
        Assert.Equal(new TimeOnly(hour, minute), StatedTimePolicy.Normalize(posted, StandardIn));
    }

    [Theory]
    [InlineData(null)]              // field not posted
    [InlineData("")]
    [InlineData("eftermiddagen")]   // the free text FR-100 exists to prevent
    [InlineData("06:30")]           // before the earliest offered
    [InlineData("22:30")]           // after the latest offered
    [InlineData("08:15")]           // off the half hour
    [InlineData("2026-08-10")]
    public void Normalize_FallsBackToTheStandard_ForAnythingTheDropdownCannotProduce(string? posted)
    {
        // Deliberately not snapped to the nearest offered time: 08:15 → 08:00 would invent a claim
        // the guest never made. A value like this can only come from a crafted request.
        Assert.Equal(StandardIn, StatedTimePolicy.Normalize(posted, StandardIn));
    }

    [Fact]
    public void TryParse_ReportsFailure_SoAStoredValueCanBeLeftAlone()
    {
        Assert.False(StatedTimePolicy.TryParse("08:15", out _));
        Assert.False(StatedTimePolicy.TryParse(null, out _));

        Assert.True(StatedTimePolicy.TryParse("09:30", out var parsed));
        Assert.Equal(new TimeOnly(9, 30), parsed);
    }

    [Fact]
    public void Display_And_Normalize_RoundTripEveryOfferedValue()
    {
        // The dropdown writes Display(t) and the server reads it back — if these ever disagree the
        // form would silently reset every booking to the standard time.
        foreach (var t in StatedTimePolicy.Options())
            Assert.Equal(t, StatedTimePolicy.Normalize(StatedTimePolicy.Display(t), StandardIn));
    }

    [Fact]
    public void Display_FallsBackToTheStandard_ForABookingWithNoStatedTime()
    {
        Assert.Equal("14:00", StatedTimePolicy.Display(null, StandardIn));
        Assert.Equal("09:30", StatedTimePolicy.Display(new TimeOnly(9, 30), StandardIn));
    }

    // ── The admin deviation warning (FR-101) ──────────────────────────────────────

    [Fact]
    public void Deviation_IsNull_WhenTheTimeMatchesTheStandard()
    {
        Assert.Null(StatedTimePolicy.Deviation("Incheckning", StandardIn, StandardIn));
    }

    [Fact]
    public void Deviation_IsNull_WhenNoTimeWasStated()
    {
        Assert.Null(StatedTimePolicy.Deviation("Incheckning", null, StandardIn));
    }

    [Fact]
    public void Deviation_NamesBothTheStatedTimeAndTheStandard()
    {
        var warning = StatedTimePolicy.Deviation("Incheckning", new TimeOnly(8, 0), StandardIn);

        Assert.Equal("Incheckning 08:00 avviker från standard 14:00.", warning);
    }

    [Fact]
    public void Deviation_AlsoFiresForALaterThanStandardTime()
    {
        // Not only early arrivals — a 20:00 check-out eats most of the turnover day.
        var warning = StatedTimePolicy.Deviation("Utcheckning", new TimeOnly(20, 0), StandardOut);

        Assert.Equal("Utcheckning 20:00 avviker från standard 12:00.", warning);
    }
}
