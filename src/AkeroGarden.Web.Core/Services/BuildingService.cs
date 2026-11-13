using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// CRUD and lifecycle rules for <see cref="Building"/> reference data
/// (FR-13–16, NFR-14). Business rules live here, not in the Razor component,
/// so the deactivate / no-hard-delete rules are testable (NFR-26/27).
/// </summary>
public class BuildingService(
    ApplicationDbContext db,
    IAuditWriter audit,
    IImageStorage images,
    ILogger<BuildingService> logger)
{
    /// <summary>The heading over every house that is the only lodging in its physical building
    /// (Smedjan, Tallstugan) — one shared group instead of a heading that repeats the house's name.</summary>
    public const string StandaloneGroupName = "Fristående stugor";

    /// <summary>All buildings, active and inactive, in admin display order.</summary>
    public async Task<List<Building>> GetAllAsync() =>
        await db.Buildings
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.Name)
            .ToListAsync();

    /// <summary>
    /// The public lodging listing (FR-81): active houses with their active rooms, grouped. Read by
    /// /faciliteter, and its <see cref="LodgingOverview.Capacity"/> by the home and about pages, so
    /// no page states a bed or house count that the data does not.
    /// </summary>
    public async Task<LodgingOverview> GetLodgingOverviewAsync()
    {
        var buildings = await db.Buildings
            .AsNoTracking()
            .Where(b => b.IsActive)
            .Include(b => b.Rooms.Where(r => r.IsActive))
            .ToListAsync();
        var physicalBuildings = await db.PhysicalBuildings
            .AsNoTracking()
            .Where(p => p.IsActive)
            .ToListAsync();

        return BuildLodgingOverview(buildings, physicalBuildings);
    }

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
        existing.SharedFacilities = input.SharedFacilities;
        existing.IsAccessible = input.IsAccessible;
        existing.DisplayOrder = input.DisplayOrder;
        // Captured before the assignment so the superseded blob can be cleaned up after the commit.
        var previousImage = existing.ImageName;
        existing.ImageName = input.ImageName;
        await audit.RecordAsync(AuditAction.ReferenceDataUpdated, nameof(Building), existing.Id,
            $"Byggnad uppdaterades: \"{existing.Name}\".");
        await db.SaveChangesAsync();

        await images.DeleteSupersededAsync(previousImage, existing.ImageName, logger);
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

        var image = building.ImageName;

        db.Buildings.Remove(building);
        await audit.RecordAsync(AuditAction.ReferenceDataDeleted, nameof(Building), building.Id,
            $"Byggnad togs bort: \"{building.Name}\".");
        await db.SaveChangesAsync();

        // The building that referenced the image is gone, so nothing ever will again.
        await images.DeleteSupersededAsync(image, null, logger);
        return HardDeleteResult.Deleted;
    }

    /// <summary>
    /// Pure guard decision (unit-testable, NFR-26): a building is hard-deletable
    /// only when nothing references it.
    /// </summary>
    public static bool CanHardDelete(int referenceCount) => referenceCount == 0;

    /// <summary>
    /// Shapes the public lodging listing (pure, unit-testable — NFR-26; the Razor page renders the
    /// result and holds no grouping or ordering logic of its own).
    /// <list type="bullet">
    /// <item>A house is listed only with at least one room: a house nobody can sleep in is not
    /// lodging, and would show as "0 rum · 0 bäddar".</item>
    /// <item>A physical building holding two or more houses is a group under its own name (Lilla
    /// bostadslängan: Blidö, Ljusterö, Singö, Tjockö), in physical-building display order.</item>
    /// <item>A house that is alone in its physical building joins the one
    /// <see cref="StandaloneGroupName"/> group, listed last — otherwise its group heading would just
    /// repeat its own name.</item>
    /// <item>Houses whose physical building is inactive or missing are never silently dropped: by the
    /// same rule, several of them form a group under "Övrigt" (there is no name to show), and one
    /// alone joins the standalone group.</item>
    /// </list>
    /// Callers pass active records with their active rooms already loaded.
    /// </summary>
    public static LodgingOverview BuildLodgingOverview(
        List<Building> buildings, List<PhysicalBuilding> physicalBuildings)
    {
        var physicalBuildingsById = physicalBuildings.ToDictionary(p => p.Id);

        var byPhysicalBuilding = buildings
            .Where(b => b.Rooms.Count > 0)
            .Select(b => new LodgingHouse(
                b,
                b.Rooms.OrderBy(r => r.DisplayOrder).ThenBy(r => r.Name).ToList()))
            .GroupBy(h => h.Building.PhysicalBuildingId)
            .Select(g => (
                PhysicalBuilding: physicalBuildingsById.GetValueOrDefault(g.Key),
                Houses: g.OrderBy(h => h.Building.DisplayOrder).ThenBy(h => h.Building.Name).ToList()))
            .OrderBy(g => g.PhysicalBuilding?.DisplayOrder ?? int.MaxValue)
            .ThenBy(g => g.PhysicalBuilding?.Name)
            .ToList();

        var groups = byPhysicalBuilding
            .Where(g => g.Houses.Count > 1)
            .Select(g => new LodgingGroup(g.PhysicalBuilding?.Name ?? "Övrigt", g.Houses))
            .ToList();

        var standalone = byPhysicalBuilding
            .Where(g => g.Houses.Count == 1)
            .Select(g => g.Houses[0])
            .ToList();
        if (standalone.Count > 0)
            groups.Add(new LodgingGroup(StandaloneGroupName, standalone));

        var houses = groups.SelectMany(g => g.Houses).ToList();
        return new LodgingOverview(
            groups,
            new LodgingCapacity(
                Beds: houses.Sum(h => h.Beds),
                Houses: houses.Count,
                Rooms: houses.Sum(h => h.Rooms.Count)));
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

/// <summary>The public lodging listing — see <see cref="BuildingService.BuildLodgingOverview"/>.</summary>
public record LodgingOverview(List<LodgingGroup> Groups, LodgingCapacity Capacity);

/// <summary>One heading's worth of houses on the public lodging listing.</summary>
public record LodgingGroup(string Name, List<LodgingHouse> Houses)
{
    public int Beds => Houses.Sum(h => h.Beds);
}

/// <summary>One house and its active rooms, in display order.</summary>
public record LodgingHouse(Building Building, List<Room> Rooms)
{
    public int Beds => Rooms.Sum(r => r.BedCount);
}

/// <summary>What the property sleeps, counted from the active data — the numbers every public page
/// states, so none can drift from the rooms the admin actually keeps.</summary>
public record LodgingCapacity(int Beds, int Houses, int Rooms);
