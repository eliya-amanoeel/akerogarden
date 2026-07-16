using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// CRUD and lifecycle rules for <see cref="PhysicalBuilding"/> reference data (requirements.md
/// §8.14, FR-94, NFR-14). Business rules live here, not in the Razor component (NFR-26/27).
/// </summary>
public class PhysicalBuildingService(ApplicationDbContext db, IAuditWriter audit)
{
    /// <summary>All physical buildings, active and inactive, in admin display order.</summary>
    public async Task<List<PhysicalBuilding>> GetAllAsync() =>
        await db.PhysicalBuildings
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Name)
            .ToListAsync();

    /// <summary>Active physical buildings only, in display order — for FK pickers on the
    /// Building/Facility/CleaningZone admin forms.</summary>
    public async Task<List<PhysicalBuilding>> GetActiveAsync() =>
        await db.PhysicalBuildings
            .Where(p => p.IsActive)
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Name)
            .ToListAsync();

    public async Task<PhysicalBuilding?> GetAsync(Guid id) =>
        await db.PhysicalBuildings.FindAsync(id);

    public async Task CreateAsync(PhysicalBuilding building)
    {
        building.Id = Guid.NewGuid();
        db.PhysicalBuildings.Add(building);
        await audit.RecordAsync(AuditAction.ReferenceDataCreated, nameof(PhysicalBuilding), building.Id,
            $"Fysisk byggnad skapades: \"{building.Name}\".");
        await db.SaveChangesAsync();
    }

    /// <summary>Updates the editable fields of an existing physical building.</summary>
    public async Task UpdateAsync(PhysicalBuilding input)
    {
        var existing = await db.PhysicalBuildings.FindAsync(input.Id);
        if (existing is null) return;

        existing.Name = input.Name;
        existing.Description = input.Description;
        existing.DisplayOrder = input.DisplayOrder;
        await audit.RecordAsync(AuditAction.ReferenceDataUpdated, nameof(PhysicalBuilding), existing.Id,
            $"Fysisk byggnad uppdaterades: \"{existing.Name}\".");
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Deactivates (soft) or reactivates a physical building without deleting history (NFR-14).
    /// </summary>
    public async Task SetActiveAsync(Guid id, bool isActive)
    {
        var building = await db.PhysicalBuildings.FindAsync(id);
        if (building is null) return;

        building.IsActive = isActive;
        await audit.RecordAsync(
            isActive ? AuditAction.ReferenceDataActivated : AuditAction.ReferenceDataDeactivated,
            nameof(PhysicalBuilding), building.Id,
            $"Fysisk byggnad {(isActive ? "aktiverades" : "inaktiverades")}: \"{building.Name}\".");
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Hard-deletes a physical building only when nothing references it (FR-16 pattern);
    /// otherwise <see cref="HardDeleteResult.Blocked"/>.
    /// </summary>
    public async Task<HardDeleteResult> TryHardDeleteAsync(Guid id)
    {
        var building = await db.PhysicalBuildings.FindAsync(id);
        if (building is null) return HardDeleteResult.NotFound;

        if (!CanHardDelete(await CountReferencesAsync(id)))
            return HardDeleteResult.Blocked;

        db.PhysicalBuildings.Remove(building);
        await audit.RecordAsync(AuditAction.ReferenceDataDeleted, nameof(PhysicalBuilding), building.Id,
            $"Fysisk byggnad togs bort: \"{building.Name}\".");
        await db.SaveChangesAsync();
        return HardDeleteResult.Deleted;
    }

    /// <summary>
    /// Pure guard decision (unit-testable, NFR-26): a physical building is hard-deletable
    /// only when nothing references it.
    /// </summary>
    public static bool CanHardDelete(int referenceCount) => referenceCount == 0;

    /// <summary>
    /// Counts references that block a hard-delete: lodging buildings, facilities, and cleaning
    /// zones that point at this physical building.
    /// </summary>
    private async Task<int> CountReferencesAsync(Guid physicalBuildingId)
    {
        var buildingCount = await db.Buildings.CountAsync(b => b.PhysicalBuildingId == physicalBuildingId);
        var facilityCount = await db.Facilities.CountAsync(f => f.PhysicalBuildingId == physicalBuildingId);
        var zoneCount = await db.CleaningZones.CountAsync(z => z.PhysicalBuildingId == physicalBuildingId);
        return buildingCount + facilityCount + zoneCount;
    }
}
