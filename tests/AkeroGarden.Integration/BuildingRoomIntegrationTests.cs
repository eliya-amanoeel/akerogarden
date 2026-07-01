using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;
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

    [Fact]
    public async Task CreateBuilding_PersistsAndAppearsInList()
    {
        var (buildings, _, _) = NewScope(out var scope);
        using var _s = scope;

        var name = $"Blidö-{Guid.NewGuid():N}";
        var building = new Building { Name = name, DisplayOrder = 1, IsAccessible = false };
        await buildings.CreateAsync(building);

        var all = await buildings.GetAllAsync();
        Assert.Contains(all, b => b.Id == building.Id && b.Name == name && b.IsActive);
    }

    [Fact]
    public async Task Deactivate_KeepsRecord_AndFlipsFlag()
    {
        var (buildings, _, _) = NewScope(out var scope);
        using var _s = scope;

        var building = new Building { Name = $"Singö-{Guid.NewGuid():N}" };
        await buildings.CreateAsync(building);

        await buildings.SetActiveAsync(building.Id, false);

        var reloaded = await buildings.GetAsync(building.Id);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.IsActive);
    }

    [Fact]
    public async Task HardDeleteBuilding_Blocked_WhenItHasRooms()
    {
        var (buildings, rooms, _) = NewScope(out var scope);
        using var _s = scope;

        var building = new Building { Name = $"Ljusterö-{Guid.NewGuid():N}" };
        await buildings.CreateAsync(building);
        await rooms.CreateAsync(new Room { BuildingId = building.Id, Name = "Rum 1", BedCount = 2 });

        var result = await buildings.TryHardDeleteAsync(building.Id);

        Assert.Equal(HardDeleteResult.Blocked, result);
        Assert.NotNull(await buildings.GetAsync(building.Id));
    }

    [Fact]
    public async Task HardDeleteBuilding_Succeeds_WhenEmpty()
    {
        var (buildings, _, _) = NewScope(out var scope);
        using var _s = scope;

        var building = new Building { Name = $"Tjockö-{Guid.NewGuid():N}" };
        await buildings.CreateAsync(building);

        var result = await buildings.TryHardDeleteAsync(building.Id);

        Assert.Equal(HardDeleteResult.Deleted, result);
        Assert.Null(await buildings.GetAsync(building.Id));
    }

    [Fact]
    public async Task HardDeleteRoom_Succeeds_AndLeavesBuildingDeletable()
    {
        var (buildings, rooms, _) = NewScope(out var scope);
        using var _s = scope;

        var building = new Building { Name = $"Vätö-{Guid.NewGuid():N}" };
        await buildings.CreateAsync(building);
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
        var (buildings, rooms, _) = NewScope(out var scope);
        using var _s = scope;

        var building = new Building { Name = $"Smedjan-{Guid.NewGuid():N}" };
        await buildings.CreateAsync(building);
        await rooms.CreateAsync(new Room { BuildingId = building.Id, Name = "B", BedCount = 1, DisplayOrder = 2 });
        await rooms.CreateAsync(new Room { BuildingId = building.Id, Name = "A", BedCount = 1, DisplayOrder = 1 });

        var list = await rooms.GetForBuildingAsync(building.Id);

        Assert.Equal(2, list.Count);
        Assert.Equal("A", list[0].Name);
        Assert.Equal("B", list[1].Name);
    }
}
