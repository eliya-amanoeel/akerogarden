using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Models;
using AkeroGarden.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// The non-blocking pre-approval guard (Phase 7): before "Godkänn", the admin is warned when the
/// requested stay overlaps a block or the overnight count exceeds the beds available for the dates.
/// Approval itself is never blocked — this only computes the warning shown on the booking detail.
/// </summary>
public class ApprovalWarningIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public ApprovalWarningIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private IServiceProvider NewProvider() =>
        _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(new FakeEmailService());
            s.AddScoped<BookingEmailService>();
            s.AddScoped<RoomAvailabilityService>();
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

    // The fixture shares one database across the class with no per-test cleanup, and the block-overlap
    // check is global by date, so each test uses a DISTINCT date window to stay isolated from the
    // others' seeded blocks/bookings.
    private sealed record Seeded(Guid BookingId, Guid BuildingId);

    private static async Task<Seeded> SeedBookingAsync(
        IServiceProvider provider, DateOnly checkIn, DateOnly checkOut, int overnightGuests, params int[] bedCounts)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var buildings = scope.ServiceProvider.GetRequiredService<BuildingService>();
        var rooms = scope.ServiceProvider.GetRequiredService<RoomService>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();

        var physicalBuilding = new PhysicalBuilding { Id = Guid.NewGuid(), Name = $"Lilla-{Guid.NewGuid():N}" };
        db.PhysicalBuildings.Add(physicalBuilding);
        await db.SaveChangesAsync();

        var building = new Building { Name = $"Blidö-{Guid.NewGuid():N}", PhysicalBuildingId = physicalBuilding.Id, DisplayOrder = 1 };
        await buildings.TryCreateAsync(building);
        for (var i = 0; i < bedCounts.Length; i++)
            await rooms.CreateAsync(new Room { Name = $"Rum {i + 1}", BedCount = bedCounts[i], BuildingId = building.Id, DisplayOrder = i + 1 });

        var booking = await bookings.CreateStayRequestAsync(new StayRequestInput
        {
            ContactName = "Grupp",
            ContactEmail = $"grupp-{Guid.NewGuid():N}@test.se",
            CheckIn = checkIn,
            CheckOut = checkOut,
            OvernightGuestCount = overnightGuests,
            MealPlan = MealPlan.SelfCateringAll,
            AcceptPrivacyPolicy = true,
        }, createdByAdmin: true);
        return new Seeded(booking.Id, building.Id);
    }

    private static async Task<ApprovalWarning> WarningsAsync(IServiceProvider provider, Guid bookingId)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BookingService>().GetApprovalWarningsAsync(bookingId);
    }

    [Fact]
    public async Task Within_capacity_and_no_block_gives_no_warning()
    {
        var provider = NewProvider();
        var seeded = await SeedBookingAsync(provider,
            new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 12), overnightGuests: 2, bedCounts: 4);

        var warning = await WarningsAsync(provider, seeded.BookingId);

        Assert.False(warning.Any);
    }

    [Fact]
    public async Task Guest_count_exceeding_available_beds_warns_on_capacity_only()
    {
        // Capacity is property-wide (all active rooms). The fixture DB accumulates other tests' rooms,
        // so use a guest count that exceeds any plausible accumulated total and assert the behaviour
        // (exceeded fires, no block on these dates) rather than an exact bed count.
        var provider = NewProvider();
        var seeded = await SeedBookingAsync(provider,
            new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 12), overnightGuests: 1000, bedCounts: 4);

        var warning = await WarningsAsync(provider, seeded.BookingId);

        Assert.True(warning.CapacityExceeded);
        Assert.False(warning.OverlapsBlock);
        Assert.Equal(1000, warning.RequestedGuests);
        Assert.True(warning.AvailableBeds < warning.RequestedGuests);
    }

    [Fact]
    public async Task Dates_overlapping_a_block_warn_even_when_capacity_is_fine()
    {
        // Two rooms (4 + 4 beds); block only the second, request 2 guests. Available beds (4) still
        // cover the party, so only the block warning should fire — isolating it from capacity.
        var checkIn = new DateOnly(2026, 10, 10);
        var checkOut = new DateOnly(2026, 10, 12);
        var provider = NewProvider();
        var seeded = await SeedBookingAsync(provider, checkIn, checkOut, overnightGuests: 2, bedCounts: [4, 4]);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // This booking's own building (the shared DB holds other tests' rooms too).
            var secondRoom = db.Rooms.Where(r => r.BuildingId == seeded.BuildingId)
                .OrderBy(r => r.DisplayOrder).ToList()[1];
            db.BlockedPeriods.Add(new BlockedPeriod
            {
                Id = Guid.NewGuid(),
                Scope = BlockScope.Room,
                BuildingId = secondRoom.BuildingId,
                RoomId = secondRoom.Id,
                StartDate = checkIn,
                EndDate = checkOut,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var warning = await WarningsAsync(provider, seeded.BookingId);

        Assert.True(warning.OverlapsBlock);
        Assert.False(warning.CapacityExceeded);
        Assert.Single(warning.BlockDescriptions);
        Assert.Contains("Rummet", warning.BlockDescriptions[0]);
    }

    [Fact]
    public async Task Whole_property_block_warns_on_both_and_names_the_scope()
    {
        var checkIn = new DateOnly(2026, 11, 10);
        var checkOut = new DateOnly(2026, 11, 12);
        var provider = NewProvider();
        var seeded = await SeedBookingAsync(provider, checkIn, checkOut, overnightGuests: 2, bedCounts: 4);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.BlockedPeriods.Add(new BlockedPeriod
            {
                Id = Guid.NewGuid(),
                Scope = BlockScope.WholeProperty,
                StartDate = checkIn,
                EndDate = checkOut,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var warning = await WarningsAsync(provider, seeded.BookingId);

        Assert.True(warning.OverlapsBlock);
        Assert.True(warning.CapacityExceeded); // the whole property is blocked → 0 beds available
        Assert.Equal(0, warning.AvailableBeds);
        Assert.Contains("Hela anläggningen", warning.BlockDescriptions[0]);
    }
}
