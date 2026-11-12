using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the pure per-date occupancy aggregation (NFR-26, FR-10): which reasons hold a
/// date, check-in through check-out inclusive (FR-106), whole-property blocks, and
/// the deliberate silence of building/room blocks. Collapsing reasons to a state is
/// AvailabilityCalculatorTests; whether a whole requested stay fits is AvailabilityServiceTests.
/// </summary>
public class OccupancyProviderTests
{
    private static readonly Guid BuildingId = Guid.NewGuid();
    private static readonly Guid RoomId = Guid.NewGuid();

    private static Booking Occupying(DateOnly checkIn, DateOnly checkOut, int guests, BookingStatus status = BookingStatus.Approved) =>
        new() { Status = status, CheckIn = checkIn, CheckOut = checkOut, OvernightGuestCount = guests };

    private static BlockedPeriod Block(BlockScope scope, DateOnly start, DateOnly end) =>
        new()
        {
            Scope = scope,
            StartDate = start,
            EndDate = end,
            BuildingId = scope == BlockScope.Building ? BuildingId : null,
            RoomId = scope == BlockScope.Room ? RoomId : null
        };

    private static AvailabilityState StateOn(
        DateOnly date,
        IReadOnlyCollection<Booking> bookings,
        IReadOnlyCollection<BlockedPeriod> blocks) =>
        OccupancyProvider.ComputeStates(date, date, bookings, blocks)[date];

    private static UnavailableReason ReasonOn(
        DateOnly date,
        IReadOnlyCollection<Booking> bookings,
        IReadOnlyCollection<BlockedPeriod> blocks) =>
        OccupancyProvider.ComputeReasons(date, date, bookings, blocks)[date];

    [Fact]
    public void NoBookingsNoBlocks_IsFree()
    {
        var date = new DateOnly(2026, 6, 10);
        Assert.Equal(AvailabilityState.Free, StateOn(date, [], []));
    }

    /// <summary>
    /// The client's rule, asserted against real guest counts: the property is let to one group at a
    /// time, so a three-person group holds the date exactly as a full house does. This is the test
    /// that fails if anyone reintroduces a capacity comparison.
    /// </summary>
    [Theory]
    [InlineData(3)]
    [InlineData(40)]
    [InlineData(79)]
    public void AnyOccupyingBooking_HoldsTheDate_WhateverTheGroupSize(int guests)
    {
        var date = new DateOnly(2026, 6, 10);
        var bookings = new[] { Occupying(new(2026, 6, 9), new(2026, 6, 12), guests) };

        Assert.Equal(AvailabilityState.Unavailable, StateOn(date, bookings, []));
        Assert.Equal(UnavailableReason.Occupied, ReasonOn(date, bookings, []));
    }

    [Fact]
    public void CheckOutDay_IsHeld_AndTheDayAfterIsFree()
    {
        // A booking holds check-in through check-out, both inclusive (FR-106): the check-out day
        // sleeps nobody but is held for cleaning; the next day is free for the next group.
        var bookings = new[] { Occupying(new(2026, 6, 8), new(2026, 6, 10), 6) };

        Assert.Equal(UnavailableReason.Occupied, ReasonOn(new(2026, 6, 8), bookings, []));
        Assert.Equal(UnavailableReason.Occupied, ReasonOn(new(2026, 6, 10), bookings, []));
        Assert.Equal(UnavailableReason.None, ReasonOn(new(2026, 6, 11), bookings, []));
    }

    [Fact]
    public void StayCheckingOutOnTheRangesFirstDay_StillHoldsIt()
    {
        var bookings = new[] { Occupying(new(2026, 6, 5), new(2026, 6, 10), 6) };
        var states = OccupancyProvider.ComputeStates(new(2026, 6, 10), new(2026, 6, 11), bookings, []);

        Assert.Equal(AvailabilityState.Unavailable, states[new(2026, 6, 10)]);
        Assert.Equal(AvailabilityState.Free, states[new(2026, 6, 11)]);
    }

    /// <summary>
    /// The client's example (2026-11-11): a group leaving on Sunday, the next arriving on Monday —
    /// back to back, with no free day between them.
    /// </summary>
    [Fact]
    public void NextGroupArrivingTheDayAfterCheckOut_LeavesNoFreeDayBetween()
    {
        var bookings = new[]
        {
            Occupying(new(2026, 6, 5), new(2026, 6, 7), 10), // leaves Sunday 7 June
            Occupying(new(2026, 6, 8), new(2026, 6, 10), 10) // arrives Monday 8 June
        };
        var states = OccupancyProvider.ComputeStates(new(2026, 6, 7), new(2026, 6, 8), bookings, []);

        Assert.Equal(AvailabilityState.Unavailable, states[new(2026, 6, 7)]);
        Assert.Equal(AvailabilityState.Unavailable, states[new(2026, 6, 8)]);
    }

    /// <summary>
    /// The status filter lives in <c>GetReasonsAsync</c>, not in the pure function — so the honest
    /// unit-level claim is only that these statuses are absent from the occupying set. That the
    /// filter is actually applied (a Pending booking holding not even its check-out day) is
    /// pinned end to end by OccupancyProviderIntegrationTests against a real database.
    /// </summary>
    [Theory]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.Denied)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Completed)]
    [InlineData(BookingStatus.Closed)]
    public void NonOccupyingStatuses_AreNotInTheOccupyingSet(BookingStatus status)
    {
        Assert.DoesNotContain(status, OccupancyProvider.OccupyingStatuses);
    }

    /// <summary>A booking waiting for its deposit holds its dates: the group was told they are
    /// reserved for them while they pay (FR-10, FR-115). Same honest limit as above — the filter
    /// being applied is pinned by the integration tests.</summary>
    [Theory]
    [InlineData(BookingStatus.AwaitingDeposit)]
    [InlineData(BookingStatus.Approved)]
    [InlineData(BookingStatus.DetailsComplete)]
    [InlineData(BookingStatus.Active)]
    public void OccupyingStatuses_AreExactlyTheLiveConfirmedOrHeldOnes(BookingStatus status)
    {
        Assert.Contains(status, OccupancyProvider.OccupyingStatuses);
        Assert.Equal(4, OccupancyProvider.OccupyingStatuses.Length);
    }

    [Fact]
    public void WholePropertyBlock_HoldsTheDate_WithNoBookings()
    {
        var date = new DateOnly(2026, 6, 10);
        var blocks = new[] { Block(BlockScope.WholeProperty, new(2026, 6, 9), new(2026, 6, 11)) };

        Assert.Equal(UnavailableReason.WholePropertyBlock, ReasonOn(date, [], blocks));
    }

    /// <summary>
    /// A building or room out of service does not close the property. With no partial state left to
    /// express it in, the public calendar says nothing — the block's real effect is the assignment
    /// veto (FR-102), which <c>BuildingExclusionCalculator</c> owns and this deliberately does not
    /// duplicate.
    /// </summary>
    [Theory]
    [InlineData(BlockScope.Building)]
    [InlineData(BlockScope.Room)]
    public void PartialBlocks_DoNotReachThePublicCalendar(BlockScope scope)
    {
        var date = new DateOnly(2026, 6, 10);
        var blocks = new[] { Block(scope, new(2026, 6, 9), new(2026, 6, 11)) };

        Assert.Equal(UnavailableReason.None, ReasonOn(date, [], blocks));
        Assert.Equal(AvailabilityState.Free, StateOn(date, [], blocks));
    }

    [Fact]
    public void OccupiedAndBlocked_CarryBothReasons()
    {
        var date = new DateOnly(2026, 6, 10);
        var bookings = new[] { Occupying(new(2026, 6, 9), new(2026, 6, 12), 5) };
        var blocks = new[] { Block(BlockScope.WholeProperty, new(2026, 6, 10), new(2026, 6, 10)) };

        Assert.Equal(
            UnavailableReason.Occupied | UnavailableReason.WholePropertyBlock,
            ReasonOn(date, bookings, blocks));
    }

    [Fact]
    public void ComputeStates_CoversEveryDateInRange()
    {
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 6, 30);
        var states = OccupancyProvider.ComputeStates(from, to, [], []);

        Assert.Equal(30, states.Count);
        for (var d = from; d <= to; d = d.AddDays(1))
            Assert.True(states.ContainsKey(d));
    }
}
