using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// CRUD and lifecycle rules for <see cref="Facility"/> reference data (FR-27–30, NFR-14).
/// Business rules live here, not in the Razor component (NFR-26/27). The image follows the shared
/// rules in <see cref="ImageUpload"/>, like a building's and a room's.
/// </summary>
public class FacilityService(
    ApplicationDbContext db,
    IAuditWriter audit,
    IImageStorage images,
    ILogger<FacilityService> logger)
{
    /// <summary>All facilities, active and inactive, in admin display order.</summary>
    public async Task<List<Facility>> GetAllAsync() =>
        await db.Facilities
            .OrderBy(f => f.DisplayOrder)
            .ThenBy(f => f.Name)
            .ToListAsync();

    /// <summary>Active facilities only, in display order — for the public facilities page (FR-81).</summary>
    public async Task<List<Facility>> GetActiveAsync() =>
        await db.Facilities
            .Where(f => f.IsActive)
            .OrderBy(f => f.DisplayOrder)
            .ThenBy(f => f.Name)
            .ToListAsync();

    public async Task<Facility?> GetAsync(Guid id) =>
        await db.Facilities.FindAsync(id);

    public async Task CreateAsync(Facility facility)
    {
        facility.Id = Guid.NewGuid();
        db.Facilities.Add(facility);
        await audit.RecordAsync(AuditAction.ReferenceDataCreated, nameof(Facility), facility.Id,
            $"Facilitet skapades: \"{facility.Name}\".");
        await db.SaveChangesAsync();
    }

    /// <summary>Updates the editable fields of an existing facility (FR-28).</summary>
    public async Task UpdateAsync(Facility input)
    {
        var existing = await db.Facilities.FindAsync(input.Id);
        if (existing is null) return;

        existing.Name = input.Name;
        existing.PhysicalBuildingId = input.PhysicalBuildingId;
        existing.Description = input.Description;
        // Captured before the assignment so the superseded blob can be cleaned up after the commit.
        var previousImage = existing.ImageName;

        existing.ImageName = input.ImageName;
        existing.AmountExVat = input.AmountExVat;
        existing.VatRate = input.VatRate;
        existing.DisplayOrder = input.DisplayOrder;
        await audit.RecordAsync(AuditAction.ReferenceDataUpdated, nameof(Facility), existing.Id,
            $"Facilitet uppdaterades: \"{existing.Name}\".");
        await db.SaveChangesAsync();

        await images.DeleteSupersededAsync(previousImage, existing.ImageName, logger);
    }

    /// <summary>Deactivates (soft) or reactivates a facility without deleting history (FR-29, NFR-14).</summary>
    public async Task SetActiveAsync(Guid id, bool isActive)
    {
        var facility = await db.Facilities.FindAsync(id);
        if (facility is null) return;

        facility.IsActive = isActive;
        await audit.RecordAsync(
            isActive ? AuditAction.ReferenceDataActivated : AuditAction.ReferenceDataDeactivated,
            nameof(Facility), facility.Id,
            $"Facilitet {(isActive ? "aktiverades" : "inaktiverades")}: \"{facility.Name}\".");
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Hard-deletes a facility only when nothing references it (FR-30); otherwise <see cref="HardDeleteResult.Blocked"/>.
    /// </summary>
    public async Task<HardDeleteResult> TryHardDeleteAsync(Guid id)
    {
        var facility = await db.Facilities.FindAsync(id);
        if (facility is null) return HardDeleteResult.NotFound;

        if (!CanHardDelete(await CountReferencesAsync(id)))
            return HardDeleteResult.Blocked;

        var image = facility.ImageName;

        db.Facilities.Remove(facility);
        await audit.RecordAsync(AuditAction.ReferenceDataDeleted, nameof(Facility), facility.Id,
            $"Facilitet togs bort: \"{facility.Name}\".");
        await db.SaveChangesAsync();

        // Delete the image too — the facility that referenced it is gone, so nothing ever will again.
        await images.DeleteSupersededAsync(image, null, logger);
        return HardDeleteResult.Deleted;
    }

    /// <summary>
    /// Pure guard decision (unit-testable, NFR-26): a facility is hard-deletable only when
    /// nothing references it.
    /// </summary>
    public static bool CanHardDelete(int referenceCount) => referenceCount == 0;

    /// <summary>
    /// Counts booking selections that reference this facility (FR-30). Wired to the real
    /// BookingFacilitySelection table in Phase 3; a non-zero count blocks the hard-delete.
    /// </summary>
    private async Task<int> CountReferencesAsync(Guid facilityId) =>
        await db.BookingFacilitySelections.CountAsync(s => s.FacilityId == facilityId);
}
