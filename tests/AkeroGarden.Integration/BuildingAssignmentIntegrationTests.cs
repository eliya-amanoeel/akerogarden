using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;
using AkeroGarden.Web.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for admin building assignment against real PostgreSQL (FR-102/103, §8.16):
/// the snapshots taken at assignment time (NFR-16), the approval precondition (D1), the rejection of
/// buildings closed by a block (FR-102), and editability after approval (D5).
///
/// <para>Assertions here check a <b>blocked/failed result and that the state did not change</b>,
/// rather than merely that the call returned — the FR-102 guard exists precisely because the
/// protection it replaces used to be an accidental side effect of the completion form's room picker,
/// and a test that only asserted "no exception" would pass against no guard at all.</para>
///
/// Tests use unique names because the fixture's database is shared across the class.
/// </summary>
public class BuildingAssignmentIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public BuildingAssignmentIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private IServiceProvider NewProvider()
    {
        var fake = new FakeEmailService();
        return _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(fake);
            s.AddScoped<BookingEmailService>();
            s.AddScoped<BookingService>();
            s.AddScoped<CleaningZoneService>();
            s.AddScoped<BuildingService>();
            s.AddScoped<RoomService>();
            s.Configure<BookingEmailOptions>(o =>
            {
                o.PublicBaseUrl = "https://akerogarden.se";
                o.AdminBaseUrl = "https://admin.akerogarden.se";
                o.AdminNotificationEmail = "admin@test.se";
                o.ContactEmail = "info@test.se";
            });
        });
    }

    // Every test gets its own date window. The class shares one database (IClassFixture), and a
    // WholeProperty block is not scoped to a building — written over a window shared with other
    // tests it would silently block their assignments too, which is exactly what happened when
    // these dates were class-level constants: each test passed alone and failed in the class.
    private static int _window;

    private static (DateOnly In, DateOnly Out) NextWindow()
    {
        var offset = Interlocked.Increment(ref _window) * 30;
        var start = new DateOnly(2027, 1, 1).AddDays(offset);
        return (start, start.AddDays(3));
    }

    private sealed record Seeded(
        Guid BookingId, Guid BuildingId, string BuildingName, Guid RoomId, string PhysicalName,
        DateOnly CheckIn, DateOnly CheckOut);

    private static async Task<Seeded> SeedAsync(IServiceScope scope, int bedCount = 8, int overnightGuests = 4)
    {
        var (checkIn, checkOut) = NextWindow();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var buildings = scope.ServiceProvider.GetRequiredService<BuildingService>();
        var rooms = scope.ServiceProvider.GetRequiredService<RoomService>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();

        var physical = new PhysicalBuilding { Id = Guid.NewGuid(), Name = $"Fysisk-{Guid.NewGuid():N}" };
        db.PhysicalBuildings.Add(physical);
        await db.SaveChangesAsync();

        var building = new Building { Name = $"Byggnad-{Guid.NewGuid():N}", PhysicalBuildingId = physical.Id };
        await buildings.TryCreateAsync(building);
        var room = new Room { Name = "Rum 1", BedCount = bedCount, BuildingId = building.Id };
        await rooms.CreateAsync(room);

        var booking = await bookings.CreateStayRequestAsync(new StayRequestInput
        {
            ContactName = "Grupp",
            ContactEmail = $"grupp-{Guid.NewGuid():N}@test.se",
            CheckIn = checkIn,
            CheckOut = checkOut,
            OvernightGuestCount = overnightGuests,
            MealPlan = MealPlan.SelfCateringAll,
            AcceptTerms = true,
        }, createdByAdmin: true);

        return new Seeded(booking.Id, building.Id, building.Name, room.Id, physical.Name, checkIn, checkOut);
    }

    // ── Assignment + snapshots (NFR-16) ───────────────────────────────────────────

    [Fact]
    public async Task Assigning_PersistsBothNameSnapshots()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var s = await SeedAsync(scope);
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();

        var result = await bookings.SetBuildingAssignmentsAsync(s.BookingId, [s.BuildingId]);

        Assert.True(result.Succeeded, result.Error);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = Assert.Single(await db.BookingBuildingAssignments.Where(a => a.BookingId == s.BookingId).ToListAsync());
        Assert.Equal(s.BuildingName, row.BuildingNameSnapshot);
        Assert.Equal(s.PhysicalName, row.PhysicalBuildingNameSnapshot);
    }

    [Fact]
    public async Task RenamingTheBuildingLater_DoesNotRewriteTheSnapshot()
    {
        // The point of the snapshot (NFR-16): what was agreed survives edits to reference data.
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var s = await SeedAsync(scope);
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await bookings.SetBuildingAssignmentsAsync(s.BookingId, [s.BuildingId]);

        var building = await db.Buildings.FindAsync(s.BuildingId);
        building!.Name = "Nytt namn efter tilldelning";
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var row = Assert.Single(await db.BookingBuildingAssignments.Where(a => a.BookingId == s.BookingId).ToListAsync());
        Assert.Equal(s.BuildingName, row.BuildingNameSnapshot);
        Assert.NotEqual("Nytt namn efter tilldelning", row.BuildingNameSnapshot);
    }

    [Fact]
    public async Task Reassigning_ReplacesRatherThanAccumulates()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var s = await SeedAsync(scope);
        var second = await SeedAsync(scope);
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await bookings.SetBuildingAssignmentsAsync(s.BookingId, [s.BuildingId]);
        await bookings.SetBuildingAssignmentsAsync(s.BookingId, [second.BuildingId]);

        db.ChangeTracker.Clear();
        var row = Assert.Single(await db.BookingBuildingAssignments.Where(a => a.BookingId == s.BookingId).ToListAsync());
        Assert.Equal(second.BuildingId, row.BuildingId);
    }

    // ── FR-102: a blocked building cannot be assigned ─────────────────────────────

    [Fact]
    public async Task BuildingScopedBlockOverTheStay_RejectsTheAssignment()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var s = await SeedAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();

        db.BlockedPeriods.Add(new BlockedPeriod
        {
            Scope = BlockScope.Building,
            BuildingId = s.BuildingId,
            StartDate = s.CheckIn,
            EndDate = s.CheckOut,
            Reason = "Renovering",
        });
        await db.SaveChangesAsync();

        var result = await bookings.SetBuildingAssignmentsAsync(s.BookingId, [s.BuildingId]);

        Assert.False(result.Succeeded);
        Assert.Contains(s.BuildingName, result.Error);
        // Rejected, not silently skipped — nothing was written.
        Assert.Empty(await db.BookingBuildingAssignments.Where(a => a.BookingId == s.BookingId).ToListAsync());
    }

    [Fact]
    public async Task WholePropertyBlockOverTheStay_RejectsTheAssignment()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var s = await SeedAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();

        db.BlockedPeriods.Add(new BlockedPeriod
        {
            Scope = BlockScope.WholeProperty,
            StartDate = s.CheckIn,
            EndDate = s.CheckOut,
            Reason = "Stängt",
        });
        await db.SaveChangesAsync();

        var result = await bookings.SetBuildingAssignmentsAsync(s.BookingId, [s.BuildingId]);

        Assert.False(result.Succeeded);
        Assert.Empty(await db.BookingBuildingAssignments.Where(a => a.BookingId == s.BookingId).ToListAsync());
    }

    [Fact]
    public async Task RoomScopedBlock_DoesNotPreventAssignment_ButReducesTheBedCount()
    {
        // FR-102 draws this distinction deliberately: one closed room does not take a building out
        // of service, it only reduces what the building offers.
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var s = await SeedAsync(scope, bedCount: 8);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();

        db.BlockedPeriods.Add(new BlockedPeriod
        {
            Scope = BlockScope.Room,
            BuildingId = s.BuildingId,
            RoomId = s.RoomId,
            StartDate = s.CheckIn,
            EndDate = s.CheckOut,
            Reason = "Vattenskada",
        });
        await db.SaveChangesAsync();

        var result = await bookings.SetBuildingAssignmentsAsync(s.BookingId, [s.BuildingId]);
        Assert.True(result.Succeeded, result.Error);

        var evaluated = await bookings.GetAssignableBuildingsAsync(s.BookingId);
        var target = evaluated.Single(b => b.Building.Id == s.BuildingId);
        Assert.True(target.IsAvailable);
        Assert.Equal(0, target.AvailableBedCount); // its only room is closed for these dates
    }

    [Fact]
    public async Task BlockOutsideTheStayDates_DoesNotPreventAssignment()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var s = await SeedAsync(scope);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();

        db.BlockedPeriods.Add(new BlockedPeriod
        {
            Scope = BlockScope.Building,
            BuildingId = s.BuildingId,
            StartDate = s.CheckOut.AddDays(10),
            EndDate = s.CheckOut.AddDays(20),
            Reason = "Senare renovering",
        });
        await db.SaveChangesAsync();

        {
            var r0 = await bookings.SetBuildingAssignmentsAsync(s.BookingId, [s.BuildingId]);
            Assert.True(r0.Succeeded, r0.Error);
        }
    }

    // ── FR-38/103/D1: assignment is a precondition of approval ────────────────────

    [Fact]
    public async Task Approving_WithoutAnAssignment_IsBlocked_AndLeavesTheBookingPending()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var s = await SeedAsync(scope, overnightGuests: 4);
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var result = await bookings.ApproveAsync(s.BookingId, DbFixture.EstimatedPrice);

        Assert.False(result.Succeeded);
        Assert.Contains("byggnad", result.Error!, StringComparison.OrdinalIgnoreCase);

        db.ChangeTracker.Clear();
        var booking = await db.Bookings.FindAsync(s.BookingId);
        // Genuinely not approved: status unchanged and no completion token was issued.
        Assert.Equal(BookingStatus.Pending, booking!.Status);
        Assert.Null(booking.CompletionToken);
    }

    [Fact]
    public async Task Approving_AfterAssigning_Succeeds()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var s = await SeedAsync(scope);
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();

        {
            var r0 = await bookings.SetBuildingAssignmentsAsync(s.BookingId, [s.BuildingId]);
            Assert.True(r0.Succeeded, r0.Error);
        }
        {
            var r1 = await bookings.ApproveAsync(s.BookingId, DbFixture.EstimatedPrice);
            Assert.True(r1.Succeeded, r1.Error);
        }
    }

    // D6's "no overnight guests → no assignment needed" exemption is not implemented and not
    // tested, by decision (2026-08-06): it is unreachable. General inquiries are rejected earlier in
    // ApproveAsync by the BookingType check, and StayRequestInput.OvernightGuestCount is
    // [Range(1, 1000)], so a stay request always has overnight guests. See ApproveAsync.

    // ── D5: editable after approval ───────────────────────────────────────────────

    [Fact]
    public async Task AssignmentRemainsEditableAfterApproval()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var s = await SeedAsync(scope);
        var other = await SeedAsync(scope);
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await bookings.SetBuildingAssignmentsAsync(s.BookingId, [s.BuildingId]);
        {
            var r1 = await bookings.ApproveAsync(s.BookingId, DbFixture.EstimatedPrice);
            Assert.True(r1.Succeeded, r1.Error);
        }

        {
            var r0 = await bookings.SetBuildingAssignmentsAsync(s.BookingId, [other.BuildingId]);
            Assert.True(r0.Succeeded, r0.Error);
        }

        db.ChangeTracker.Clear();
        var row = Assert.Single(await db.BookingBuildingAssignments.Where(a => a.BookingId == s.BookingId).ToListAsync());
        Assert.Equal(other.BuildingId, row.BuildingId);
    }

    // ── Audit (FR-95) ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Assigning_WritesAnAuditRow_WithBuildingNamesButNoGuestData()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var s = await SeedAsync(scope);
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var guestEmail = (await db.Bookings.FindAsync(s.BookingId))!.ContactEmail;
        await bookings.SetBuildingAssignmentsAsync(s.BookingId, [s.BuildingId]);

        var row = Assert.Single(await db.AuditLogs
            .Where(a => a.BookingId == s.BookingId && a.Action == AuditAction.BuildingAssignmentChanged)
            .ToListAsync());
        Assert.Contains(s.BuildingName, row.Summary);   // reference data, not guest data
        Assert.DoesNotContain(guestEmail, row.Summary); // NFR-37
        Assert.DoesNotContain("Grupp", row.Summary);
    }
}
