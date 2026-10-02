using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for block CRUD against real PostgreSQL (FR-40, §3.3): the three scopes
/// persist, reference existence is enforced server-side, delete is a hard delete, and a block may
/// not close a building that a booking which is not finished already has assigned (FR-102).
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
        // A lodging Building requires a PhysicalBuilding (§8.2, FR-13) — seed one and link it.
        var pb = new PhysicalBuilding { Id = Guid.NewGuid(), Name = $"Fysisk-{Guid.NewGuid():N}", IsActive = true };
        var b = new Building { Id = Guid.NewGuid(), Name = $"Hus-{Guid.NewGuid():N}", PhysicalBuildingId = pb.Id };
        var r = new Room { Id = Guid.NewGuid(), BuildingId = b.Id, Name = "Rum 1", BedCount = 2 };
        db.PhysicalBuildings.Add(pb);
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

    // ── FR-102 in reverse: an assigned building cannot be blocked ────────────────

    // Relative to today and years out, so these windows never meet the literal 2026 dates the tests
    // above use for their whole-property blocks. Each test takes its own window: the whole-property
    // rule looks at every booking, so a shared window would let one test's booking refuse another's.
    private static DateOnly Window(int index) =>
        DateOnly.FromDateTime(DateTime.UtcNow).AddYears(6).AddDays(index * 20);

    private static async Task<Booking> SeedAssignedBookingAsync(
        ApplicationDbContext db, Building building, DateOnly checkIn, BookingStatus status)
    {
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = $"AG-T-{Guid.NewGuid():N}"[..20],
            BookingType = BookingType.StayRequest,
            Status = status,
            ContactName = "Tilldelad",
            ContactEmail = $"tilldelad-{Guid.NewGuid():N}@test.se",
            CheckIn = checkIn,
            CheckOut = checkIn.AddDays(3),
            OvernightGuestCount = 2,
            MealPlan = MealPlan.SelfCateringAll,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Bookings.Add(booking);
        db.BookingBuildingAssignments.Add(new BookingBuildingAssignment
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            BuildingId = building.Id,
            BuildingNameSnapshot = building.Name,
            PhysicalBuildingNameSnapshot = "Fysisk",
            AssignedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return booking;
    }

    [Theory]
    [InlineData(BookingStatus.Pending, 0)]
    [InlineData(BookingStatus.Approved, 1)]
    [InlineData(BookingStatus.DetailsComplete, 2)]
    [InlineData(BookingStatus.Active, 3)]
    public async Task Building_block_over_an_assigned_building_is_refused_and_names_the_booking(
        BookingStatus status, int window)
    {
        var service = NewService(out var scope, out var db);
        using var _s = scope;
        var (building, _) = await SeedBuildingRoomAsync(db);
        var checkIn = Window(window);
        var booking = await SeedAssignedBookingAsync(db, building, checkIn, status);

        var result = await service.TryCreateAsync(new BlockedPeriod
        {
            Scope = BlockScope.Building,
            BuildingId = building.Id,
            StartDate = checkIn.AddDays(1),
            EndDate = checkIn.AddDays(1),
        });

        Assert.False(result.Succeeded);
        Assert.Equal(
            $"Byggnaden {building.Name} är tilldelad {booking.BookingReference} under perioden. "
                + "Avmarkera byggnaden på bokningen innan du blockerar den.",
            result.Error);
        Assert.DoesNotContain(await service.GetAllAsync(), p => p.BuildingId == building.Id);
    }

    [Theory]
    [InlineData(BookingStatus.Completed, 4)]
    [InlineData(BookingStatus.Cancelled, 5)]
    [InlineData(BookingStatus.Denied, 6)]
    public async Task Building_block_over_a_finished_bookings_building_is_allowed(BookingStatus status, int window)
    {
        var service = NewService(out var scope, out var db);
        using var _s = scope;
        var (building, _) = await SeedBuildingRoomAsync(db);
        var checkIn = Window(window);
        await SeedAssignedBookingAsync(db, building, checkIn, status);

        var result = await service.TryCreateAsync(new BlockedPeriod
        {
            Scope = BlockScope.Building,
            BuildingId = building.Id,
            StartDate = checkIn,
            EndDate = checkIn.AddDays(2),
        });

        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public async Task Building_block_starting_on_the_check_out_day_is_allowed()
    {
        // Same overlap as assignment (FR-102): the check-out day is a departure, not a night held.
        var service = NewService(out var scope, out var db);
        using var _s = scope;
        var (building, _) = await SeedBuildingRoomAsync(db);
        var checkIn = Window(7);
        var booking = await SeedAssignedBookingAsync(db, building, checkIn, BookingStatus.Approved);

        var result = await service.TryCreateAsync(new BlockedPeriod
        {
            Scope = BlockScope.Building,
            BuildingId = building.Id,
            StartDate = booking.CheckOut!.Value,
            EndDate = booking.CheckOut.Value.AddDays(2),
        });

        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public async Task Room_block_inside_an_assigned_building_is_allowed()
    {
        // A room block only removes beds — the approval capacity warning reports that (FR-103).
        var service = NewService(out var scope, out var db);
        using var _s = scope;
        var (building, room) = await SeedBuildingRoomAsync(db);
        var checkIn = Window(8);
        await SeedAssignedBookingAsync(db, building, checkIn, BookingStatus.Approved);

        var result = await service.TryCreateAsync(new BlockedPeriod
        {
            Scope = BlockScope.Room,
            BuildingId = building.Id,
            RoomId = room.Id,
            StartDate = checkIn,
            EndDate = checkIn.AddDays(2),
        });

        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public async Task Whole_property_block_is_refused_while_any_booking_has_a_building_assigned()
    {
        var service = NewService(out var scope, out var db);
        using var _s = scope;
        var (building, _) = await SeedBuildingRoomAsync(db);
        var checkIn = Window(9);
        var booking = await SeedAssignedBookingAsync(db, building, checkIn, BookingStatus.Approved);

        var result = await service.TryCreateAsync(new BlockedPeriod
        {
            Scope = BlockScope.WholeProperty,
            StartDate = checkIn,
            EndDate = checkIn,
        });

        Assert.False(result.Succeeded);
        Assert.Equal(
            $"Hela anläggningen kan inte blockeras: {booking.BookingReference} har byggnader tilldelade under perioden. "
                + "Avmarkera byggnaderna på bokningen eller välj andra datum.",
            result.Error);
    }

    [Fact]
    public async Task Moving_an_existing_block_onto_an_assigned_building_is_refused_too()
    {
        // The edit path runs the same check — otherwise the rule is one "Redigera" away from moot.
        var service = NewService(out var scope, out var db);
        using var _s = scope;
        var (building, _) = await SeedBuildingRoomAsync(db);
        var checkIn = Window(10);
        var booking = await SeedAssignedBookingAsync(db, building, checkIn, BookingStatus.Pending);

        var block = new BlockedPeriod
        {
            Scope = BlockScope.Building,
            BuildingId = building.Id,
            StartDate = checkIn.AddDays(10),
            EndDate = checkIn.AddDays(12),
            Reason = "Flyttas",
        };
        Assert.True((await service.TryCreateAsync(block)).Succeeded);

        var result = await service.TryUpdateAsync(new BlockedPeriod
        {
            Id = block.Id,
            Scope = BlockScope.Building,
            BuildingId = building.Id,
            StartDate = checkIn,
            EndDate = checkIn.AddDays(1),
            Reason = "Flyttas",
        });

        Assert.False(result.Succeeded);
        Assert.Contains(booking.BookingReference, result.Error);
        var stored = await service.GetAsync(block.Id);
        Assert.Equal(checkIn.AddDays(10), stored!.StartDate); // unchanged
    }

    [Fact]
    public async Task Several_conflicting_bookings_are_all_named_earliest_first()
    {
        var service = NewService(out var scope, out var db);
        using var _s = scope;
        var (building, _) = await SeedBuildingRoomAsync(db);
        var checkIn = Window(11);
        var later = await SeedAssignedBookingAsync(db, building, checkIn.AddDays(4), BookingStatus.Approved);
        var earlier = await SeedAssignedBookingAsync(db, building, checkIn, BookingStatus.Pending);

        var conflicts = await service.GetAssignmentConflictsAsync(new BlockedPeriod
        {
            Scope = BlockScope.Building,
            BuildingId = building.Id,
            StartDate = checkIn,
            EndDate = checkIn.AddDays(6),
        });

        Assert.Equal(new[] { earlier.Id, later.Id }, conflicts.Select(c => c.BookingId));
    }
}
