namespace AkeroGarden.Web.Core.Services;

/// <summary>The three public-calendar states for a single date (FR-09).</summary>
public enum AvailabilityState
{
    Free,
    PartiallyBooked,
    FullyBooked
}

/// <summary>
/// Pre-aggregated occupancy inputs for a single date, from which
/// <see cref="AvailabilityCalculator"/> derives the public state (FR-10).
/// The caller (a Phase 3 provider backed by real bookings + blocks) is responsible
/// for summing and scoping these values; the calculator itself is pure and stateless.
/// </summary>
/// <param name="BookedGuestCount">
/// Sum of <c>OvernightGuestCount</c> across all occupying bookings (Approved / DetailsComplete /
/// Active) whose range includes this date. Pending and terminal-status bookings are excluded
/// by the caller and never appear here.
/// </param>
/// <param name="TotalCapacity">Total property capacity in beds — the sum of active rooms' bed counts (79 today).</param>
/// <param name="BedsRemovedByPartialBlocks">
/// Beds removed by <c>Building</c>- or <c>Room</c>-scoped blocks active on this date. A whole-property
/// block is signalled separately by <paramref name="WholePropertyBlock"/>, not here.
/// </param>
/// <param name="WholePropertyBlock">True when a <c>WholeProperty</c> block covers this date.</param>
public readonly record struct DateAvailabilityInput(
    int BookedGuestCount,
    int TotalCapacity,
    int BedsRemovedByPartialBlocks,
    bool WholePropertyBlock);

/// <summary>
/// Pure computation of a date's public availability state from pre-aggregated occupancy
/// inputs (FR-10). No database, no rendering — this is the unit-tested core the Phase 3
/// calendar page consumes once real bookings and blocks exist.
/// </summary>
public static class AvailabilityCalculator
{
    /// <summary>
    /// Effective capacity for a date: total property capacity minus beds removed by
    /// partial (building/room) blocks, floored at zero (FR-10).
    /// </summary>
    public static int EffectiveCapacity(int totalCapacity, int bedsRemovedByPartialBlocks) =>
        Math.Max(0, totalCapacity - bedsRemovedByPartialBlocks);

    /// <summary>Derives the public state for a single date per FR-10's rules.</summary>
    public static AvailabilityState Evaluate(DateAvailabilityInput input)
    {
        // A whole-property block makes the date fully unavailable regardless of anything else.
        if (input.WholePropertyBlock)
            return AvailabilityState.FullyBooked;

        var effectiveCapacity = EffectiveCapacity(input.TotalCapacity, input.BedsRemovedByPartialBlocks);
        var hasPartialBlock = input.BedsRemovedByPartialBlocks > 0;
        var hasBooking = input.BookedGuestCount > 0;

        // Fully booked when booked guests reach effective capacity. This also covers the
        // degenerate case where partial blocks remove every bed (capacity 0 → 0 >= 0).
        if (input.BookedGuestCount >= effectiveCapacity)
            return AvailabilityState.FullyBooked;

        // Any occupying booking and/or partial block that is neither Free nor Full.
        if (hasBooking || hasPartialBlock)
            return AvailabilityState.PartiallyBooked;

        // No occupying bookings and no blocks.
        return AvailabilityState.Free;
    }
}
