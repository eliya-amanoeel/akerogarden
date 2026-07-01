using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the public calendar's occupancy provider against real PostgreSQL
/// (FR-10, NFR-07): only occupying-status bookings count, blocks reduce capacity, and the result
/// is the aggregate state — no personal data. Each test uses its own building so the shared
/// database doesn't cross-contaminate capacity.
/// </summary>
public class OccupancyProviderIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public OccupancyProviderIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private OccupancyProvider NewProvider(out IServiceScope scope, out ApplicationDbContext db)
    {
        var services = _fixture.BuildServices(s => s.AddScoped<OccupancyProvider>());
        scope = services.CreateScope();
        db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return scope.ServiceProvider.GetRequiredService<OccupancyProvider>();
    }

    private static async Task<Guid> SeedBuildingWithBedsAsync(ApplicationDbContext db, params int[] beds)
    {
        var building = new Building { Id = Guid.NewGuid(), Name = $"Hus-{Guid.NewGuid():N}", IsActive = true };
        db.Buildings.Add(building);
        foreach (var b in beds)
            db.Rooms.Add(new Room { Id = Guid.NewGuid(), BuildingId = building.Id, Name = "R", BedCount = b, IsActive = true });
        await db.SaveChangesAsync();
        return building.Id;
    }

    private static Booking Booking(BookingStatus status, DateOnly checkIn, DateOnly checkOut, int guests) => new()
    {
        Id = Guid.NewGuid(),
        BookingType = BookingType.StayRequest,
        Status = status,
        ContactName = "X",
        ContactEmail = "x@test.se",
        CheckIn = checkIn,
        CheckOut = checkOut,
        OvernightGuestCount = guests,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task OnlyOccupyingStatuses_CountTowardOccupancy()
    {
        var provider = NewProvider(out var scope, out var db);
        using var _s = scope;
        // Isolate capacity to this test by making all other rooms irrelevant: use a large booking
        // that would only reach capacity for THIS building's beds. To keep it deterministic we
        // assert Free for a Pending booking regardless of global capacity.
        await SeedBuildingWithBedsAsync(db, 5, 5);
        var date = new DateOnly(2027, 3, 10);

        // Pending must not count.
        db.Bookings.Add(Booking(BookingStatus.Pending, date, date.AddDays(2), 999));
        await db.SaveChangesAsync();
        var states = await provider.GetStatesAsync(date, date);
        Assert.Equal(AvailabilityState.Free, states[date]);

        // Approved must count → 999 guests exceeds any capacity → Fully booked.
        db.Bookings.Add(Booking(BookingStatus.Approved, date, date.AddDays(2), 999));
        await db.SaveChangesAsync();
        states = await provider.GetStatesAsync(date, date);
        Assert.Equal(AvailabilityState.FullyBooked, states[date]);
    }

    [Fact]
    public async Task WholePropertyBlock_MakesDateFullyBooked()
    {
        var provider = NewProvider(out var scope, out var db);
        using var _s = scope;
        await SeedBuildingWithBedsAsync(db, 5);
        var date = new DateOnly(2027, 4, 15);
        db.BlockedPeriods.Add(new BlockedPeriod
        {
            Id = Guid.NewGuid(),
            Scope = BlockScope.WholeProperty,
            StartDate = date,
            EndDate = date,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var states = await provider.GetStatesAsync(date, date);
        Assert.Equal(AvailabilityState.FullyBooked, states[date]);
    }

    [Fact]
    public async Task GetStatesAsync_ReturnsAStatePerDateInRange()
    {
        var provider = NewProvider(out var scope, out var db);
        using var _s = scope;
        await SeedBuildingWithBedsAsync(db, 5);

        var from = new DateOnly(2027, 5, 1);
        var to = new DateOnly(2027, 5, 31);
        var states = await provider.GetStatesAsync(from, to);

        Assert.Equal(31, states.Count);
    }
}
