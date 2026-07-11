using AkeroGarden.Web.Data.Entities;

namespace AkeroGarden.Web.Services;

/// <summary>
/// Pure resolution of which <see cref="CleaningZoneKind.SleepingBuilding"/> zones are "in play"
/// for a booking (§8.11, §5.5, FR-93): a structural join room → <c>Building.PhysicalBuildingId</c>
/// → <c>CleaningZone.PhysicalBuildingId</c>. One zone per occupied physical building, never per
/// room — two occupied lodging units in the same physical building (e.g. Blidö and Tjockö, both in
/// Lilla bostadslängan) resolve to the same single zone (unit-testable, NFR-26).
/// </summary>
public static class CleaningZoneResolver
{
    /// <param name="occupiedPhysicalBuildingIds">The distinct physical buildings the booking's
    /// assigned rooms sit in.</param>
    /// <param name="sleepingZones">Active <see cref="CleaningZoneKind.SleepingBuilding"/> zones.</param>
    public static HashSet<Guid> ResolveSleepingZoneIds(
        IReadOnlyCollection<Guid> occupiedPhysicalBuildingIds,
        IReadOnlyCollection<CleaningZone> sleepingZones) =>
        sleepingZones
            .Where(z => z.PhysicalBuildingId is { } pbId && occupiedPhysicalBuildingIds.Contains(pbId))
            .Select(z => z.Id)
            .ToHashSet();
}
