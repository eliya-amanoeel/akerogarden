using AkeroGarden.Web.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Feeds the pure room-assignment logic (<see cref="RoomExclusionCalculator"/>,
/// <see cref="RoomConflictDetector"/>) with real rooms, blocks, and cross-booking occupancy for a
/// booking's dates (FR-56, §3.3). Occupancy uses nightly semantics (check-in inclusive, check-out
/// exclusive) via the live <see cref="AkeroGarden.Web.Core.Data.Entities.Participant.RoomId"/> link.
/// </summary>
public class RoomAvailabilityService(ApplicationDbContext db)
{
    /// <summary>Rooms the completion form may offer for a booking's dates (blocked and
    /// fully-occupied rooms excluded). Empty if the booking has no dates.</summary>
    public async Task<IReadOnlyList<RoomInfo>> GetAvailableRoomsAsync(Guid bookingId)
    {
        var booking = await db.Bookings.FindAsync(bookingId);
        if (booking?.CheckIn is not { } checkIn || booking.CheckOut is not { } checkOut)
            return [];

        var rooms = await LoadRoomsAsync();
        var blocks = await db.BlockedPeriods
            .Where(p => p.StartDate < checkOut && p.EndDate >= checkIn)
            .ToListAsync();
        var occupiedByOthers = await LoadOccupiedBedsByOthersAsync(bookingId, checkIn, checkOut);

        return RoomExclusionCalculator.AvailableRooms(rooms, blocks, occupiedByOthers);
    }

    /// <summary>All active rooms (in active buildings). The admin room-assignment dropdown offers
    /// these — the admin may override into any room, with conflicts flagged (FR-56).</summary>
    public async Task<IReadOnlyList<RoomInfo>> GetAllActiveRoomsAsync() => await LoadRoomsAsync();

    /// <summary>Room-assignment conflicts for a booking (a room assigned beyond its bed count over
    /// overlapping dates, FR-56) — surfaced in the admin panel (slice 6).</summary>
    public async Task<IReadOnlyList<RoomConflict>> GetConflictsAsync(Guid bookingId)
    {
        var booking = await db.Bookings.FindAsync(bookingId);
        if (booking?.CheckIn is not { } checkIn || booking.CheckOut is not { } checkOut)
            return [];

        var roomsById = (await LoadRoomsAsync()).ToDictionary(r => r.Id);
        var occupiedByOthers = await LoadOccupiedBedsByOthersAsync(bookingId, checkIn, checkOut);

        var assignedThisBooking = (await db.Participants
                .Where(p => p.BookingId == bookingId && p.RoomId != null)
                .GroupBy(p => p.RoomId!.Value)
                .Select(g => new { RoomId = g.Key, Count = g.Count() })
                .ToListAsync())
            .ToDictionary(x => x.RoomId, x => x.Count);

        return RoomConflictDetector.Detect(assignedThisBooking, roomsById, occupiedByOthers);
    }

    private async Task<List<RoomInfo>> LoadRoomsAsync()
    {
        // Project to an anonymous type (EF-translatable) then build the RoomInfo struct in memory —
        // EF cannot translate the record-struct constructor into SQL.
        var rows = await db.Rooms
            .Where(r => r.IsActive)
            .Join(db.Buildings.Where(b => b.IsActive),
                r => r.BuildingId, b => b.Id,
                (r, b) => new { r.Id, r.Name, r.BedCount, BuildingId = b.Id, BuildingName = b.Name })
            .ToListAsync();

        return rows
            .Select(x => new RoomInfo(x.Id, x.Name, x.BedCount, x.BuildingId, x.BuildingName))
            .OrderBy(r => r.BuildingName)
            .ThenBy(r => r.Name)
            .ToList();
    }

    /// <summary>Beds used per room by <em>other</em> overlapping occupying bookings (nightly overlap).</summary>
    private async Task<Dictionary<Guid, int>> LoadOccupiedBedsByOthersAsync(
        Guid bookingId, DateOnly checkIn, DateOnly checkOut) =>
        (await db.Participants
            .Where(p => p.RoomId != null
                && p.BookingId != bookingId
                && p.Booking != null
                && OccupancyProvider.OccupyingStatuses.Contains(p.Booking.Status)
                && p.Booking.CheckIn != null && p.Booking.CheckOut != null
                && p.Booking.CheckIn < checkOut && p.Booking.CheckOut > checkIn)
            .GroupBy(p => p.RoomId!.Value)
            .Select(g => new { RoomId = g.Key, Count = g.Count() })
            .ToListAsync())
        .ToDictionary(x => x.RoomId, x => x.Count);
}
