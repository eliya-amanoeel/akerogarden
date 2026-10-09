using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the range-level availability rule (NFR-26, FR-10, FR-106): whether a whole
/// requested stay fits, and the Swedish explanation when it does not (NFR-32). The database path
/// through <c>OccupancyProvider</c> is covered by the integration suite.
/// </summary>
public class AvailabilityServiceTests
{
    private static Dictionary<DateOnly, UnavailableReason> Free(DateOnly from, int days)
    {
        var map = new Dictionary<DateOnly, UnavailableReason>();
        for (var i = 0; i < days; i++)
            map[from.AddDays(i)] = UnavailableReason.None;
        return map;
    }

    private static Dictionary<DateOnly, UnavailableReason> With(
        Dictionary<DateOnly, UnavailableReason> map, UnavailableReason reason, params DateOnly[] dates)
    {
        foreach (var d in dates)
            map[d] = reason;
        return map;
    }

    [Fact]
    public void EveryDateFree_StayIsAvailable()
    {
        var checkIn = new DateOnly(2026, 10, 10);
        var checkOut = new DateOnly(2026, 10, 15);

        var result = AvailabilityService.Evaluate(checkIn, checkOut, Free(checkIn, 10));

        Assert.True(result.IsAvailable);
        Assert.Empty(result.ConflictingStayDates);
        Assert.Empty(result.ConflictingTurnoverDates);
    }

    [Fact]
    public void AnOccupiedNight_MakesTheStayUnavailable()
    {
        var checkIn = new DateOnly(2026, 10, 10);
        var checkOut = new DateOnly(2026, 10, 15);
        var reasons = With(Free(checkIn, 10), UnavailableReason.Occupied, new DateOnly(2026, 10, 12));

        var result = AvailabilityService.Evaluate(checkIn, checkOut, reasons);

        Assert.False(result.IsAvailable);
        Assert.Equal(new[] { new DateOnly(2026, 10, 12) }, result.ConflictingStayDates);
        Assert.Empty(result.ConflictingTurnoverDates);
    }

    /// <summary>
    /// The check-out date is not a night the group sleeps, so it is not a stay conflict — but the
    /// stay's own turnover window holds it, and something else holding it is still a conflict. This
    /// is the case a nights-only check would wave through, letting one group's departure land on
    /// the next group's arrival.
    /// </summary>
    [Fact]
    public void ADepartureCollidingWithTheNextArrival_IsCaught()
    {
        var checkIn = new DateOnly(2026, 10, 10);
        var checkOut = new DateOnly(2026, 10, 15);
        // Another group arrives on the 15th — the day this group leaves and cleaning must happen.
        var reasons = With(Free(checkIn, 10), UnavailableReason.Occupied, new DateOnly(2026, 10, 15));

        var result = AvailabilityService.Evaluate(checkIn, checkOut, reasons);

        Assert.False(result.IsAvailable);
        Assert.Empty(result.ConflictingStayDates);
        Assert.Equal(new[] { new DateOnly(2026, 10, 15) }, result.ConflictingTurnoverDates);
    }

    [Fact]
    public void TheSecondTurnoverDay_IsAlsoRequired()
    {
        var checkIn = new DateOnly(2026, 10, 10);
        var checkOut = new DateOnly(2026, 10, 15);
        var reasons = With(Free(checkIn, 10), UnavailableReason.Occupied, new DateOnly(2026, 10, 16));

        var result = AvailabilityService.Evaluate(checkIn, checkOut, reasons);

        Assert.False(result.IsAvailable);
        Assert.Equal(new[] { new DateOnly(2026, 10, 16) }, result.ConflictingTurnoverDates);
    }

    [Fact]
    public void NothingBeyondTheTurnoverWindow_IsRequired()
    {
        var checkIn = new DateOnly(2026, 10, 10);
        var checkOut = new DateOnly(2026, 10, 15);
        // A group arriving on the 17th leaves the 15th and 16th clear for cleaning.
        var reasons = With(Free(checkIn, 12), UnavailableReason.Occupied, new DateOnly(2026, 10, 17));

        Assert.True(AvailabilityService.Evaluate(checkIn, checkOut, reasons).IsAvailable);
    }

    [Fact]
    public void AWholePropertyBlock_MakesTheStayUnavailable()
    {
        var checkIn = new DateOnly(2026, 10, 10);
        var checkOut = new DateOnly(2026, 10, 15);
        var reasons = With(Free(checkIn, 10), UnavailableReason.WholePropertyBlock, new DateOnly(2026, 10, 11));

        Assert.False(AvailabilityService.Evaluate(checkIn, checkOut, reasons).IsAvailable);
    }

    [Fact]
    public void AMalformedRange_IsLeftToTheModelValidator()
    {
        var date = new DateOnly(2026, 10, 10);

        // Not "available" as a claim about the property — a shape error owned by
        // StayRequestInput.Validate, which must not be contradicted by a second message.
        Assert.True(AvailabilityService.Evaluate(date, date, Free(date, 5)).IsAvailable);
        Assert.True(AvailabilityService.Evaluate(date, date.AddDays(-2), Free(date, 5)).IsAvailable);
    }

    [Fact]
    public void Describe_NamesContiguousRuns_NotASpanAcrossFreeDays()
    {
        var checkIn = new DateOnly(2026, 10, 10);
        var checkOut = new DateOnly(2026, 10, 20);
        var reasons = With(Free(checkIn, 14), UnavailableReason.Occupied,
            new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 17));

        var message = AvailabilityService.Describe(AvailabilityService.Evaluate(checkIn, checkOut, reasons));

        Assert.Contains("11–12 oktober", message);
        Assert.Contains("17 oktober", message);
        // The free days between the runs must not be swept into a single span.
        Assert.DoesNotContain("11–17", message);
    }

    [Fact]
    public void Describe_SingleDate_UsesSingularSwedish()
    {
        var checkIn = new DateOnly(2026, 10, 10);
        var checkOut = new DateOnly(2026, 10, 15);
        var reasons = With(Free(checkIn, 10), UnavailableReason.Occupied, new DateOnly(2026, 10, 12));

        var message = AvailabilityService.Describe(AvailabilityService.Evaluate(checkIn, checkOut, reasons));

        Assert.StartsWith("Datumet 12 oktober är tyvärr upptaget.", message);
    }

    [Fact]
    public void Describe_TurnoverConflict_ExplainsTheDaysAfterDeparture_WithoutTheMechanism()
    {
        var checkIn = new DateOnly(2026, 10, 10);
        var checkOut = new DateOnly(2026, 10, 15);
        var reasons = With(Free(checkIn, 10), UnavailableReason.Occupied, new DateOnly(2026, 10, 15));

        var message = AvailabilityService.Describe(AvailabilityService.Evaluate(checkIn, checkOut, reasons));

        Assert.Contains("tid att göra i ordning gården mellan två grupper", message);
        Assert.DoesNotContain("städning", message);
        Assert.Contains("15 oktober", message);
    }

    [Fact]
    public void DescribeForAdmin_StatesTheRule_WithoutTellingTheAdminToPickOtherDates()
    {
        var checkIn = new DateOnly(2026, 10, 10);
        var checkOut = new DateOnly(2026, 10, 15);
        var reasons = With(Free(checkIn, 10), UnavailableReason.Occupied, new DateOnly(2026, 10, 12));

        var message = AvailabilityService.DescribeForAdmin(AvailabilityService.Evaluate(checkIn, checkOut, reasons));

        Assert.Contains("en grupp i taget", message);
        Assert.DoesNotContain("Välj gärna", message);
        Assert.DoesNotContain("blockerad", message);
    }

    [Fact]
    public void DescribeForAdmin_SingleDate_UsesSingularSwedish()
    {
        var checkIn = new DateOnly(2026, 10, 10);
        var checkOut = new DateOnly(2026, 10, 15);
        var reasons = With(Free(checkIn, 10), UnavailableReason.Occupied, new DateOnly(2026, 10, 12));

        var message = AvailabilityService.DescribeForAdmin(AvailabilityService.Evaluate(checkIn, checkOut, reasons));

        Assert.StartsWith("Datumet 12 oktober är redan upptaget.", message);
    }

    /// <summary>
    /// A whole-property block is not another group. It used to be explained as one ("Gården hyrs ut
    /// till en grupp i taget"), sending the admin looking for a booking that did not exist.
    /// </summary>
    [Fact]
    public void DescribeForAdmin_WholePropertyBlock_NamesTheBlock_NotAGroup()
    {
        var checkIn = new DateOnly(2026, 10, 10);
        var checkOut = new DateOnly(2026, 10, 15);
        var reasons = With(Free(checkIn, 10), UnavailableReason.WholePropertyBlock,
            new DateOnly(2026, 10, 11), new DateOnly(2026, 10, 12));

        var message = AvailabilityService.DescribeForAdmin(AvailabilityService.Evaluate(checkIn, checkOut, reasons));

        Assert.Equal(
            "Hela anläggningen är blockerad 11–12 oktober. "
                + "Bokningen kan inte godkännas förrän blockeringen tas bort eller ändras.",
            message);
    }

    [Fact]
    public void DescribeForAdmin_BlockOnTheCleaningDays_SaysTheyMustBeFree()
    {
        var checkIn = new DateOnly(2026, 10, 10);
        var checkOut = new DateOnly(2026, 10, 15);
        // The nights are free; the block starts on check-out day, inside the stay's turnover window.
        var reasons = With(Free(checkIn, 10), UnavailableReason.WholePropertyBlock,
            new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 16));

        var message = AvailabilityService.DescribeForAdmin(AvailabilityService.Evaluate(checkIn, checkOut, reasons));

        Assert.Equal(
            "Hela anläggningen är blockerad 15–16 oktober, och städdagarna efter avresan behöver vara fria. "
                + "Bokningen kan inte godkännas förrän blockeringen tas bort eller ändras.",
            message);
    }

    [Fact]
    public void DescribeForAdmin_GroupAndBlockTogether_ReportsEachForItsOwnDates()
    {
        var checkIn = new DateOnly(2026, 10, 10);
        var checkOut = new DateOnly(2026, 10, 15);
        var reasons = With(Free(checkIn, 10), UnavailableReason.Occupied, new DateOnly(2026, 10, 10));
        With(reasons, UnavailableReason.WholePropertyBlock, new DateOnly(2026, 10, 13));

        var message = AvailabilityService.DescribeForAdmin(AvailabilityService.Evaluate(checkIn, checkOut, reasons));

        Assert.Contains("Datumet 10 oktober är redan upptaget.", message);
        Assert.Contains("Hela anläggningen är blockerad 13 oktober.", message);
        Assert.DoesNotContain("13 oktober är redan", message);
    }

    [Fact]
    public void DescribeForAdmin_DateHeldByBothAGroupAndABlock_IsReportedUnderBoth()
    {
        var checkIn = new DateOnly(2026, 10, 10);
        var checkOut = new DateOnly(2026, 10, 15);
        var reasons = With(Free(checkIn, 10),
            UnavailableReason.Occupied | UnavailableReason.WholePropertyBlock, new DateOnly(2026, 10, 12));

        var message = AvailabilityService.DescribeForAdmin(AvailabilityService.Evaluate(checkIn, checkOut, reasons));

        Assert.Contains("Datumet 12 oktober är redan upptaget.", message);
        Assert.Contains("Hela anläggningen är blockerad 12 oktober.", message);
    }

    /// <summary>The guest is never told about a block — "upptaget" is all they need (FR-09).</summary>
    [Fact]
    public void Describe_ForTheGuest_DoesNotMentionABlock()
    {
        var checkIn = new DateOnly(2026, 10, 10);
        var checkOut = new DateOnly(2026, 10, 15);
        var reasons = With(Free(checkIn, 10), UnavailableReason.WholePropertyBlock, new DateOnly(2026, 10, 12));

        var message = AvailabilityService.Describe(AvailabilityService.Evaluate(checkIn, checkOut, reasons));

        Assert.Contains("12 oktober är tyvärr upptaget", message);
        Assert.DoesNotContain("blocker", message);
    }

    [Fact]
    public void DescribePastDate_RefusesACheckInBeforeToday()
    {
        var today = new DateOnly(2026, 10, 10);

        Assert.NotNull(AvailabilityService.DescribePastDate(today.AddDays(-1), today));
        Assert.Contains("redan passerat", AvailabilityService.DescribePastDate(today.AddDays(-1), today)!);
    }

    [Fact]
    public void DescribePastDate_AllowsTodayAndTheFuture()
    {
        var today = new DateOnly(2026, 10, 10);

        Assert.Null(AvailabilityService.DescribePastDate(today, today));
        Assert.Null(AvailabilityService.DescribePastDate(today.AddDays(1), today));
    }

    /// <summary>
    /// The past-date rule is the public form's alone. The shared availability rule must stay silent
    /// about it, or approving a stay recorded after the fact (FR-112) — and marking one completed —
    /// would be blocked.
    /// </summary>
    [Fact]
    public void EvaluateStay_SaysNothingAboutDatesBeingInThePast()
    {
        var checkIn = new DateOnly(2020, 1, 1);
        var checkOut = new DateOnly(2020, 1, 4);

        Assert.True(AvailabilityService.Evaluate(checkIn, checkOut, Free(checkIn, 10)).IsAvailable);
    }

    [Fact]
    public void Describe_AvailableStay_HasNoMessage()
    {
        Assert.Equal(string.Empty, AvailabilityService.Describe(StayAvailability.Available));
        Assert.Equal(string.Empty, AvailabilityService.DescribeForAdmin(StayAvailability.Available));
    }
}
