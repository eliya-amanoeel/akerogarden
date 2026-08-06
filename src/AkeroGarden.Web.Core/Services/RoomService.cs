using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// CRUD and lifecycle rules for <see cref="Room"/> reference data
/// (FR-17–20, NFR-14). Business rules live here, not in the Razor component (NFR-26/27).
/// </summary>
public class RoomService(ApplicationDbContext db, IAuditWriter audit)
{
    /// <summary>All rooms in a building, active and inactive, in admin display order.</summary>
    public async Task<List<Room>> GetForBuildingAsync(Guid buildingId) =>
        await db.Rooms
            .Where(r => r.BuildingId == buildingId)
            .OrderBy(r => r.DisplayOrder)
            .ThenBy(r => r.Name)
            .ToListAsync();

    /// <summary>All rooms across every building, active and inactive, in admin display order —
    /// for the unified Byggnader tree (grouped in-memory by BuildingId rather than one round-trip
    /// per building, since the whole hierarchy renders on a single page load).</summary>
    public async Task<List<Room>> GetAllAsync() =>
        await db.Rooms
            .OrderBy(r => r.DisplayOrder)
            .ThenBy(r => r.Name)
            .ToListAsync();

    public async Task<Room?> GetAsync(Guid id) =>
        await db.Rooms.FindAsync(id);

    public async Task CreateAsync(Room room)
    {
        room.Id = Guid.NewGuid();
        db.Rooms.Add(room);
        await audit.RecordAsync(AuditAction.ReferenceDataCreated, nameof(Room), room.Id,
            $"Rum skapades: \"{room.Name}\".");
        await db.SaveChangesAsync();
    }

    /// <summary>Updates the editable fields of an existing room (FR-18). BuildingId is fixed.</summary>
    public async Task UpdateAsync(Room input)
    {
        var existing = await db.Rooms.FindAsync(input.Id);
        if (existing is null) return;

        existing.Name = input.Name;
        existing.BedCount = input.BedCount;
        existing.BedConfiguration = input.BedConfiguration;
        existing.DisplayOrder = input.DisplayOrder;
        await audit.RecordAsync(AuditAction.ReferenceDataUpdated, nameof(Room), existing.Id,
            $"Rum uppdaterades: \"{existing.Name}\".");
        await db.SaveChangesAsync();
    }

    /// <summary>Deactivates (soft) or reactivates a room without deleting history (FR-19, NFR-14).</summary>
    public async Task SetActiveAsync(Guid id, bool isActive)
    {
        var room = await db.Rooms.FindAsync(id);
        if (room is null) return;

        room.IsActive = isActive;
        await audit.RecordAsync(
            isActive ? AuditAction.ReferenceDataActivated : AuditAction.ReferenceDataDeactivated,
            nameof(Room), room.Id,
            $"Rum {(isActive ? "aktiverades" : "inaktiverades")}: \"{room.Name}\".");
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Hard-deletes a room only when nothing references it (FR-20); otherwise <see cref="HardDeleteResult.Blocked"/>.
    /// </summary>
    public async Task<HardDeleteResult> TryHardDeleteAsync(Guid id)
    {
        var room = await db.Rooms.FindAsync(id);
        if (room is null) return HardDeleteResult.NotFound;

        if (!CanHardDelete(await CountReferencesAsync(id)))
            return HardDeleteResult.Blocked;

        db.Rooms.Remove(room);
        await audit.RecordAsync(AuditAction.ReferenceDataDeleted, nameof(Room), room.Id,
            $"Rum togs bort: \"{room.Name}\".");
        await db.SaveChangesAsync();
        return HardDeleteResult.Deleted;
    }

    /// <summary>
    /// Pure guard decision (unit-testable, NFR-26): a room is hard-deletable
    /// only when nothing references it.
    /// </summary>
    public static bool CanHardDelete(int referenceCount) => referenceCount == 0;

    /// <summary>
    /// Counts references that block a hard-delete (FR-20): blocked periods scoped to this room.
    ///
    /// This guard previously counted participants assigned to the room via <c>Participant.RoomId</c>.
    /// That link was removed with per-participant room assignment (FR-103), which would have left the
    /// count at zero for ever — <see cref="CanHardDelete"/> always true, every room hard-deletable,
    /// and no test failing. FR-20 is therefore repointed at blocked-period integrity rather than
    /// tombstoned: <see cref="BlockedPeriod.RoomId"/> is a <b>logical reference, not a foreign key</b>
    /// (see ApplicationDbContext), so deleting a room silently orphans any room-scoped block pointing
    /// at it — a real hole that predates this change and was guarded by nothing.
    /// </summary>
    private Task<int> CountReferencesAsync(Guid roomId) =>
        db.BlockedPeriods.CountAsync(p => p.RoomId == roomId);
}
