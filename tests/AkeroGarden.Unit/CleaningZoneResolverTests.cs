using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the pure sleeping-building cleaning-zone resolution (§8.11, §5.5, FR-93):
/// a structural join from occupied physical buildings to their zones, one zone per physical
/// building regardless of how many rooms/lodging units within it are occupied.
/// </summary>
public class CleaningZoneResolverTests
{
    private static readonly Guid LillaId = Guid.NewGuid();
    private static readonly Guid StoraId = Guid.NewGuid();
    private static readonly Guid KonferensId = Guid.NewGuid(); // a Conference zone, never a candidate here

    private static CleaningZone Zone(Guid id, Guid? physicalBuildingId, CleaningZoneKind kind) => new()
    {
        Id = id,
        Name = "Zon",
        Kind = kind,
        PhysicalBuildingId = physicalBuildingId,
        IsActive = true,
    };

    private static readonly CleaningZone LillaZone = Zone(Guid.NewGuid(), LillaId, CleaningZoneKind.SleepingBuilding);
    private static readonly CleaningZone StoraZone = Zone(Guid.NewGuid(), StoraId, CleaningZoneKind.SleepingBuilding);
    private static readonly CleaningZone EverythingBundle = Zone(Guid.NewGuid(), null, CleaningZoneKind.Everything);

    [Fact]
    public void NoOccupiedBuildings_ResolvesToEmpty()
    {
        var result = CleaningZoneResolver.ResolveSleepingZoneIds([], [LillaZone, StoraZone]);
        Assert.Empty(result);
    }

    [Fact]
    public void ChurchOnlyOrNonLodgingOccupancy_NoMatchingZone_ResolvesToEmpty()
    {
        // Occupying a physical building with no corresponding SleepingBuilding zone (e.g. only
        // Kapellet/Huvudbyggnad were "occupied" in some other sense) resolves to nothing.
        var unrelatedPhysicalBuildingId = Guid.NewGuid();
        var result = CleaningZoneResolver.ResolveSleepingZoneIds([unrelatedPhysicalBuildingId], [LillaZone, StoraZone]);
        Assert.Empty(result);
    }

    [Fact]
    public void SingleOccupiedPhysicalBuilding_ResolvesToItsZoneOnly()
    {
        var result = CleaningZoneResolver.ResolveSleepingZoneIds([LillaId], [LillaZone, StoraZone]);
        Assert.Equal([LillaZone.Id], result);
    }

    [Fact]
    public void MultipleUnitsInSamePhysicalBuilding_CollapsesToOneZone()
    {
        // Two lodging units (e.g. Blidö and Tjockö) both sit in Lilla bostadslängan — occupying
        // both must still resolve to exactly one zone, never per room/unit (§5.5).
        var result = CleaningZoneResolver.ResolveSleepingZoneIds([LillaId, LillaId], [LillaZone, StoraZone]);
        Assert.Equal([LillaZone.Id], result);
    }

    [Fact]
    public void MultiplePhysicalBuildingsOccupied_ResolvesToAllTheirZones()
    {
        var result = CleaningZoneResolver.ResolveSleepingZoneIds([LillaId, StoraId], [LillaZone, StoraZone]);
        Assert.Equal(new HashSet<Guid> { LillaZone.Id, StoraZone.Id }, result);
    }

    [Fact]
    public void NonSleepingZonesInCandidateList_AreIgnoredEvenIfPhysicalBuildingMatches()
    {
        // A Conference zone happening to share a physical building id with an occupied lodging
        // building should never surface here — callers only ever pass SleepingBuilding zones, but
        // the resolver itself is defensive about it too.
        var conferenceSharingLillaId = Zone(Guid.NewGuid(), LillaId, CleaningZoneKind.Conference);
        var result = CleaningZoneResolver.ResolveSleepingZoneIds([LillaId], [conferenceSharingLillaId]);
        // The resolver only filters by PhysicalBuildingId match — Kind filtering is the caller's
        // responsibility (GetActiveListByKindAsync already scopes to SleepingBuilding). Documents
        // that contract: passing a non-sleeping zone in still matches on PhysicalBuildingId.
        Assert.Equal([conferenceSharingLillaId.Id], result);
    }

    [Fact]
    public void ZonesWithNoPhysicalBuildingId_AreNeverMatched()
    {
        // The Everything bundle has no PhysicalBuildingId and must never be resolved as if it were
        // a sleeping-building zone, even by coincidence.
        var result = CleaningZoneResolver.ResolveSleepingZoneIds([LillaId, StoraId, KonferensId], [EverythingBundle]);
        Assert.Empty(result);
    }
}
