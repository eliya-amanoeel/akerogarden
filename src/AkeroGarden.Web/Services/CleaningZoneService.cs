using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Services;

/// <summary>
/// CRUD, bundle-integrity, and lifecycle rules for <see cref="CleaningZone"/> reference
/// data (FR-85–88, NFR-14). Business rules live here, not in the Razor component (NFR-26/27).
/// </summary>
public class CleaningZoneService(ApplicationDbContext db)
{
    /// <summary>All cleaning zones, active and inactive, in admin display order.</summary>
    public async Task<List<CleaningZone>> GetAllAsync() =>
        await db.CleaningZones
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();

    public async Task<CleaningZone?> GetAsync(Guid id) =>
        await db.CleaningZones.FindAsync(id);

    /// <summary>Creates a cleaning zone after validating its bundle membership (FR-85).</summary>
    public async Task<SaveResult> TryCreateAsync(CleaningZone zone)
    {
        zone.Id = Guid.NewGuid();
        zone.IncludesZoneIds = zone.IncludesZoneIds.Distinct().ToList();

        var error = ValidateBundle(zone.Id, zone.IncludesZoneIds, await db.CleaningZones.ToListAsync());
        if (error is not null) return SaveResult.Fail(error);

        db.CleaningZones.Add(zone);
        await db.SaveChangesAsync();
        return SaveResult.Success;
    }

    /// <summary>Updates a cleaning zone after re-validating its bundle membership (FR-86).</summary>
    public async Task<SaveResult> TryUpdateAsync(CleaningZone input)
    {
        var existing = await db.CleaningZones.FindAsync(input.Id);
        if (existing is null) return SaveResult.Fail("Städzonen kunde inte hittas.");

        var includes = input.IncludesZoneIds.Distinct().ToList();
        var error = ValidateBundle(input.Id, includes, await db.CleaningZones.ToListAsync());
        if (error is not null) return SaveResult.Fail(error);

        existing.Name = input.Name;
        existing.Description = input.Description;
        existing.AmountExVat = input.AmountExVat;
        existing.VatRate = input.VatRate;
        existing.IncludesZoneIds = includes;
        existing.DisplayOrder = input.DisplayOrder;
        await db.SaveChangesAsync();
        return SaveResult.Success;
    }

    /// <summary>
    /// Deactivates (soft) or reactivates a zone without altering selections already saved
    /// on bookings (FR-87, NFR-14).
    /// </summary>
    public async Task SetActiveAsync(Guid id, bool isActive)
    {
        var zone = await db.CleaningZones.FindAsync(id);
        if (zone is null) return;

        zone.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Hard-deletes a zone only when nothing references it (FR-88); otherwise <see cref="HardDeleteResult.Blocked"/>.
    /// </summary>
    public async Task<HardDeleteResult> TryHardDeleteAsync(Guid id)
    {
        var zone = await db.CleaningZones.FindAsync(id);
        if (zone is null) return HardDeleteResult.NotFound;

        if (!CanHardDelete(await CountReferencesAsync(id)))
            return HardDeleteResult.Blocked;

        db.CleaningZones.Remove(zone);
        await db.SaveChangesAsync();
        return HardDeleteResult.Deleted;
    }

    /// <summary>
    /// Pure bundle-integrity rule (unit-testable, NFR-26). Returns a Swedish error message
    /// if the proposed bundle membership is invalid, or null if it is valid.
    /// </summary>
    /// <param name="zoneId">The zone being created/edited.</param>
    /// <param name="includesZoneIds">Proposed included zone IDs (empty = a normal zone).</param>
    /// <param name="allZones">All persisted zones, used to resolve the included IDs.</param>
    public static string? ValidateBundle(
        Guid zoneId,
        IReadOnlyCollection<Guid> includesZoneIds,
        IReadOnlyCollection<CleaningZone> allZones)
    {
        if (includesZoneIds.Count == 0) return null; // normal zone — nothing to validate

        if (includesZoneIds.Contains(zoneId))
            return "En städzon kan inte inkludera sig själv.";

        foreach (var includedId in includesZoneIds)
        {
            var included = allZones.FirstOrDefault(z => z.Id == includedId);
            if (included is null)
                return "En av de inkluderade städzonerna finns inte.";
            if (!included.IsActive)
                return "En bunt kan endast inkludera aktiva städzoner.";
            if (included.IncludesZoneIds.Count > 0)
                return "En bunt kan inte inkludera en annan bunt.";
        }

        return null;
    }

    /// <summary>
    /// Pure guard decision (unit-testable, NFR-26): a zone is hard-deletable only when
    /// nothing references it.
    /// </summary>
    public static bool CanHardDelete(int referenceCount) => referenceCount == 0;

    /// <summary>
    /// Counts references that block a hard-delete. Phase 1: none exist yet —
    /// zones are first referenced by BookingCleaningSelection.CleaningZoneId in Phase 3 (FR-88), wired in then.
    /// </summary>
    private Task<int> CountReferencesAsync(Guid cleaningZoneId) => Task.FromResult(0);
}
