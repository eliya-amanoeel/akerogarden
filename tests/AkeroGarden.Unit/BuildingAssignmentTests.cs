using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the pure building-assignment logic (NFR-26, FR-102/103, §3.3): which lodging
/// buildings the admin may assign for a booking's dates, and how many beds each really offers over
/// those dates. Replaces the block half of the former <c>RoomAssignmentTests</c>; the occupancy half
/// is gone with per-participant room assignment, which the system no longer records.
/// </summary>
public class BuildingAssignmentTests
{
    private static readonly Guid Room1 = Guid.NewGuid();
    private static readonly Guid Room2 = Guid.NewGuid();
    private static readonly Guid Room3 = Guid.NewGuid();

    // Blidö: 4 + 2 = 6 beds. Singö: 3 beds.
    private static readonly BuildingInfo Blido = new(
        Guid.NewGuid(), "Blidö", "Stora bostadslängan", [new(Room1, 4), new(Room2, 2)]);

    private static readonly BuildingInfo Singo = new(
        Guid.NewGuid(), "Singö", "Lilla bostadslängan", [new(Room3, 3)]);

    private static readonly BuildingInfo[] AllBuildings = [Blido, Singo];

    private static BuildingAvailability For(IReadOnlyList<BuildingAvailability> r, BuildingInfo b) =>
        r.Single(x => x.Building.Id == b.Id);

    // ── Assignability (FR-102) ────────────────────────────────────────────────────

    [Fact]
    public void NoBlocks_AllBuildingsAssignable_AtFullCapacity()
    {
        var result = BuildingExclusionCalculator.Evaluate(AllBuildings, []);

        Assert.All(result, b => Assert.True(b.IsAvailable));
        Assert.Equal(6, For(result, Blido).AvailableBedCount);
        Assert.Equal(3, For(result, Singo).AvailableBedCount);
    }

    [Fact]
    public void WholePropertyBlock_MakesEveryBuildingUnassignable()
    {
        var block = new BlockedPeriod { Scope = BlockScope.WholeProperty };

        var result = BuildingExclusionCalculator.Evaluate(AllBuildings, [block]);

        Assert.All(result, b =>
        {
            Assert.False(b.IsAvailable);
            Assert.Equal(BuildingUnavailableReason.Blocked, b.Reason);
            Assert.Equal(0, b.AvailableBedCount);
        });
        Assert.Empty(BuildingExclusionCalculator.AssignableBuildings(AllBuildings, [block]));
    }

    [Fact]
    public void BuildingBlock_MakesOnlyThatBuildingUnassignable()
    {
        var block = new BlockedPeriod { Scope = BlockScope.Building, BuildingId = Blido.Id };

        var result = BuildingExclusionCalculator.Evaluate(AllBuildings, [block]);

        Assert.False(For(result, Blido).IsAvailable);
        Assert.Equal(BuildingUnavailableReason.Blocked, For(result, Blido).Reason);
        Assert.True(For(result, Singo).IsAvailable);
        Assert.Equal([Singo.Id], BuildingExclusionCalculator.AssignableBuildings(AllBuildings, [block]).Select(b => b.Id));
    }

    // ── Capacity over the stay dates (FR-103/D2) ──────────────────────────────────

    [Fact]
    public void RoomBlock_LeavesTheBuildingAssignable_ButRemovesThatRoomsBeds()
    {
        // The distinction FR-102 draws: one closed room does not take a building out of service,
        // but the capacity warning must be measured against the beds actually on offer.
        var block = new BlockedPeriod { Scope = BlockScope.Room, BuildingId = Blido.Id, RoomId = Room1 };

        var result = BuildingExclusionCalculator.Evaluate(AllBuildings, [block]);
        var blido = For(result, Blido);

        Assert.True(blido.IsAvailable);
        Assert.Equal(BuildingUnavailableReason.Available, blido.Reason);
        Assert.Equal(2, blido.AvailableBedCount); // 6 paper beds − the blocked 4-bed room
    }

    [Fact]
    public void EveryRoomBlockedIndividually_LeavesTheBuildingAssignableWithNoBeds()
    {
        // Deliberately asserts the awkward edge rather than hiding it: blocking every room one by
        // one is not the same act as blocking the building, so the building stays assignable — but
        // it offers nothing, and the capacity warning is what surfaces that to the admin.
        BlockedPeriod[] blocks =
        [
            new() { Scope = BlockScope.Room, BuildingId = Blido.Id, RoomId = Room1 },
            new() { Scope = BlockScope.Room, BuildingId = Blido.Id, RoomId = Room2 },
        ];

        var blido = For(BuildingExclusionCalculator.Evaluate(AllBuildings, blocks), Blido);

        Assert.True(blido.IsAvailable);
        Assert.Equal(0, blido.AvailableBedCount);
    }

    [Fact]
    public void BuildingBlock_TakesPrecedenceOverItsRoomBlocks()
    {
        BlockedPeriod[] blocks =
        [
            new() { Scope = BlockScope.Building, BuildingId = Blido.Id },
            new() { Scope = BlockScope.Room, BuildingId = Blido.Id, RoomId = Room1 },
        ];

        var blido = For(BuildingExclusionCalculator.Evaluate(AllBuildings, blocks), Blido);

        Assert.False(blido.IsAvailable);
        Assert.Equal(0, blido.AvailableBedCount);
    }

    [Fact]
    public void RoomBlockInAnotherBuilding_DoesNotReduceThisBuildingsBeds()
    {
        var block = new BlockedPeriod { Scope = BlockScope.Room, BuildingId = Singo.Id, RoomId = Room3 };

        var result = BuildingExclusionCalculator.Evaluate(AllBuildings, [block]);

        Assert.Equal(6, For(result, Blido).AvailableBedCount);
        Assert.Equal(0, For(result, Singo).AvailableBedCount);
    }
}
