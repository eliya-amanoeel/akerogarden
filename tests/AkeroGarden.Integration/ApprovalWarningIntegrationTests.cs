using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;
using AkeroGarden.Web.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// The pre-approval findings shown before "Godkänn", in their two kinds. Advisory: the stay overlaps
/// a building- or room-scoped block, the overnight count exceeds the beds available for the dates,
/// or the guest stated an arrival/departure time that deviates from the standard (FR-101) — approval
/// still goes through. Blocking: no building is assigned (FR-103), or the property is not free for
/// the stay (FR-10/FR-113) — approval is refused, and these tests assert the refusal itself, not
/// only the flag, so the panel and the button cannot drift apart again.
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
        IServiceProvider provider, DateOnly checkIn, DateOnly checkOut, int overnightGuests, params int[] bedCounts) =>
        await SeedBookingAsync(provider, checkIn, checkOut, overnightGuests, null, null, bedCounts);

    private static async Task<Seeded> SeedBookingAsync(
        IServiceProvider provider, DateOnly checkIn, DateOnly checkOut, int overnightGuests,
        string? checkInTime, string? checkOutTime, params int[] bedCounts)
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
            CheckInTime = checkInTime,
            CheckOutTime = checkOutTime,
            MealPlan = MealPlan.SelfCateringAll,
            AcceptTerms = true,
        }, createdByAdmin: true);

        // The capacity warning is measured against the beds of the *assigned* buildings (FR-103/D2),
        // not every room on the property — so the booking must be assigned this building or the
        // available-bed count is zero and every case would warn.
        db.BookingBuildingAssignments.Add(new BookingBuildingAssignment
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            BuildingId = building.Id,
            BuildingNameSnapshot = building.Name,
            PhysicalBuildingNameSnapshot = physicalBuilding.Name,
            AssignedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

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

        Assert.False(warning.BlocksApproval);
        Assert.False(warning.HasAdvisory);
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

        // A room-scoped block is advice, not a refusal: the property is still free for the stay.
        Assert.True(warning.HasAdvisory);
        Assert.False(warning.BlocksApproval);
        Assert.Null(warning.AvailabilityConflict);

        // And it is a room, not a building: the panel must not claim the building cannot be assigned.
        Assert.True(warning.OverlapsRoomBlock);
        Assert.False(warning.OverlapsBuildingBlock);
    }

    [Fact]
    public async Task A_block_on_another_building_is_advice_and_reported_as_a_building_block()
    {
        // The booking's own building is free; a different building is closed over the dates. That
        // building cannot be assigned (FR-102) — the panel says so — but approval is not affected.
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(5).AddDays(60);
        var checkOut = checkIn.AddDays(2);
        var provider = NewProvider();
        var seeded = await SeedBookingAsync(provider, checkIn, checkOut, overnightGuests: 2, bedCounts: 4);
        var other = await SeedBookingAsync(provider, checkIn.AddYears(1), checkIn.AddYears(1).AddDays(2), 2, 4);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.BlockedPeriods.Add(new BlockedPeriod
            {
                Id = Guid.NewGuid(),
                Scope = BlockScope.Building,
                BuildingId = other.BuildingId,
                StartDate = checkIn,
                EndDate = checkOut,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var warning = await WarningsAsync(provider, seeded.BookingId);

        Assert.True(warning.OverlapsBlock);
        Assert.True(warning.OverlapsBuildingBlock);
        Assert.False(warning.OverlapsRoomBlock);
        Assert.Contains("Byggnaden", warning.BlockDescriptions[0]);
        Assert.False(warning.CapacityExceeded);
        Assert.True(warning.HasAdvisory);
        Assert.False(warning.BlocksApproval);
    }

    [Fact]
    public async Task Whole_property_block_refuses_approval_and_is_not_listed_as_advice()
    {
        // A whole-property block makes the dates unavailable (FR-10), so ApproveAsync refuses the stay
        // (FR-113). It used to sit in the advisory list — under "du kan ändå godkänna" — together with
        // a "2 guests exceed 0 beds" capacity line that was arithmetic, not information.
        // Relative to today, years out: clear of every literal window the older tests here occupy.
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(5);
        var checkOut = checkIn.AddDays(2);
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

        Assert.True(warning.BlocksApproval);
        Assert.NotNull(warning.AvailabilityConflict);
        // Named as the block it is, not as another group.
        Assert.Contains("Hela anläggningen är blockerad", warning.AvailabilityConflict);
        Assert.DoesNotContain("en grupp i taget", warning.AvailabilityConflict);
        Assert.False(warning.OverlapsBlock);
        Assert.False(warning.CapacityExceeded);
        Assert.False(warning.HasAdvisory);

        // The panel names exactly the refusal the button gives.
        using var approveScope = provider.CreateScope();
        var result = await approveScope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(seeded.BookingId, DbFixture.EstimatedPrice);
        Assert.False(result.Succeeded);
        Assert.Equal(warning.AvailabilityConflict, result.Error);
    }

    [Fact]
    public async Task Another_group_over_the_same_dates_refuses_approval_and_says_so_beforehand()
    {
        // One group at a time (FR-10/FR-113): the second request is refused at approval. The panel
        // must say so before the click, in the refusal's own words.
        // Clear of the whole-property test's window (and its turnover days) by a month.
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(5).AddDays(30);
        var checkOut = checkIn.AddDays(2);
        var provider = NewProvider();
        var first = await SeedBookingAsync(provider, checkIn, checkOut, overnightGuests: 2, bedCounts: 4);
        var second = await SeedBookingAsync(provider, checkIn, checkOut, overnightGuests: 2, bedCounts: 4);

        using (var scope = provider.CreateScope())
        {
            var approved = await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(first.BookingId, DbFixture.EstimatedPrice);
            Assert.True(approved.Succeeded, approved.Error);
        }

        var warning = await WarningsAsync(provider, second.BookingId);

        Assert.True(warning.BlocksApproval);
        Assert.Contains("en grupp i taget", warning.AvailabilityConflict);
        Assert.DoesNotContain("blockerad", warning.AvailabilityConflict);
        Assert.False(warning.HasAdvisory);

        using var approveScope = provider.CreateScope();
        var result = await approveScope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(second.BookingId, DbFixture.EstimatedPrice);
        Assert.False(result.Succeeded);
        Assert.Equal(warning.AvailabilityConflict, result.Error);
    }

    // ── FR-101: stated times that deviate from the standard ──────────────────────

    [Fact]
    public async Task Standard_times_give_no_deviation_warning()
    {
        var provider = NewProvider();
        var seeded = await SeedBookingAsync(
            provider, new DateOnly(2027, 3, 1), new DateOnly(2027, 3, 3), 2, "14:00", "12:00", 10);

        var warning = await WarningsAsync(provider, seeded.BookingId);

        Assert.False(warning.HasTimeDeviation);
        Assert.Empty(warning.TimeDeviations);
        Assert.False(warning.HasAdvisory);
        Assert.False(warning.BlocksApproval);
    }

    [Fact]
    public async Task An_early_arrival_is_warned_about_without_blocking_approval()
    {
        var provider = NewProvider();
        var seeded = await SeedBookingAsync(
            provider, new DateOnly(2027, 3, 10), new DateOnly(2027, 3, 12), 2, "08:00", "12:00", 10);

        var warning = await WarningsAsync(provider, seeded.BookingId);

        Assert.True(warning.HasTimeDeviation);
        Assert.Equal("Incheckning 08:00 avviker från standard 14:00.", Assert.Single(warning.TimeDeviations));
        Assert.True(warning.HasAdvisory);
        Assert.False(warning.BlocksApproval);

        // Non-blocking: approval still succeeds (same pattern as FR-75).
        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(seeded.BookingId, DbFixture.EstimatedPrice);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task A_late_departure_is_warned_about_too()
    {
        var provider = NewProvider();
        var seeded = await SeedBookingAsync(
            provider, new DateOnly(2027, 3, 20), new DateOnly(2027, 3, 22), 2, "14:00", "20:00", 10);

        var warning = await WarningsAsync(provider, seeded.BookingId);

        Assert.Equal("Utcheckning 20:00 avviker från standard 12:00.", Assert.Single(warning.TimeDeviations));
    }

    [Fact]
    public async Task Both_times_deviating_are_reported_separately()
    {
        var provider = NewProvider();
        var seeded = await SeedBookingAsync(
            provider, new DateOnly(2027, 4, 1), new DateOnly(2027, 4, 3), 2, "09:30", "18:00", 10);

        var warning = await WarningsAsync(provider, seeded.BookingId);

        Assert.Equal(2, warning.TimeDeviations.Count);
        Assert.Contains(warning.TimeDeviations, w => w.StartsWith("Incheckning 09:30", StringComparison.Ordinal));
        Assert.Contains(warning.TimeDeviations, w => w.StartsWith("Utcheckning 18:00", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_time_the_dropdown_cannot_produce_is_rejected_and_stored_as_the_standard()
    {
        // Server-authoritative (FR-100): a crafted 08:15 is not snapped to 08:00 — that would invent
        // a request the guest never made — it is treated as no time validly stated.
        var provider = NewProvider();
        var seeded = await SeedBookingAsync(
            provider, new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 12), 2, "08:15", "25:00", 10);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var booking = await db.Bookings.FindAsync(seeded.BookingId);

        Assert.Equal(MealOccasionResolver.StandardCheckInTime, booking!.CheckInTime);
        Assert.Equal(MealOccasionResolver.StandardCheckOutTime, booking.CheckOutTime);

        var warning = await WarningsAsync(provider, seeded.BookingId);
        Assert.False(warning.HasTimeDeviation);
    }

    // ── Nothing assigned yet ─────────────────────────────────────────────────────

    [Fact]
    public async Task Before_any_building_is_assigned_the_warning_says_so_instead_of_comparing_beds()
    {
        // With nothing assigned the available-bed count is zero, so a capacity comparison would fire
        // on every new booking — arithmetic, not information. A warning that is always present is one
        // the admin learns to skip, and this strip also carries the FR-101 time deviations.
        var provider = NewProvider();
        var seeded = await SeedUnassignedBookingAsync(
            provider, new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 3), overnightGuests: 2);

        var warning = await WarningsAsync(provider, seeded.BookingId);

        Assert.True(warning.NoLodgingAssigned);
        Assert.False(warning.CapacityExceeded);
        // Not advice: approval is refused without a building (FR-103), so it is a blocking finding.
        Assert.True(warning.BlocksApproval);
        Assert.False(warning.HasAdvisory);
    }

    [Fact]
    public async Task Once_a_building_is_assigned_the_no_lodging_warning_clears()
    {
        var provider = NewProvider();
        var seeded = await SeedBookingAsync(
            provider, new DateOnly(2027, 5, 10), new DateOnly(2027, 5, 12), 2, 10);

        var warning = await WarningsAsync(provider, seeded.BookingId);

        Assert.False(warning.NoLodgingAssigned);
        Assert.False(warning.CapacityExceeded);
        Assert.False(warning.BlocksApproval);
        Assert.False(warning.HasAdvisory);
    }

    /// <summary>A stay request with no building assignment at all — the state every booking is in
    /// between submission and the admin's first decision.</summary>
    private static async Task<Seeded> SeedUnassignedBookingAsync(
        IServiceProvider provider, DateOnly checkIn, DateOnly checkOut, int overnightGuests)
    {
        using var scope = provider.CreateScope();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();

        var booking = await bookings.CreateStayRequestAsync(new StayRequestInput
        {
            ContactName = "Otilldelad",
            ContactEmail = $"otilldelad-{Guid.NewGuid():N}@test.se",
            CheckIn = checkIn,
            CheckOut = checkOut,
            OvernightGuestCount = overnightGuests,
            MealPlan = MealPlan.SelfCateringAll,
            AcceptTerms = true,
        }, createdByAdmin: true);

        return new Seeded(booking.Id, Guid.Empty);
    }
}
