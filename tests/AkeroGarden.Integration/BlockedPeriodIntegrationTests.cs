using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for block CRUD against real PostgreSQL (FR-40, §3.3): the three scopes
/// persist, reference existence is enforced server-side, and delete is a hard delete.
/// </summary>
public class BlockedPeriodIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public BlockedPeriodIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private BlockedPeriodService NewService(out IServiceScope scope, out ApplicationDbContext db)
    {
        var services = _fixture.BuildServices(s => s.AddScoped<BlockedPeriodService>());
        scope = services.CreateScope();
        db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return scope.ServiceProvider.GetRequiredService<BlockedPeriodService>();
    }

    private static async Task<(Building building, Room room)> SeedBuildingRoomAsync(ApplicationDbContext db)
    {
        var b = new Building { Id = Guid.NewGuid(), Name = $"Hus-{Guid.NewGuid():N}" };
        var r = new Room { Id = Guid.NewGuid(), BuildingId = b.Id, Name = "Rum 1", BedCount = 2 };
        db.Buildings.Add(b);
        db.Rooms.Add(r);
        await db.SaveChangesAsync();
        return (b, r);
    }

    [Fact]
    public async Task Create_WholeProperty_Persists()
    {
        var service = NewService(out var scope, out _);
        using var _s = scope;

        var result = await service.TryCreateAsync(new BlockedPeriod
        {
            Scope = BlockScope.WholeProperty,
            StartDate = new DateOnly(2026, 7, 1),
            EndDate = new DateOnly(2026, 7, 3),
            Reason = "Underhåll"
        });

        Assert.True(result.Succeeded);
        Assert.Single(await service.GetAllAsync(), p => p.Reason == "Underhåll");
    }

    [Fact]
    public async Task Create_RoomScope_Persists_WhenRoomBelongsToBuilding()
    {
        var service = NewService(out var scope, out var db);
        using var _s = scope;
        var (building, room) = await SeedBuildingRoomAsync(db);

        var result = await service.TryCreateAsync(new BlockedPeriod
        {
            Scope = BlockScope.Room,
            BuildingId = building.Id,
            RoomId = room.Id,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 2)
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Create_RoomScope_Rejected_WhenRoomNotInBuilding()
    {
        var service = NewService(out var scope, out var db);
        using var _s = scope;
        var (building, _) = await SeedBuildingRoomAsync(db);
        var (_, otherRoom) = await SeedBuildingRoomAsync(db);

        var result = await service.TryCreateAsync(new BlockedPeriod
        {
            Scope = BlockScope.Room,
            BuildingId = building.Id,
            RoomId = otherRoom.Id, // belongs to a different building
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 2)
        });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Create_BuildingScope_Rejected_WhenBuildingMissing()
    {
        var service = NewService(out var scope, out _);
        using var _s = scope;

        var result = await service.TryCreateAsync(new BlockedPeriod
        {
            Scope = BlockScope.Building,
            BuildingId = Guid.NewGuid(), // does not exist
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 2)
        });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Create_Rejected_WhenEndBeforeStart()
    {
        var service = NewService(out var scope, out _);
        using var _s = scope;

        var result = await service.TryCreateAsync(new BlockedPeriod
        {
            Scope = BlockScope.WholeProperty,
            StartDate = new DateOnly(2026, 8, 5),
            EndDate = new DateOnly(2026, 8, 1)
        });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Delete_RemovesBlock()
    {
        var service = NewService(out var scope, out _);
        using var _s = scope;

        await service.TryCreateAsync(new BlockedPeriod
        {
            Scope = BlockScope.WholeProperty,
            StartDate = new DateOnly(2026, 9, 1),
            EndDate = new DateOnly(2026, 9, 2),
            Reason = "DeleteMe"
        });
        var created = (await service.GetAllAsync()).First(p => p.Reason == "DeleteMe");

        await service.DeleteAsync(created.Id);

        Assert.DoesNotContain(await service.GetAllAsync(), p => p.Id == created.Id);
    }
}
