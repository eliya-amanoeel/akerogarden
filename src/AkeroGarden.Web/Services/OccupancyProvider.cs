using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Services;

/// <summary>
/// Supplies the public availability calendar (FR-08–10) with a per-date state by feeding real
/// bookings and blocks into the already-unit-tested <see cref="AvailabilityCalculator"/> — the
/// deliberate Phase 2 → Phase 3 seam (the calculator is not reimplemented here).
///
/// <para>Occupancy uses nightly semantics: a stay occupies the nights from check-in up to, but
/// not including, check-out (guests leave at 12:00, so the check-out date's bed is free). Blocks
/// are inclusive of both their start and end dates.</para>
/// </summary>
public class OccupancyProvider(ApplicationDbContext db)
{
    /// <summary>Statuses whose bookings occupy beds on the public calendar (FR-10).</summary>
    public static readonly BookingStatus[] OccupyingStatuses =
        [BookingStatus.Approved, BookingStatus.DetailsComplete, BookingStatus.Active];

    /// <summary>
    /// Computes the availability state for every date in <c>[from, to]</c> from real data.
    /// Loads active rooms, occupying bookings, and blocks overlapping the window, then defers to
    /// the pure <see cref="ComputeStates"/>.
    /// </summary>
    public async Task<IReadOnlyDictionary<DateOnly, AvailabilityState>> GetStatesAsync(DateOnly from, DateOnly to)
    {
        var activeRooms = await db.Rooms.Where(r => r.IsActive).ToListAsync();

        var occupying = await db.Bookings
            .Where(b => OccupyingStatuses.Contains(b.Status)
                && b.CheckIn != null && b.CheckOut != null
                && b.CheckIn <= to && b.CheckOut > from)
            .ToListAsync();

        var blocks = await db.BlockedPeriods
            .Where(p => p.StartDate <= to && p.EndDate >= from)
            .ToListAsync();

        return ComputeStates(from, to, activeRooms, occupying, blocks);
    }

    /// <summary>
    /// Pure per-date state computation (unit-testable, NFR-26). Effective capacity is total
    /// active-room beds minus beds removed by building/room blocks active that day; occupancy is
    /// summed overnight guest counts; the result is delegated to <see cref="AvailabilityCalculator"/>.
    /// </summary>
    public static IReadOnlyDictionary<DateOnly, AvailabilityState> ComputeStates(
        DateOnly from,
        DateOnly to,
        IReadOnlyCollection<Room> activeRooms,
        IReadOnlyCollection<Booking> occupyingBookings,
        IReadOnlyCollection<BlockedPeriod> blocks)
    {
        var totalCapacity = activeRooms.Sum(r => r.BedCount);
        var bedsByRoom = activeRooms.ToDictionary(r => r.Id, r => r.BedCount);
        var roomIdsByBuilding = activeRooms
            .GroupBy(r => r.BuildingId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Id).ToList());

        var states = new Dictionary<DateOnly, AvailabilityState>();

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var activeBlocks = blocks.Where(b => b.StartDate <= date && b.EndDate >= date).ToList();
            var wholeProperty = activeBlocks.Any(b => b.Scope == BlockScope.WholeProperty);

            // Union the rooms taken out by building/room blocks so overlapping blocks don't
            // double-count a room's beds.
            var blockedRoomIds = new HashSet<Guid>();
            foreach (var b in activeBlocks)
            {
                if (b.Scope == BlockScope.Building && b.BuildingId is not null
                    && roomIdsByBuilding.TryGetValue(b.BuildingId.Value, out var ids))
                    blockedRoomIds.UnionWith(ids);
                else if (b.Scope == BlockScope.Room && b.RoomId is not null)
                    blockedRoomIds.Add(b.RoomId.Value);
            }
            var bedsRemoved = blockedRoomIds.Where(bedsByRoom.ContainsKey).Sum(id => bedsByRoom[id]);

            // Nightly occupancy: check-in inclusive, check-out exclusive.
            var booked = occupyingBookings
                .Where(b => b.CheckIn <= date && b.CheckOut > date)
                .Sum(b => b.OvernightGuestCount ?? 0);

            states[date] = AvailabilityCalculator.Evaluate(
                new DateAvailabilityInput(booked, totalCapacity, bedsRemoved, wholeProperty));
        }

        return states;
    }
}
