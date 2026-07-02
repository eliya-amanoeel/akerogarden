using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Services;

/// <summary>
/// CRUD and lifecycle rules for <see cref="Building"/> reference data
/// (FR-13–16, NFR-14). Business rules live here, not in the Razor component,
/// so the deactivate / no-hard-delete rules are testable (NFR-26/27).
/// </summary>
public class BuildingService(ApplicationDbContext db)
{
    /// <summary>All buildings, active and inactive, in admin display order.</summary>
    public async Task<List<Building>> GetAllAsync() =>
        await db.Buildings
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.Name)
            .ToListAsync();

    public async Task<Building?> GetAsync(Guid id) =>
        await db.Buildings.FindAsync(id);

    public async Task CreateAsync(Building building)
    {
        building.Id = Guid.NewGuid();
        db.Buildings.Add(building);
        await db.SaveChangesAsync();
    }

    /// <summary>Updates the editable fields of an existing building (FR-14).</summary>
    public async Task UpdateAsync(Building input)
    {
        var existing = await db.Buildings.FindAsync(input.Id);
        if (existing is null) return;

        existing.Name = input.Name;
        existing.Description = input.Description;
        existing.SharedFacilities = input.SharedFacilities;
        existing.IsAccessible = input.IsAccessible;
        existing.DisplayOrder = input.DisplayOrder;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Deactivates (soft) or reactivates a building without deleting history (FR-15, NFR-14).
    /// </summary>
    public async Task SetActiveAsync(Guid id, bool isActive)
    {
        var building = await db.Buildings.FindAsync(id);
        if (building is null) return;

        building.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Hard-deletes a building only when nothing references it (FR-16); otherwise <see cref="HardDeleteResult.Blocked"/>.
    /// </summary>
    public async Task<HardDeleteResult> TryHardDeleteAsync(Guid id)
    {
        var building = await db.Buildings.FindAsync(id);
        if (building is null) return HardDeleteResult.NotFound;

        if (!CanHardDelete(await CountReferencesAsync(id)))
            return HardDeleteResult.Blocked;

        db.Buildings.Remove(building);
        await db.SaveChangesAsync();
        return HardDeleteResult.Deleted;
    }

    /// <summary>
    /// Pure guard decision (unit-testable, NFR-26): a building is hard-deletable
    /// only when nothing references it.
    /// </summary>
    public static bool CanHardDelete(int referenceCount) => referenceCount == 0;

    /// <summary>
    /// Counts references that block a hard-delete (FR-16). Two-layered: rooms in the building
    /// (Phase 1), plus the transitive check added in Phase 4 — participants assigned to any of
    /// those rooms via the live <see cref="Participant.RoomId"/> link (§8.5). Either being non-zero
    /// blocks the delete.
    /// </summary>
    private async Task<int> CountReferencesAsync(Guid buildingId)
    {
        var roomCount = await db.Rooms.CountAsync(r => r.BuildingId == buildingId);
        var participantCount = await db.Participants
            .CountAsync(p => p.Room != null && p.Room.BuildingId == buildingId);
        return roomCount + participantCount;
    }
}
