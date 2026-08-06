using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// CRUD and lifecycle rules for <see cref="Building"/> reference data
/// (FR-13–16, NFR-14). Business rules live here, not in the Razor component,
/// so the deactivate / no-hard-delete rules are testable (NFR-26/27).
/// </summary>
public class BuildingService(ApplicationDbContext db, IAuditWriter audit)
{
    /// <summary>All buildings, active and inactive, in admin display order.</summary>
    public async Task<List<Building>> GetAllAsync() =>
        await db.Buildings
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.Name)
            .ToListAsync();

    /// <summary>Active buildings only, in display order — for the public facilities page (FR-81
    /// context: the "Åtta hus" lodging section) and any other public lodging listing.</summary>
    public async Task<List<Building>> GetActiveAsync() =>
        await db.Buildings
            .Where(b => b.IsActive)
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.Name)
            .ToListAsync();

    public async Task<Building?> GetAsync(Guid id) =>
        await db.Buildings.FindAsync(id);

    /// <summary>Creates a building after validating it references a physical building (FR-13).</summary>
    public async Task<SaveResult> TryCreateAsync(Building building)
    {
        var error = await ValidatePhysicalBuildingAsync(building.PhysicalBuildingId);
        if (error is not null) return SaveResult.Fail(error);

        building.Id = Guid.NewGuid();
        db.Buildings.Add(building);
        await audit.RecordAsync(AuditAction.ReferenceDataCreated, nameof(Building), building.Id,
            $"Byggnad skapades: \"{building.Name}\".");
        await db.SaveChangesAsync();
        return SaveResult.Success;
    }

    /// <summary>Updates the editable fields of an existing building, re-validating its physical
    /// building reference (FR-14).</summary>
    public async Task<SaveResult> TryUpdateAsync(Building input)
    {
        var existing = await db.Buildings.FindAsync(input.Id);
        if (existing is null) return SaveResult.Fail("Byggnaden kunde inte hittas.");

        var error = await ValidatePhysicalBuildingAsync(input.PhysicalBuildingId);
        if (error is not null) return SaveResult.Fail(error);

        existing.Name = input.Name;
        existing.PhysicalBuildingId = input.PhysicalBuildingId;
        existing.Description = input.Description;
        existing.SharedFacilities = input.SharedFacilities;
        existing.IsAccessible = input.IsAccessible;
        existing.DisplayOrder = input.DisplayOrder;
        await audit.RecordAsync(AuditAction.ReferenceDataUpdated, nameof(Building), existing.Id,
            $"Byggnad uppdaterades: \"{existing.Name}\".");
        await db.SaveChangesAsync();
        return SaveResult.Success;
    }

    /// <summary>
    /// Required-FK guard: <see cref="Building.PhysicalBuildingId"/> is a non-nullable Guid, so
    /// <c>[Required]</c> alone never catches an unset (default) value from a dropdown a user
    /// never touched — this validates it explicitly (§8.14, FR-13).
    /// </summary>
    private async Task<string?> ValidatePhysicalBuildingAsync(Guid physicalBuildingId)
    {
        if (physicalBuildingId == Guid.Empty)
            return "Fysisk byggnad krävs.";
        var exists = await db.PhysicalBuildings.AnyAsync(p => p.Id == physicalBuildingId);
        return exists ? null : "Vald fysisk byggnad finns inte.";
    }

    /// <summary>
    /// Deactivates (soft) or reactivates a building without deleting history (FR-15, NFR-14).
    /// </summary>
    public async Task SetActiveAsync(Guid id, bool isActive)
    {
        var building = await db.Buildings.FindAsync(id);
        if (building is null) return;

        building.IsActive = isActive;
        await audit.RecordAsync(
            isActive ? AuditAction.ReferenceDataActivated : AuditAction.ReferenceDataDeactivated,
            nameof(Building), building.Id,
            $"Byggnad {(isActive ? "aktiverades" : "inaktiverades")}: \"{building.Name}\".");
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
        await audit.RecordAsync(AuditAction.ReferenceDataDeleted, nameof(Building), building.Id,
            $"Byggnad togs bort: \"{building.Name}\".");
        await db.SaveChangesAsync();
        return HardDeleteResult.Deleted;
    }

    /// <summary>
    /// Pure guard decision (unit-testable, NFR-26): a building is hard-deletable
    /// only when nothing references it.
    /// </summary>
    public static bool CanHardDelete(int referenceCount) => referenceCount == 0;

    /// <summary>
    /// Groups active buildings by their physical building for the public lodging listing,
    /// ordered by <see cref="PhysicalBuilding.DisplayOrder"/> (unit-testable, NFR-26 — the
    /// Razor page calls this rather than embedding grouping/ordering logic itself). A building
    /// whose physical building isn't in <paramref name="physicalBuildings"/> (e.g. deactivated
    /// while the lodging unit under it stayed active) falls into an "Övrigt" group sorted last,
    /// rather than being silently dropped from the page.
    /// </summary>
    public static List<LodgingGroup> GroupByPhysicalBuilding(
        List<Building> buildings, List<PhysicalBuilding> physicalBuildings)
    {
        var physicalBuildingsById = physicalBuildings.ToDictionary(p => p.Id);

        return buildings
            .GroupBy(b => b.PhysicalBuildingId)
            .Select(g =>
            {
                var physicalBuilding = physicalBuildingsById.GetValueOrDefault(g.Key);
                return new LodgingGroup(
                    physicalBuilding?.Name ?? "Övrigt",
                    physicalBuilding?.DisplayOrder ?? int.MaxValue,
                    g.OrderBy(b => b.DisplayOrder).ThenBy(b => b.Name).ToList());
            })
            .OrderBy(g => g.PhysicalBuildingOrder)
            .ThenBy(g => g.PhysicalBuildingName)
            .ToList();
    }

    /// <summary>
    /// Counts references that block a hard-delete (FR-16). Three layers, any one of which blocks:
    /// rooms in the building (Phase 1); bookings the building is assigned to via
    /// <see cref="BookingBuildingAssignment"/> (§8.16, FR-103); and blocked periods scoped to the
    /// building or to any of its rooms.
    ///
    /// The booking layer replaces the former transitive count of participants assigned to this
    /// building's rooms, which died with per-participant room assignment. The blocked-period layer
    /// is new: <see cref="BlockedPeriod.BuildingId"/> / <see cref="BlockedPeriod.RoomId"/> are
    /// <b>logical references, not foreign keys</b> (see ApplicationDbContext), so deleting a building
    /// silently orphans blocks pointing at it or at its rooms.
    ///
    /// Note the room layer alone made this guard look safe while the participant layer was rotting —
    /// a lodging building always has rooms, so the delete was blocked by coincidence rather than by
    /// design. The layers below are each load-bearing on their own.
    /// </summary>
    private async Task<int> CountReferencesAsync(Guid buildingId)
    {
        var roomCount = await db.Rooms.CountAsync(r => r.BuildingId == buildingId);
        var assignmentCount = await db.BookingBuildingAssignments
            .CountAsync(a => a.BuildingId == buildingId);
        var blockCount = await db.BlockedPeriods
            .CountAsync(p => p.BuildingId == buildingId
                || (p.RoomId != null && db.Rooms.Any(r => r.Id == p.RoomId && r.BuildingId == buildingId)));
        return roomCount + assignmentCount + blockCount;
    }
}

/// <summary>One physical building's lodging units, for the public "Boende" listing — see
/// <see cref="BuildingService.GroupByPhysicalBuilding"/>.</summary>
public record LodgingGroup(string PhysicalBuildingName, int PhysicalBuildingOrder, List<Building> Buildings);
