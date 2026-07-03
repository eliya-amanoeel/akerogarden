using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the pure room-assignment logic (NFR-26, FR-56, §3.3): which rooms the
/// completion form may offer (blocked / fully-occupied excluded) and which assignments conflict
/// (assigned beyond bed count over overlapping dates).
/// </summary>
public class RoomAssignmentTests
{
    private static readonly Guid BuildingA = Guid.NewGuid();
    private static readonly Guid BuildingB = Guid.NewGuid();
    private static readonly RoomInfo Room1 = new(Guid.NewGuid(), "Rum 1", 4, BuildingA, "Blidö");
    private static readonly RoomInfo Room2 = new(Guid.NewGuid(), "Rum 2", 2, BuildingA, "Blidö");
    private static readonly RoomInfo Room3 = new(Guid.NewGuid(), "Rum 3", 3, BuildingB, "Singö");

    private static readonly RoomInfo[] AllRooms = [Room1, Room2, Room3];
    private static readonly Dictionary<Guid, int> NoOccupancy = [];

    // ── Exclusion ─────────────────────────────────────────────────────────────────

    [Fact]
    public void NoBlocksNoOccupancy_AllAvailable()
    {
        var available = RoomExclusionCalculator.AvailableRooms(AllRooms, [], NoOccupancy);
        Assert.Equal(3, available.Count);
    }

    [Fact]
    public void WholePropertyBlock_ExcludesEverything()
    {
        var block = new BlockedPeriod { Scope = BlockScope.WholeProperty };
        Assert.Empty(RoomExclusionCalculator.AvailableRooms(AllRooms, [block], NoOccupancy));
    }

    [Fact]
    public void BuildingBlock_ExcludesThatBuildingsRooms()
    {
        var block = new BlockedPeriod { Scope = BlockScope.Building, BuildingId = BuildingA };
        var available = RoomExclusionCalculator.AvailableRooms(AllRooms, [block], NoOccupancy);
        Assert.Equal([Room3.Id], available.Select(r => r.Id));
    }

    [Fact]
    public void RoomBlock_ExcludesThatRoom()
    {
        var block = new BlockedPeriod { Scope = BlockScope.Room, BuildingId = BuildingA, RoomId = Room1.Id };
        var available = RoomExclusionCalculator.AvailableRooms(AllRooms, [block], NoOccupancy);
        Assert.DoesNotContain(available, r => r.Id == Room1.Id);
        Assert.Equal(2, available.Count);
    }

    [Fact]
    public void FullyOccupiedRoom_IsExcluded_ButPartialRemains()
    {
        // Room2 (2 beds) fully taken by others; Room1 (4 beds) has 2 of 4 taken → still available.
        var occupancy = new Dictionary<Guid, int> { [Room2.Id] = 2, [Room1.Id] = 2 };
        var eval = RoomExclusionCalculator.Evaluate(AllRooms, [], occupancy);

        Assert.Equal(RoomUnavailableReason.FullyOccupied, eval.Single(r => r.Room.Id == Room2.Id).Reason);
        Assert.True(eval.Single(r => r.Room.Id == Room1.Id).IsAvailable);
    }

    // ── Conflicts ─────────────────────────────────────────────────────────────────

    private static readonly Dictionary<Guid, RoomInfo> RoomsById =
        AllRooms.ToDictionary(r => r.Id);

    [Fact]
    public void WithinBedCount_NoConflict()
    {
        var assigned = new Dictionary<Guid, int> { [Room1.Id] = 4 }; // exactly bed count
        Assert.Empty(RoomConflictDetector.Detect(assigned, RoomsById, NoOccupancy));
    }

    [Fact]
    public void BeyondBedCount_SameBooking_IsConflict()
    {
        var assigned = new Dictionary<Guid, int> { [Room2.Id] = 3 }; // 3 into a 2-bed room
        var conflict = Assert.Single(RoomConflictDetector.Detect(assigned, RoomsById, NoOccupancy));
        Assert.Equal(Room2.Id, conflict.RoomId);
        Assert.Equal(3, conflict.AssignedBeds);
        Assert.Equal(2, conflict.BedCount);
    }

    [Fact]
    public void ThisPlusOtherBookings_ExceedingBedCount_IsConflict()
    {
        var assigned = new Dictionary<Guid, int> { [Room1.Id] = 3 };
        var others = new Dictionary<Guid, int> { [Room1.Id] = 2 }; // 3 + 2 = 5 > 4 beds
        var conflict = Assert.Single(RoomConflictDetector.Detect(assigned, RoomsById, others));
        Assert.Equal(5, conflict.AssignedBeds);
    }

    [Fact]
    public void UnknownRoom_IsIgnored()
    {
        var assigned = new Dictionary<Guid, int> { [Guid.NewGuid()] = 9 };
        Assert.Empty(RoomConflictDetector.Detect(assigned, RoomsById, NoOccupancy));
    }
}
