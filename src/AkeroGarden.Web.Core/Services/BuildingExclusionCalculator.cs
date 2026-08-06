using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Services;

/// <summary>A room reduced to what building assignment needs — its bed contribution (§8.16).</summary>
public readonly record struct AssignableRoom(Guid Id, int BedCount);

/// <summary>A lodging building plus the rooms it contains, kept free of EF so the calculator stays
/// pure and unit-testable (NFR-26).</summary>
public readonly record struct BuildingInfo(
    Guid Id,
    string Name,
    string PhysicalBuildingName,
    IReadOnlyList<AssignableRoom> Rooms);

/// <summary>Why a building cannot be assigned to a booking (FR-102).</summary>
public enum BuildingUnavailableReason
{
    Available,
    Blocked
}

/// <summary>
/// A building's assignability for a booking's stay dates, with the beds actually available over
/// those dates — paper capacity minus the beds of any room-scoped block (see
/// <see cref="BuildingExclusionCalculator"/>).
/// </summary>
public readonly record struct BuildingAvailability(
    BuildingInfo Building,
    bool IsAvailable,
    BuildingUnavailableReason Reason,
    int AvailableBedCount);

/// <summary>
/// Pure, unit-testable (NFR-26) computation of which lodging buildings the admin may assign to a
/// booking (FR-102, FR-103, §3.3). Replaces the block half of the former room-picker exclusion
/// logic, one level up: the occupancy half died with per-participant room assignment, since the
/// system no longer records who sleeps in which room.
///
/// Two rules, and the distinction between them matters:
/// <list type="bullet">
/// <item>A <see cref="BlockScope.WholeProperty"/> or <see cref="BlockScope.Building"/> block
/// overlapping the stay makes the building <b>unassignable</b> (FR-102).</item>
/// <item>A <see cref="BlockScope.Room"/> block does <b>not</b> — one closed room does not take a
/// building out of service. It instead reduces the building's available beds, so the capacity
/// warning (FR-103/D2) is measured against the beds really on offer for those dates rather than
/// paper capacity. Warning on paper capacity would stay silent in exactly the case the warning
/// exists for. This mirrors <see cref="OccupancyProvider"/>, which already nets blocked beds out
/// of the public calendar's capacity.</item>
/// </list>
///
/// The caller pre-filters <paramref name="overlappingBlocks"/> to the booking's date window; this
/// class applies the scope rules only.
/// </summary>
public static class BuildingExclusionCalculator
{
    /// <summary>Evaluates every supplied building. <paramref name="buildings"/> should already be
    /// active lodging buildings holding their active rooms.</summary>
    public static IReadOnlyList<BuildingAvailability> Evaluate(
        IReadOnlyCollection<BuildingInfo> buildings,
        IReadOnlyCollection<BlockedPeriod> overlappingBlocks)
    {
        var wholeProperty = overlappingBlocks.Any(b => b.Scope == BlockScope.WholeProperty);
        var blockedBuildings = overlappingBlocks
            .Where(b => b.Scope == BlockScope.Building && b.BuildingId is not null)
            .Select(b => b.BuildingId!.Value)
            .ToHashSet();
        var blockedRooms = overlappingBlocks
            .Where(b => b.Scope == BlockScope.Room && b.RoomId is not null)
            .Select(b => b.RoomId!.Value)
            .ToHashSet();

        var result = new List<BuildingAvailability>(buildings.Count);
        foreach (var building in buildings)
        {
            var blocked = wholeProperty || blockedBuildings.Contains(building.Id);

            // A blocked building offers nothing; otherwise net out individually blocked rooms.
            var beds = blocked
                ? 0
                : building.Rooms.Where(r => !blockedRooms.Contains(r.Id)).Sum(r => r.BedCount);

            result.Add(new BuildingAvailability(
                building,
                !blocked,
                blocked ? BuildingUnavailableReason.Blocked : BuildingUnavailableReason.Available,
                beds));
        }

        return result;
    }

    /// <summary>The assignable buildings only — what the admin's assignment editor offers.</summary>
    public static IReadOnlyList<BuildingInfo> AssignableBuildings(
        IReadOnlyCollection<BuildingInfo> buildings,
        IReadOnlyCollection<BlockedPeriod> overlappingBlocks) =>
        Evaluate(buildings, overlappingBlocks)
            .Where(b => b.IsAvailable)
            .Select(b => b.Building)
            .ToList();
}
