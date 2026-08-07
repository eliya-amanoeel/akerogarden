namespace AkeroGarden.Web.Core.Services;

/// <summary>The three public-calendar states for a single date (FR-09).</summary>
public enum AvailabilityState
{
    Free,
    PartiallyBooked,
    FullyBooked
}

/// <summary>
/// Why a date is unavailable regardless of how many beds are free — the reasons that cannot be
/// expressed as a guest count (FR-10).
///
/// <para>Modelled as a set rather than a single flag deliberately. Reasons of this kind are not
/// interchangeable with occupancy: a ten-person group's turnover days would read *partially*
/// booked if turnover were smuggled in as headcount, and inflating the count to fake a full
/// property would put fiction in the calculation. Adding a future reason is additive here —
/// <see cref="AvailabilityCalculator.Evaluate"/> asks whether *any* reason applies and never has
/// to be reopened.</para>
/// </summary>
[Flags]
public enum UnavailableReason
{
    /// <summary>Nothing forces the date unavailable; state follows from guests against capacity.</summary>
    None = 0,

    /// <summary>A <c>WholeProperty</c> block covers the date (FR-21).</summary>
    WholePropertyBlock = 1,

    /// <summary>The date falls in an occupying booking's cleaning/turnover window (FR-106).</summary>
    Turnover = 2
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
/// Beds removed by <c>Building</c>- or <c>Room</c>-scoped blocks active on this date. Reasons that
/// force the date unavailable outright are signalled by <paramref name="Unavailable"/>, not here.
/// </param>
/// <param name="Unavailable">
/// The set of reasons forcing this date unavailable irrespective of headcount — a whole-property
/// block, a turnover window, or both. <see cref="UnavailableReason.None"/> when neither applies.
/// </param>
public readonly record struct DateAvailabilityInput(
    int BookedGuestCount,
    int TotalCapacity,
    int BedsRemovedByPartialBlocks,
    UnavailableReason Unavailable);

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
        // Any reason forcing unavailability wins outright, before beds are ever compared —
        // this is what makes a small group's turnover days read *fully* booked (FR-106).
        if (input.Unavailable != UnavailableReason.None)
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
