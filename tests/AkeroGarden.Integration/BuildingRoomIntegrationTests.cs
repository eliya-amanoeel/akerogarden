using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for Building/Room reference data against real PostgreSQL
/// (FR-13–20, NFR-14): create, soft-deactivate-without-data-loss, and the
/// no-hard-delete-when-referenced rule. Tests use unique names because the
/// fixture's database is shared across the class.
/// </summary>
public class BuildingRoomIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public BuildingRoomIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private (BuildingService buildings, RoomService rooms, ApplicationDbContext db) NewScope(out IServiceScope scope)
    {
        var services = _fixture.BuildServices(s =>
        {
            s.AddScoped<BuildingService>();
            s.AddScoped<RoomService>();
        });
        scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (
            scope.ServiceProvider.GetRequiredService<BuildingService>(),
            scope.ServiceProvider.GetRequiredService<RoomService>(),
            db);
    }

    /// <summary>Every lodging Building now requires a real PhysicalBuilding FK (FR-13, §8.14) —
    /// seeds a throwaway one for tests that only care about Building/Room behaviour.</summary>
    private static async Task<PhysicalBuilding> SeedPhysicalBuildingAsync(ApplicationDbContext db)
    {
        var pb = new PhysicalBuilding { Id = Guid.NewGuid(), Name = $"Fysisk-{Guid.NewGuid():N}" };
        db.PhysicalBuildings.Add(pb);
        await db.SaveChangesAsync();
        return pb;
    }

    [Fact]
    public async Task CreateBuilding_PersistsAndAppearsInList()
    {
        var (buildings, _, db) = NewScope(out var scope);
        using var _s = scope;
        var pb = await SeedPhysicalBuildingAsync(db);

        var name = $"Blidö-{Guid.NewGuid():N}";
        var building = new Building { Name = name, PhysicalBuildingId = pb.Id, DisplayOrder = 1, IsAccessible = false };
        await buildings.TryCreateAsync(building);

        var all = await buildings.GetAllAsync();
        Assert.Contains(all, b => b.Id == building.Id && b.Name == name && b.IsActive);
    }

    [Fact]
    public async Task Deactivate_KeepsRecord_AndFlipsFlag()
    {
        var (buildings, _, db) = NewScope(out var scope);
        using var _s = scope;
        var pb = await SeedPhysicalBuildingAsync(db);

        var building = new Building { Name = $"Singö-{Guid.NewGuid():N}", PhysicalBuildingId = pb.Id };
        await buildings.TryCreateAsync(building);

        await buildings.SetActiveAsync(building.Id, false);

        var reloaded = await buildings.GetAsync(building.Id);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.IsActive);
    }

    [Fact]
    public async Task HardDeleteBuilding_Blocked_WhenItHasRooms()
    {
        var (buildings, rooms, db) = NewScope(out var scope);
        using var _s = scope;
        var pb = await SeedPhysicalBuildingAsync(db);

        var building = new Building { Name = $"Ljusterö-{Guid.NewGuid():N}", PhysicalBuildingId = pb.Id };
        await buildings.TryCreateAsync(building);
        await rooms.CreateAsync(new Room { BuildingId = building.Id, Name = "Rum 1", BedCount = 2 });

        var result = await buildings.TryHardDeleteAsync(building.Id);

        Assert.Equal(HardDeleteResult.Blocked, result);
        Assert.NotNull(await buildings.GetAsync(building.Id));
    }

    [Fact]
    public async Task HardDeleteBuilding_Succeeds_WhenEmpty()
    {
        var (buildings, _, db) = NewScope(out var scope);
        using var _s = scope;
        var pb = await SeedPhysicalBuildingAsync(db);

        var building = new Building { Name = $"Tjockö-{Guid.NewGuid():N}", PhysicalBuildingId = pb.Id };
        await buildings.TryCreateAsync(building);

        var result = await buildings.TryHardDeleteAsync(building.Id);

        Assert.Equal(HardDeleteResult.Deleted, result);
        Assert.Null(await buildings.GetAsync(building.Id));
    }

    [Fact]
    public async Task HardDeleteRoom_Succeeds_AndLeavesBuildingDeletable()
    {
        var (buildings, rooms, db) = NewScope(out var scope);
        using var _s = scope;
        var pb = await SeedPhysicalBuildingAsync(db);

        var building = new Building { Name = $"Vätö-{Guid.NewGuid():N}", PhysicalBuildingId = pb.Id };
        await buildings.TryCreateAsync(building);
        var room = new Room { BuildingId = building.Id, Name = "Rum 1", BedCount = 2 };
        await rooms.CreateAsync(room);

        var roomResult = await rooms.TryHardDeleteAsync(room.Id);
        Assert.Equal(HardDeleteResult.Deleted, roomResult);

        // Building now has no rooms, so it becomes hard-deletable.
        var buildingResult = await buildings.TryHardDeleteAsync(building.Id);
        Assert.Equal(HardDeleteResult.Deleted, buildingResult);
    }

    [Fact]
    public async Task GetForBuilding_ReturnsRoomsInDisplayOrder()
    {
        var (buildings, rooms, db) = NewScope(out var scope);
        using var _s = scope;
        var pb = await SeedPhysicalBuildingAsync(db);

        var building = new Building { Name = $"Smedjan-{Guid.NewGuid():N}", PhysicalBuildingId = pb.Id };
        await buildings.TryCreateAsync(building);
        await rooms.CreateAsync(new Room { BuildingId = building.Id, Name = "B", BedCount = 1, DisplayOrder = 2 });
        await rooms.CreateAsync(new Room { BuildingId = building.Id, Name = "A", BedCount = 1, DisplayOrder = 1 });

        var list = await rooms.GetForBuildingAsync(building.Id);

        Assert.Equal(2, list.Count);
        Assert.Equal("A", list[0].Name);
        Assert.Equal("B", list[1].Name);
    }

    /// <summary>GetAllAsync backs the unified Byggnader tree, which loads every room across every
    /// building in one call and groups them in-memory rather than one round-trip per building.</summary>
    [Fact]
    public async Task GetAll_ReturnsRoomsAcrossDifferentBuildings()
    {
        var (buildings, rooms, db) = NewScope(out var scope);
        using var _s = scope;
        var pb = await SeedPhysicalBuildingAsync(db);

        var buildingA = new Building { Name = $"Blidö-{Guid.NewGuid():N}", PhysicalBuildingId = pb.Id };
        var buildingB = new Building { Name = $"Ljusterö-{Guid.NewGuid():N}", PhysicalBuildingId = pb.Id };
        await buildings.TryCreateAsync(buildingA);
        await buildings.TryCreateAsync(buildingB);
        var roomA = new Room { BuildingId = buildingA.Id, Name = $"Rum A-{Guid.NewGuid():N}", BedCount = 2 };
        var roomB = new Room { BuildingId = buildingB.Id, Name = $"Rum B-{Guid.NewGuid():N}", BedCount = 3 };
        await rooms.CreateAsync(roomA);
        await rooms.CreateAsync(roomB);

        var all = await rooms.GetAllAsync();

        Assert.Contains(all, r => r.Id == roomA.Id && r.BuildingId == buildingA.Id);
        Assert.Contains(all, r => r.Id == roomB.Id && r.BuildingId == buildingB.Id);
    }
}
