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

    /// <summary>Active cleaning zones only, in display order — for the public prices page (FR-82).</summary>
    public async Task<List<CleaningZone>> GetActiveAsync() =>
        await db.CleaningZones
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();

    public async Task<CleaningZone?> GetAsync(Guid id) =>
        await db.CleaningZones.FindAsync(id);

    /// <summary>The single active zone of a given Kind (§8.11) — <see cref="CleaningZoneKind.MainBuilding"/>
    /// and <see cref="CleaningZoneKind.Conference"/> are expected to have exactly one. Null if none
    /// configured/active. Read-only lookup — <c>AsNoTracking</c> so it never lingers in the change
    /// tracker of a caller (e.g. <c>BookingService</c>) that goes on to <c>SaveChanges</c> for an
    /// unrelated entity in the same request.</summary>
    public async Task<CleaningZone?> GetActiveByKindAsync(CleaningZoneKind kind) =>
        await db.CleaningZones.AsNoTracking().Where(z => z.Kind == kind && z.IsActive).FirstOrDefaultAsync();

    /// <summary>All active zones of a given Kind — <see cref="CleaningZoneKind.SleepingBuilding"/>
    /// may have several (one per physical building). Read-only lookup — see <see cref="GetActiveByKindAsync"/>.</summary>
    public async Task<List<CleaningZone>> GetActiveListByKindAsync(CleaningZoneKind kind) =>
        await db.CleaningZones.AsNoTracking().Where(z => z.Kind == kind && z.IsActive).ToListAsync();

    /// <summary>
    /// Whether the Konferensbyggnad completion-form cleaning toggle applies to a booking (§5.5,
    /// FR-93) — true only when one of the booking's selected facilities' <c>PhysicalBuildingId</c>
    /// matches the active <see cref="CleaningZoneKind.Conference"/> zone's. Server-authoritative:
    /// used both to decide whether to render the toggle and to decide whether to honor it on submit.
    /// </summary>
    public async Task<bool> IsConferenceCleaningApplicableAsync(Guid bookingId)
    {
        var conferenceZone = await GetActiveByKindAsync(CleaningZoneKind.Conference);
        if (conferenceZone?.PhysicalBuildingId is not { } physicalBuildingId) return false;

        return await db.BookingFacilitySelections
            .Where(s => s.BookingId == bookingId)
            .Join(db.Facilities, s => s.FacilityId, f => f.Id, (s, f) => f.PhysicalBuildingId)
            .AnyAsync(facilityPhysicalBuildingId => facilityPhysicalBuildingId == physicalBuildingId);
    }

    /// <summary>Creates a cleaning zone after validating its bundle membership (FR-85) and its
    /// Kind/PhysicalBuilding pairing (§8.11).</summary>
    public async Task<SaveResult> TryCreateAsync(CleaningZone zone)
    {
        zone.Id = Guid.NewGuid();
        zone.IncludesZoneIds = zone.IncludesZoneIds.Distinct().ToList();

        var error = ValidateBundle(zone.Id, zone.IncludesZoneIds, await db.CleaningZones.ToListAsync())
            ?? await ValidatePhysicalBuildingAsync(zone.Kind, zone.PhysicalBuildingId);
        if (error is not null) return SaveResult.Fail(error);

        NormalizePhysicalBuildingForKind(zone);
        db.CleaningZones.Add(zone);
        await db.SaveChangesAsync();
        return SaveResult.Success;
    }

    /// <summary>Updates a cleaning zone after re-validating its bundle membership (FR-86) and its
    /// Kind/PhysicalBuilding pairing (§8.11).</summary>
    public async Task<SaveResult> TryUpdateAsync(CleaningZone input)
    {
        var existing = await db.CleaningZones.FindAsync(input.Id);
        if (existing is null) return SaveResult.Fail("Städzonen kunde inte hittas.");

        var includes = input.IncludesZoneIds.Distinct().ToList();
        var error = ValidateBundle(input.Id, includes, await db.CleaningZones.ToListAsync())
            ?? await ValidatePhysicalBuildingAsync(input.Kind, input.PhysicalBuildingId);
        if (error is not null) return SaveResult.Fail(error);

        NormalizePhysicalBuildingForKind(input);
        existing.Name = input.Name;
        existing.Description = input.Description;
        existing.AmountExVat = input.AmountExVat;
        existing.VatRate = input.VatRate;
        existing.Kind = input.Kind;
        existing.PhysicalBuildingId = input.PhysicalBuildingId;
        existing.IncludesZoneIds = includes;
        existing.DisplayOrder = input.DisplayOrder;
        await db.SaveChangesAsync();
        return SaveResult.Success;
    }

    /// <summary>
    /// Required-FK guard (§8.11): every <see cref="CleaningZoneKind"/> except
    /// <see cref="CleaningZoneKind.Everything"/> must reference a real, existing physical
    /// building — <c>[Required]</c> alone never catches an unset nullable Guid left at null by a
    /// dropdown a user never touched.
    /// </summary>
    private async Task<string?> ValidatePhysicalBuildingAsync(CleaningZoneKind kind, Guid? physicalBuildingId)
    {
        if (kind == CleaningZoneKind.Everything) return null; // bundle — reference-only, no PB

        if (physicalBuildingId is null) return "Fysisk byggnad krävs för denna typ av städzon.";
        var exists = await db.PhysicalBuildings.AnyAsync(p => p.Id == physicalBuildingId);
        return exists ? null : "Vald fysisk byggnad finns inte.";
    }

    /// <summary>The Everything bundle is reference-only and never carries a PhysicalBuildingId
    /// (§8.11) — clear it defensively even if the form somehow submitted one.</summary>
    private static void NormalizePhysicalBuildingForKind(CleaningZone zone)
    {
        if (zone.Kind == CleaningZoneKind.Everything)
            zone.PhysicalBuildingId = null;
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
    /// Counts booking selections that reference this cleaning zone (FR-88). Wired to the real
    /// BookingCleaningSelection table in Phase 3; a non-zero count blocks the hard-delete.
    /// </summary>
    private async Task<int> CountReferencesAsync(Guid cleaningZoneId) =>
        await db.BookingCleaningSelections.CountAsync(s => s.CleaningZoneId == cleaningZoneId);
}
