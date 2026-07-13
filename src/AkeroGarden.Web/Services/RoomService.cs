using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Services;

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
    /// Counts references that block a hard-delete (FR-20): participants assigned to this room via
    /// the live <see cref="Participant.RoomId"/> link (§8.5). Wired in Phase 4 now that Participant
    /// exists.
    /// </summary>
    private Task<int> CountReferencesAsync(Guid roomId) =>
        db.Participants.CountAsync(p => p.RoomId == roomId);
}
