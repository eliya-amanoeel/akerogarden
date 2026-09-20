using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Resolves which dates are unavailable and why (FR-08–10), feeding real bookings and blocks into
/// the pure <see cref="AvailabilityCalculator"/>. Every surface that needs to know whether a date
/// is free reads it from here — the public calendar, the inquiry form and the approval path — so
/// the rule exists once (see <see cref="AvailabilityService"/> for the range-level question).
///
/// <para>Occupancy uses nightly semantics: a stay occupies the nights from check-in up to, but not
/// including, check-out. The check-out date holds no guests — but it is not bookable, because it
/// opens the booking's turnover window (<see cref="TurnoverWindow"/>, FR-106). Blocks are
/// inclusive of both their start and end dates.</para>
///
/// <para><b>Only whole-property blocks reach the public calendar.</b> A Building- or Room-scoped
/// block takes lodging out of service without closing the property, and its real effect is the
/// assignment veto in <see cref="BuildingExclusionCalculator"/> (FR-102). Since the calendar has
/// no "partly available" state to express it in, showing such a block would have to either close a
/// date the property can still host on, or say nothing — and saying nothing is the true one.</para>
/// </summary>
public class OccupancyProvider(ApplicationDbContext db)
{
    /// <summary>Statuses whose bookings occupy the property on the public calendar (FR-10).</summary>
    public static readonly BookingStatus[] OccupyingStatuses =
        [BookingStatus.Approved, BookingStatus.DetailsComplete, BookingStatus.Active];

    /// <summary>
    /// Computes the availability state for every date in <c>[from, to]</c> from real data.
    /// </summary>
    public async Task<IReadOnlyDictionary<DateOnly, AvailabilityState>> GetStatesAsync(DateOnly from, DateOnly to)
    {
        var reasons = await GetReasonsAsync(from, to);
        return reasons.ToDictionary(kv => kv.Key, kv => AvailabilityCalculator.Evaluate(kv.Value));
    }

    /// <summary>
    /// The unavailability reasons for every date in <c>[from, to]</c> — the same computation as
    /// <see cref="GetStatesAsync"/> before the state is collapsed out of it. Callers that must
    /// explain *why* a date is unavailable read this.
    /// </summary>
    /// <param name="excludeBookingId">
    /// A booking to leave out of the occupancy scan — used when evaluating a booking against the
    /// calendar it is itself part of, so it cannot collide with its own nights.
    /// </param>
    public async Task<IReadOnlyDictionary<DateOnly, UnavailableReason>> GetReasonsAsync(
        DateOnly from,
        DateOnly to,
        Guid? excludeBookingId = null)
    {
        // Reach back past `from`: a stay that ended just before the window still holds turnover
        // days inside it (FR-106). Loading only the stays overlapping [from, to] — the old
        // `CheckOut > from` — would silently free the window's first days.
        var earliestCheckOut = TurnoverWindow.EarliestCheckOutAffecting(from);

        var occupying = await db.Bookings
            .Where(b => OccupyingStatuses.Contains(b.Status)
                && b.CheckIn != null && b.CheckOut != null
                && b.CheckIn <= to && b.CheckOut >= earliestCheckOut
                && (excludeBookingId == null || b.Id != excludeBookingId))
            .ToListAsync();

        var blocks = await db.BlockedPeriods
            .Where(p => p.Scope == BlockScope.WholeProperty && p.StartDate <= to && p.EndDate >= from)
            .ToListAsync();

        return ComputeReasons(from, to, occupying, blocks);
    }

    /// <summary>
    /// Pure per-date state computation (unit-testable, NFR-26).
    /// </summary>
    public static IReadOnlyDictionary<DateOnly, AvailabilityState> ComputeStates(
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<Booking> occupyingBookings,
        IReadOnlyCollection<BlockedPeriod> blocks) =>
        ComputeReasons(from, to, occupyingBookings, blocks)
            .ToDictionary(kv => kv.Key, kv => AvailabilityCalculator.Evaluate(kv.Value));

    /// <summary>
    /// Pure per-date reason computation (unit-testable, NFR-26). Blocks of a scope other than
    /// <see cref="BlockScope.WholeProperty"/> are ignored here — see the note on the class.
    /// </summary>
    public static IReadOnlyDictionary<DateOnly, UnavailableReason> ComputeReasons(
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<Booking> occupyingBookings,
        IReadOnlyCollection<BlockedPeriod> blocks)
    {
        var reasons = new Dictionary<DateOnly, UnavailableReason>();

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var reason = UnavailableReason.None;

            if (blocks.Any(b => b.Scope == BlockScope.WholeProperty && b.StartDate <= date && b.EndDate >= date))
                reason |= UnavailableReason.WholePropertyBlock;

            if (occupyingBookings.Any(b => b.CheckOut is { } checkOut && TurnoverWindow.Covers(checkOut, date)))
                reason |= UnavailableReason.Turnover;

            // Nightly occupancy: check-in inclusive, check-out exclusive. The guest count is not
            // consulted — one group holds the property (see UnavailableReason).
            if (occupyingBookings.Any(b => b.CheckIn <= date && b.CheckOut > date))
                reason |= UnavailableReason.Occupied;

            reasons[date] = reason;
        }

        return reasons;
    }
}
