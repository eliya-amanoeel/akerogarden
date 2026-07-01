using AkeroGarden.Web.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the pure availability-state computation (NFR-26, FR-10). Exercises the
/// state boundaries — Free / Partially booked / Fully booked — across capacity, partial
/// blocks, and whole-property blocks. This is the Phase 2 core; the Phase 3 calendar page
/// wires real bookings/blocks into it unchanged.
/// </summary>
public class AvailabilityCalculatorTests
{
    private const int Capacity = 79; // total property beds today (§3.1)

    private static AvailabilityState Evaluate(
        int booked, int totalCapacity = Capacity, int partialBlockBeds = 0, bool wholeBlock = false) =>
        AvailabilityCalculator.Evaluate(
            new DateAvailabilityInput(booked, totalCapacity, partialBlockBeds, wholeBlock));

    [Fact]
    public void EmptyDate_NoBookingsNoBlocks_IsFree()
    {
        Assert.Equal(AvailabilityState.Free, Evaluate(booked: 0));
    }

    [Fact]
    public void OneGuest_IsPartiallyBooked()
    {
        Assert.Equal(AvailabilityState.PartiallyBooked, Evaluate(booked: 1));
    }

    [Fact]
    public void CapacityMinusOne_IsPartiallyBooked()
    {
        Assert.Equal(AvailabilityState.PartiallyBooked, Evaluate(booked: Capacity - 1));
    }

    [Fact]
    public void ExactlyFull_ReachesCapacity_IsFullyBooked()
    {
        Assert.Equal(AvailabilityState.FullyBooked, Evaluate(booked: Capacity));
    }

    [Fact]
    public void OverCapacity_IsFullyBooked()
    {
        // Should not happen in practice, but the boundary is ">=", so guard it.
        Assert.Equal(AvailabilityState.FullyBooked, Evaluate(booked: Capacity + 5));
    }

    [Fact]
    public void WholePropertyBlock_IsFullyBooked_EvenWithNoBookings()
    {
        Assert.Equal(AvailabilityState.FullyBooked, Evaluate(booked: 0, wholeBlock: true));
    }

    [Fact]
    public void WholePropertyBlock_OverridesEverything()
    {
        Assert.Equal(AvailabilityState.FullyBooked, Evaluate(booked: 3, partialBlockBeds: 10, wholeBlock: true));
    }

    [Fact]
    public void PartialBlockOnly_NoBookings_IsPartiallyBooked()
    {
        // A block exists, so the date is not Free; capacity remains > booked, so not Full.
        Assert.Equal(AvailabilityState.PartiallyBooked, Evaluate(booked: 0, partialBlockBeds: 10));
    }

    [Fact]
    public void PartialBlock_ReducesEffectiveCapacity_SoFewerGuestsFillIt()
    {
        // 79 total − 70 blocked = 9 effective; 9 booked reaches it → Fully booked.
        Assert.Equal(AvailabilityState.FullyBooked, Evaluate(booked: 9, partialBlockBeds: 70));
        // 8 booked against 9 effective → still Partially booked.
        Assert.Equal(AvailabilityState.PartiallyBooked, Evaluate(booked: 8, partialBlockBeds: 70));
    }

    [Fact]
    public void AllBedsBlockedByPartialBlocks_IsFullyBooked()
    {
        // Every bed removed → effective capacity 0 → 0 >= 0 → Fully booked.
        Assert.Equal(AvailabilityState.FullyBooked, Evaluate(booked: 0, partialBlockBeds: Capacity));
    }

    [Fact]
    public void PartialBlocksExceedingCapacity_ClampToZero_IsFullyBooked()
    {
        Assert.Equal(AvailabilityState.FullyBooked, Evaluate(booked: 0, partialBlockBeds: Capacity + 100));
    }

    [Theory]
    [InlineData(79, 0, 79)]
    [InlineData(79, 10, 69)]
    [InlineData(79, 79, 0)]
    [InlineData(79, 100, 0)] // clamped, never negative
    public void EffectiveCapacity_SubtractsPartialBlocks_FlooredAtZero(int total, int removed, int expected)
    {
        Assert.Equal(expected, AvailabilityCalculator.EffectiveCapacity(total, removed));
    }
}
