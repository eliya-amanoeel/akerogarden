using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the rewritten Room (FR-20) and Building (FR-16) hard-delete guards
/// against real PostgreSQL.
///
/// These exist because the pre-existing unit tests could not have caught the defect they guard.
/// Those tests assert <c>CanHardDelete(0) == true</c> and <c>CanHardDelete(n) == false</c> — a pure
/// function of a count, which passes no matter what is being counted, or whether the thing being
/// counted still exists. When per-participant room assignment was removed, the room guard's query
/// would have returned zero for ever and every room become hard-deletable, with the whole unit
/// suite still green. So these tests assert a <b>Blocked result and that the record survives</b>,
/// against real referencing rows, rather than exercising the decision function in isolation.
///
/// Tests use unique names because the fixture's database is shared across the class.
/// </summary>
public class ReferenceDataDeleteGuardIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public ReferenceDataDeleteGuardIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private IServiceProvider NewServices() => _fixture.BuildServices(s =>
    {
        s.AddScoped<PhysicalBuildingService>();
        s.AddScoped<BuildingService>();
        s.AddScoped<RoomService>();
    });

    private static async Task<(Guid buildingId, Guid roomId)> SeedBuildingWithRoomAsync(IServiceScope scope)
    {
        var physical = scope.ServiceProvider.GetRequiredService<PhysicalBuildingService>();
        var buildings = scope.ServiceProvider.GetRequiredService<BuildingService>();
        var rooms = scope.ServiceProvider.GetRequiredService<RoomService>();

        var pb = new PhysicalBuilding { Name = $"Fysisk-{Guid.NewGuid():N}" };
        await physical.CreateAsync(pb);

        var building = new Building { Name = $"Byggnad-{Guid.NewGuid():N}", PhysicalBuildingId = pb.Id };
        await buildings.TryCreateAsync(building);

        var room = new Room { Name = $"Rum-{Guid.NewGuid():N}", BuildingId = building.Id, BedCount = 4 };
        await rooms.CreateAsync(room);

        return (building.Id, room.Id);
    }

    // ── Room (FR-20) — repointed at blocked-period integrity ──────────────────────

    [Fact]
    public async Task Room_HardDelete_Blocked_WhenARoomScopedBlockReferencesIt()
    {
        var services = NewServices();
        using var scope = services.CreateScope();
        var (buildingId, roomId) = await SeedBuildingWithRoomAsync(scope);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.BlockedPeriods.Add(new BlockedPeriod
        {
            Scope = BlockScope.Room,
            BuildingId = buildingId,
            RoomId = roomId,
            StartDate = new DateOnly(2027, 3, 1),
            EndDate = new DateOnly(2027, 3, 5),
            Reason = "Renovering",
        });
        await db.SaveChangesAsync();

        var rooms = scope.ServiceProvider.GetRequiredService<RoomService>();
        var result = await rooms.TryHardDeleteAsync(roomId);

        // The block would otherwise be orphaned: BlockedPeriod.RoomId is a logical reference, not
        // an FK, so the database itself would not have stopped this.
        Assert.Equal(HardDeleteResult.Blocked, result);
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<RoomService>().GetAsync(roomId));
    }

    [Fact]
    public async Task Room_HardDelete_Succeeds_WhenNoBlockReferencesIt()
    {
        var services = NewServices();
        using var scope = services.CreateScope();
        var (_, roomId) = await SeedBuildingWithRoomAsync(scope);

        var rooms = scope.ServiceProvider.GetRequiredService<RoomService>();
        var result = await rooms.TryHardDeleteAsync(roomId);

        Assert.Equal(HardDeleteResult.Deleted, result);
        Assert.Null(await rooms.GetAsync(roomId));
    }

    // ── Building (FR-16) ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Building_HardDelete_Blocked_WhenAssignedToABooking()
    {
        var services = NewServices();
        using var scope = services.CreateScope();
        var (buildingId, roomId) = await SeedBuildingWithRoomAsync(scope);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Remove the room first, so the room layer of the guard cannot be what blocks the delete —
        // this test must prove the booking-assignment layer is load-bearing on its own.
        db.Rooms.Remove(await db.Rooms.FindAsync(roomId) ?? throw new InvalidOperationException());
        await db.SaveChangesAsync();

        var booking = new Booking
        {
            BookingType = BookingType.StayRequest,
            Status = BookingStatus.Pending,
            ContactName = "Testgrupp",
            ContactEmail = "testgrupp@example.com",
            BookingReference = $"AG-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}",
            CheckIn = new DateOnly(2027, 5, 1),
            CheckOut = new DateOnly(2027, 5, 3),
            OvernightGuestCount = 4,
        };
        booking.BuildingAssignments.Add(new BookingBuildingAssignment
        {
            BuildingId = buildingId,
            BuildingNameSnapshot = "Byggnad",
            PhysicalBuildingNameSnapshot = "Fysisk",
            AssignedAt = DateTime.UtcNow,
        });
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var buildings = scope.ServiceProvider.GetRequiredService<BuildingService>();
        var result = await buildings.TryHardDeleteAsync(buildingId);

        Assert.Equal(HardDeleteResult.Blocked, result);
        Assert.NotNull(await buildings.GetAsync(buildingId));
    }

    [Fact]
    public async Task Building_HardDelete_Blocked_WhenABuildingScopedBlockReferencesIt()
    {
        var services = NewServices();
        using var scope = services.CreateScope();
        var (buildingId, roomId) = await SeedBuildingWithRoomAsync(scope);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Again strip the room, so the block layer is proven on its own rather than riding on the
        // room count — the coincidence that hid the rotting participant layer for two phases.
        db.Rooms.Remove(await db.Rooms.FindAsync(roomId) ?? throw new InvalidOperationException());
        db.BlockedPeriods.Add(new BlockedPeriod
        {
            Scope = BlockScope.Building,
            BuildingId = buildingId,
            StartDate = new DateOnly(2027, 4, 1),
            EndDate = new DateOnly(2027, 4, 10),
            Reason = "Stängt",
        });
        await db.SaveChangesAsync();

        var buildings = scope.ServiceProvider.GetRequiredService<BuildingService>();
        var result = await buildings.TryHardDeleteAsync(buildingId);

        Assert.Equal(HardDeleteResult.Blocked, result);
        Assert.NotNull(await buildings.GetAsync(buildingId));
    }

    [Fact]
    public async Task Building_HardDelete_Blocked_WhenARoomScopedBlockReferencesOneOfItsRooms()
    {
        var services = NewServices();
        using var scope = services.CreateScope();
        var (buildingId, roomId) = await SeedBuildingWithRoomAsync(scope);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.BlockedPeriods.Add(new BlockedPeriod
        {
            Scope = BlockScope.Room,
            BuildingId = buildingId,
            RoomId = roomId,
            StartDate = new DateOnly(2027, 6, 1),
            EndDate = new DateOnly(2027, 6, 2),
            Reason = "Vattenskada",
        });
        await db.SaveChangesAsync();

        var buildings = scope.ServiceProvider.GetRequiredService<BuildingService>();
        var result = await buildings.TryHardDeleteAsync(buildingId);

        Assert.Equal(HardDeleteResult.Blocked, result);
        Assert.NotNull(await buildings.GetAsync(buildingId));
    }
}
