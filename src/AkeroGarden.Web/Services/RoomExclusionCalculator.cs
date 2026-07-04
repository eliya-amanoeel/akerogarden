using AkeroGarden.Web.Data.Entities;

namespace AkeroGarden.Web.Services;

/// <summary>A room plus the building it belongs to — the minimal projection the room-assignment
/// logic needs (kept free of EF so the calculators are pure/unit-testable, NFR-26).</summary>
public readonly record struct RoomInfo(Guid Id, string Name, int BedCount, Guid BuildingId, string BuildingName);

/// <summary>Why a room is not offered on the completion form's room picker (FR-56).</summary>
public enum RoomUnavailableReason
{
    Available,
    Blocked,
    FullyOccupied
}

/// <summary>A room's availability for a booking's dates, with the reason when unavailable.</summary>
public readonly record struct RoomAvailability(RoomInfo Room, bool IsAvailable, RoomUnavailableReason Reason);

/// <summary>
/// Pure, unit-testable (NFR-26) computation of which rooms the completion form may offer for a
/// booking's dates (FR-56, §3.3): a room is excluded if a whole-property / building / room block
/// overlaps the stay, or if other overlapping bookings already fully occupy it (occupied beds ≥ its
/// bed count, via the live <see cref="Participant.RoomId"/> link, §8.5). The caller pre-filters
/// blocks and occupancy to the booking's date window; this class applies the scope + capacity rules.
/// </summary>
public static class RoomExclusionCalculator
{
    /// <summary>
    /// Evaluates every supplied room. <paramref name="rooms"/> should already be active rooms in
    /// active buildings; <paramref name="overlappingBlocks"/> the blocks overlapping the stay; and
    /// <paramref name="occupiedBedsByOthers"/> the bed count other overlapping bookings already use
    /// per room.
    /// </summary>
    public static IReadOnlyList<RoomAvailability> Evaluate(
        IReadOnlyCollection<RoomInfo> rooms,
        IReadOnlyCollection<BlockedPeriod> overlappingBlocks,
        IReadOnlyDictionary<Guid, int> occupiedBedsByOthers)
    {
        var wholeProperty = overlappingBlocks.Any(b => b.Scope == BlockScope.WholeProperty);
        var blockedBuildings = overlappingBlocks
            .Where(b => b.Scope == BlockScope.Building && b.BuildingId is not null)
            .Select(b => b.BuildingId!.Value)
            .ToHashSet();
        var blockedRooms = overlappingBlocks
            .Where(b => b.Scope == BlockScope.Room && b.RoomId is not null)
            .Select(b => b.RoomId!.Value)
            .ToHashSet();

        var result = new List<RoomAvailability>(rooms.Count);
        foreach (var room in rooms)
        {
            var reason = RoomUnavailableReason.Available;

            if (wholeProperty || blockedBuildings.Contains(room.BuildingId) || blockedRooms.Contains(room.Id))
                reason = RoomUnavailableReason.Blocked;
            else if (occupiedBedsByOthers.TryGetValue(room.Id, out var occupied) && occupied >= room.BedCount)
                reason = RoomUnavailableReason.FullyOccupied;

            result.Add(new RoomAvailability(room, reason == RoomUnavailableReason.Available, reason));
        }

        return result;
    }

    /// <summary>The available rooms only — what the picker offers.</summary>
    public static IReadOnlyList<RoomInfo> AvailableRooms(
        IReadOnlyCollection<RoomInfo> rooms,
        IReadOnlyCollection<BlockedPeriod> overlappingBlocks,
        IReadOnlyDictionary<Guid, int> occupiedBedsByOthers) =>
        Evaluate(rooms, overlappingBlocks, occupiedBedsByOthers)
            .Where(r => r.IsAvailable)
            .Select(r => r.Room)
            .ToList();
}
