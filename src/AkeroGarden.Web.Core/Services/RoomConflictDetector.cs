namespace AkeroGarden.Web.Core.Services;

/// <summary>A room assigned beyond its bed count for overlapping dates (FR-56) — surfaced in the
/// admin panel so a double-assignment can be corrected.</summary>
public readonly record struct RoomConflict(
    Guid RoomId, string RoomName, string BuildingName, int AssignedBeds, int BedCount);

/// <summary>
/// Pure, unit-testable (NFR-26) detection of room-assignment conflicts (FR-56): a room is in
/// conflict when the beds assigned to it — this booking's overnight participants plus any other
/// overlapping bookings' participants — exceed the room's bed count. Feeds the admin panel's
/// conflict flag (the panel wiring is slice 6).
/// </summary>
public static class RoomConflictDetector
{
    /// <param name="assignedBedsThisBooking">This booking's overnight participants per room (roomId → count).</param>
    /// <param name="roomsById">Room metadata (bed count + names) keyed by room id.</param>
    /// <param name="occupiedBedsByOthers">Beds other overlapping bookings use per room (roomId → count).</param>
    public static IReadOnlyList<RoomConflict> Detect(
        IReadOnlyDictionary<Guid, int> assignedBedsThisBooking,
        IReadOnlyDictionary<Guid, RoomInfo> roomsById,
        IReadOnlyDictionary<Guid, int> occupiedBedsByOthers)
    {
        var conflicts = new List<RoomConflict>();

        foreach (var (roomId, thisBooking) in assignedBedsThisBooking)
        {
            if (!roomsById.TryGetValue(roomId, out var room)) continue;

            var others = occupiedBedsByOthers.TryGetValue(roomId, out var o) ? o : 0;
            var total = thisBooking + others;
            if (total > room.BedCount)
                conflicts.Add(new RoomConflict(roomId, room.Name, room.BuildingName, total, room.BedCount));
        }

        return conflicts;
    }
}
