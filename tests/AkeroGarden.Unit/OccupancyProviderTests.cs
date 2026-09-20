using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the pure per-date occupancy aggregation (NFR-26, FR-10): which reasons hold a
/// date, nightly (check-out-exclusive) semantics, the turnover window, whole-property blocks, and
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
    public void CheckOutDay_HoldsNoGuests_ButIsHeldForTurnover()
    {
        // Nightly semantics: the check-out day counts no guests, so Occupied is absent — and the
        // day is still unavailable, because it opens the turnover window (FR-106).
        var bookings = new[] { Occupying(new(2026, 6, 8), new(2026, 6, 10), 2) };

        Assert.Equal(UnavailableReason.Occupied, ReasonOn(new(2026, 6, 9), bookings, []));
        Assert.Equal(UnavailableReason.Turnover, ReasonOn(new(2026, 6, 10), bookings, []));
    }

    [Fact]
    public void TurnoverExtendsOneDayBeyondCheckOut_ThenTheDateIsFreeAgain()
    {
        var bookings = new[] { Occupying(new(2026, 6, 8), new(2026, 6, 10), 6) };

        Assert.Equal(AvailabilityState.Unavailable, StateOn(new(2026, 6, 11), bookings, []));
        Assert.Equal(AvailabilityState.Free, StateOn(new(2026, 6, 12), bookings, []));
    }

    [Fact]
    public void StayEndingBeforeTheRange_StillHoldsTheRangesFirstDay()
    {
        // The stay ends the day before the window opens; its turnover still reaches inside.
        var bookings = new[] { Occupying(new(2026, 6, 5), new(2026, 6, 9), 6) };
        var states = OccupancyProvider.ComputeStates(new(2026, 6, 10), new(2026, 6, 11), bookings, []);

        Assert.Equal(AvailabilityState.Unavailable, states[new(2026, 6, 10)]);
        Assert.Equal(AvailabilityState.Free, states[new(2026, 6, 11)]);
    }

    [Fact]
    public void TurnoverWindowsOfAdjacentBookings_DoNotFreeEachOther()
    {
        var bookings = new[]
        {
            Occupying(new(2026, 6, 1), new(2026, 6, 5), 10),
            Occupying(new(2026, 6, 7), new(2026, 6, 10), 10)
        };
        var states = OccupancyProvider.ComputeStates(new(2026, 6, 5), new(2026, 6, 7), bookings, []);

        Assert.Equal(AvailabilityState.Unavailable, states[new(2026, 6, 5)]); // first booking's turnover
        Assert.Equal(AvailabilityState.Unavailable, states[new(2026, 6, 6)]); // its second turnover day
        Assert.Equal(AvailabilityState.Unavailable, states[new(2026, 6, 7)]); // second booking's first night
    }

    /// <summary>
    /// The status filter lives in <c>GetReasonsAsync</c>, not in the pure function — so the honest
    /// unit-level claim is only that these statuses are absent from the occupying set. That the
    /// filter is actually applied (a Pending booking creating no turnover window, in particular) is
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
