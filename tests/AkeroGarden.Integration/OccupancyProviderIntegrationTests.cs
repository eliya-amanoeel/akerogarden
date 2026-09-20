using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the public calendar's occupancy provider against real PostgreSQL
/// (FR-10, NFR-07): only occupying-status bookings hold a date, only whole-property blocks reach
/// the calendar, the turnover window is honoured, and the result is the aggregate state — no
/// personal data. Each test uses its own building so the shared database doesn't cross-contaminate.
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
        // A lodging Building requires a PhysicalBuilding (§8.2, FR-13) — seed one and link it.
        var pb = new PhysicalBuilding { Id = Guid.NewGuid(), Name = $"Fysisk-{Guid.NewGuid():N}", IsActive = true };
        var building = new Building { Id = Guid.NewGuid(), Name = $"Hus-{Guid.NewGuid():N}", PhysicalBuildingId = pb.Id, IsActive = true };
        db.PhysicalBuildings.Add(pb);
        db.Buildings.Add(building);
        foreach (var b in beds)
            db.Rooms.Add(new Room { Id = Guid.NewGuid(), BuildingId = building.Id, Name = "R", BedCount = b, IsActive = true });
        await db.SaveChangesAsync();
        return building.Id;
    }

    /// <summary>Inserts a booking and assigns its BookingReference (FR-92) from the DB-generated
    /// SequenceNumber — the same two-phase pattern BookingService uses — since the shared per-class
    /// database's unique index on BookingReference rejects a second empty-string row otherwise.</summary>
    private static async Task AddBookingAsync(ApplicationDbContext db, Booking booking)
    {
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        booking.BookingReference = BookingReferencePolicy.Format(booking.SequenceNumber, booking.CreatedAt.Year);
        await db.SaveChangesAsync();
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
        await AddBookingAsync(db, Booking(BookingStatus.Pending, date, date.AddDays(2), 999));
        var states = await provider.GetStatesAsync(date, date);
        Assert.Equal(AvailabilityState.Free, states[date]);

        // Approved must count → 999 guests exceeds any capacity → Fully booked.
        await AddBookingAsync(db, Booking(BookingStatus.Approved, date, date.AddDays(2), 999));
        states = await provider.GetStatesAsync(date, date);
        Assert.Equal(AvailabilityState.Unavailable, states[date]);
    }

    [Fact]
    public async Task WholePropertyBlock_MakesDateUnavailable()
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
        Assert.Equal(AvailabilityState.Unavailable, states[date]);
    }

    [Fact]
    public async Task TurnoverWindow_MakesCheckOutDayAndTheDayAfter_Unavailable()
    {
        var provider = NewProvider(out var scope, out var db);
        using var _s = scope;
        await SeedBuildingWithBedsAsync(db, 5, 5);

        // Two guests against a property with beds to spare. Group size does not enter the
        // calculation — the nights are held because a group is here, and the two days after
        // check-out are held for turnover (FR-106).
        var checkIn = new DateOnly(2027, 7, 5);
        var checkOut = new DateOnly(2027, 7, 8);
        await AddBookingAsync(db, Booking(BookingStatus.Approved, checkIn, checkOut, 2));

        var states = await provider.GetStatesAsync(checkIn, checkOut.AddDays(2));

        Assert.Equal(AvailabilityState.Unavailable, states[checkIn]);
        Assert.Equal(AvailabilityState.Unavailable, states[checkOut]);
        Assert.Equal(AvailabilityState.Unavailable, states[checkOut.AddDays(1)]);
        Assert.Equal(AvailabilityState.Free, states[checkOut.AddDays(2)]);
    }

    [Fact]
    public async Task StayEndingBeforeTheRange_IsStillLoaded_AndHoldsTheRangesFirstDay()
    {
        var provider = NewProvider(out var scope, out var db);
        using var _s = scope;
        await SeedBuildingWithBedsAsync(db, 5, 5);

        // The booking's whole stay — and its check-out — sit before the requested window. Its
        // turnover window still reaches the window's first day. A query loading only the stays
        // overlapping [from, to] drops this booking entirely and reports the day Free, which is
        // exactly the silent failure this test exists to catch.
        var checkOut = new DateOnly(2027, 8, 12);
        await AddBookingAsync(db, Booking(BookingStatus.Approved, new DateOnly(2027, 8, 9), checkOut, 2));

        var from = checkOut.AddDays(1);
        var states = await provider.GetStatesAsync(from, from.AddDays(1));

        Assert.Equal(AvailabilityState.Unavailable, states[from]);
        Assert.Equal(AvailabilityState.Free, states[from.AddDays(1)]);
    }

    [Fact]
    public async Task PendingBooking_CreatesNoTurnoverWindow()
    {
        var provider = NewProvider(out var scope, out var db);
        using var _s = scope;
        await SeedBuildingWithBedsAsync(db, 5, 5);

        // Turnover follows occupancy: an inquiry nobody has approved reserves no cleaning days.
        var checkOut = new DateOnly(2027, 9, 14);
        await AddBookingAsync(db, Booking(BookingStatus.Pending, new DateOnly(2027, 9, 11), checkOut, 2));

        var states = await provider.GetStatesAsync(checkOut, checkOut.AddDays(1));

        Assert.Equal(AvailabilityState.Free, states[checkOut]);
        Assert.Equal(AvailabilityState.Free, states[checkOut.AddDays(1)]);
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

    /// <summary>
    /// The query filters blocks to whole-property scope, so a building or room out of service never
    /// reaches the public calendar (FR-102 owns its real effect). Covered here and not only in the
    /// pure function because the filter lives in the EF query — a unit test cannot see it.
    /// </summary>
    [Theory]
    [InlineData(BlockScope.Building)]
    [InlineData(BlockScope.Room)]
    public async Task PartialBlock_DoesNotReachTheCalendar(BlockScope scope)
    {
        var provider = NewProvider(out var svcScope, out var db);
        using var _s = svcScope;
        var buildingId = await SeedBuildingWithBedsAsync(db, 5, 5);
        var roomId = db.Rooms.First(r => r.BuildingId == buildingId).Id;

        var from = new DateOnly(2027, 9, 6);
        var to = new DateOnly(2027, 9, 8);
        db.BlockedPeriods.Add(new BlockedPeriod
        {
            Id = Guid.NewGuid(),
            Scope = scope,
            StartDate = from,
            EndDate = to,
            BuildingId = scope == BlockScope.Building ? buildingId : null,
            RoomId = scope == BlockScope.Room ? roomId : null,
            Reason = "Renovering",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var states = await provider.GetStatesAsync(from, to);

        for (var d = from; d <= to; d = d.AddDays(1))
            Assert.Equal(AvailabilityState.Free, states[d]);
    }
}
