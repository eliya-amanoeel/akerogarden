using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Models;
using AkeroGarden.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for inquiry creation against real PostgreSQL (FR-03–06, NFR-33/34):
/// a Pending booking is persisted with price snapshots, the server-authoritative rules are
/// enforced, retention is set (FR-91), and the acknowledgement + admin emails are dispatched.
/// Tests use unique names because the fixture's database is shared across the class.
/// </summary>
public class BookingInquiryIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public BookingInquiryIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private IServiceProvider NewProvider(out FakeEmailService email)
    {
        var fake = new FakeEmailService();
        email = fake;
        return _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(fake);
            s.AddScoped<BookingEmailService>();
            s.AddScoped<BookingService>();
            s.AddScoped<FacilityService>();
            s.Configure<BookingEmailOptions>(o =>
            {
                o.PublicBaseUrl = "https://akerogarden.se";
                o.AdminBaseUrl = "https://admin.akerogarden.se";
                o.AdminNotificationEmail = "admin@test.se";
                o.ContactEmail = "info@test.se";
            });
        });
    }

    private static async Task<Facility> SeedFacilityAsync(ApplicationDbContext db, decimal amount, decimal vat)
    {
        var f = new Facility
        {
            Id = Guid.NewGuid(),
            Name = $"Kapellet-{Guid.NewGuid():N}",
            AmountExVat = amount,
            VatRate = vat,
            IsActive = true
        };
        db.Facilities.Add(f);
        await db.SaveChangesAsync();
        return f;
    }

    private static async Task<CleaningZone> SeedZoneAsync(
        ApplicationDbContext db, decimal amount, decimal vat, params Guid[] includes)
    {
        var z = new CleaningZone
        {
            Id = Guid.NewGuid(),
            Name = $"Zon-{Guid.NewGuid():N}",
            AmountExVat = amount,
            VatRate = vat,
            IsActive = true,
            IncludesZoneIds = includes.ToList()
        };
        db.CleaningZones.Add(z);
        await db.SaveChangesAsync();
        return z;
    }

    [Fact]
    public async Task CreateStayRequest_PersistsPending_WithSnapshots_AndSendsBothEmails()
    {
        var provider = NewProvider(out var email);
        Guid bookingId;
        Facility facility;
        CleaningZone zone;

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            facility = await SeedFacilityAsync(db, amount: 1000m, vat: 0.25m);
            zone = await SeedZoneAsync(db, amount: 2000m, vat: 0.12m);

            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            var booking = await service.CreateStayRequestAsync(new StayRequestInput
            {
                ContactName = "Anna Andersson",
                ContactEmail = "anna@test.se",
                CheckIn = new DateOnly(2026, 8, 1),
                CheckOut = new DateOnly(2026, 8, 5),
                OvernightGuestCount = 12,
                MealPlan = MealPlan.FullBoardAll,
                SelectedFacilityIds = [facility.Id],
                SelectedCleaningZoneIds = [zone.Id],
                AcceptPrivacyPolicy = true
            });
            bookingId = booking.Id;
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var reloaded = await db.Bookings
                .Include(b => b.FacilitySelections)
                .Include(b => b.CleaningSelections)
                .SingleAsync(b => b.Id == bookingId);

            Assert.Equal(BookingStatus.Pending, reloaded.Status);
            Assert.Equal(BookingType.StayRequest, reloaded.BookingType);
            Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(12), reloaded.DeleteAfter);

            var fs = Assert.Single(reloaded.FacilitySelections);
            Assert.Equal(facility.Name, fs.FacilityNameSnapshot);
            Assert.Equal(1000m, fs.AmountExVatSnapshot);
            Assert.Equal(0.25m, fs.VatRateSnapshot);

            var cs = Assert.Single(reloaded.CleaningSelections);
            Assert.Equal(zone.Name, cs.ZoneNameSnapshot);
            Assert.Equal(2000m, cs.AmountExVatSnapshot);
        }

        Assert.Contains(email.Sent, e => e.ToAddress == "anna@test.se");   // guest ack (FR-05)
        Assert.Contains(email.Sent, e => e.ToAddress == "admin@test.se");  // admin notify (FR-06)
    }

    [Fact]
    public async Task CreateStayRequest_SelfCateringAll_DropsDayGuests()
    {
        var provider = NewProvider(out _);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BookingService>();

        var booking = await service.CreateStayRequestAsync(new StayRequestInput
        {
            ContactName = "Bo",
            ContactEmail = "bo@test.se",
            CheckIn = new DateOnly(2026, 9, 1),
            CheckOut = new DateOnly(2026, 9, 3),
            OvernightGuestCount = 4,
            MealPlan = MealPlan.SelfCateringAll,
            DayGuestCount = 5, // must be ignored server-side (§4.2)
            AcceptPrivacyPolicy = true
        });

        Assert.Null(booking.DayGuestCount);
    }

    [Fact]
    public async Task CreateStayRequest_BundleDropsCoveredZones()
    {
        var provider = NewProvider(out _);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var a = await SeedZoneAsync(db, 2000m, 0.12m);
        var b = await SeedZoneAsync(db, 2000m, 0.12m);
        var bundle = await SeedZoneAsync(db, 8000m, 0.12m, a.Id, b.Id);

        var service = scope.ServiceProvider.GetRequiredService<BookingService>();
        var booking = await service.CreateStayRequestAsync(new StayRequestInput
        {
            ContactName = "Cecilia",
            ContactEmail = "cecilia@test.se",
            CheckIn = new DateOnly(2026, 10, 1),
            CheckOut = new DateOnly(2026, 10, 4),
            OvernightGuestCount = 20,
            MealPlan = MealPlan.FullBoardAll,
            SelectedCleaningZoneIds = [bundle.Id, a.Id, b.Id], // bundle + covered zones
            AcceptPrivacyPolicy = true
        });

        var selection = Assert.Single(booking.CleaningSelections);
        Assert.Equal(bundle.Id, selection.CleaningZoneId);
    }

    [Fact]
    public async Task CreateGeneralInquiry_PersistsPending_AndSendsBothEmails()
    {
        var provider = NewProvider(out var email);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BookingService>();

        var booking = await service.CreateGeneralInquiryAsync(new GeneralInquiryInput
        {
            ContactName = "David",
            ContactEmail = "david@test.se",
            Message = "Har ni ledigt i sommar?",
            AcceptPrivacyPolicy = true
        });

        Assert.Equal(BookingType.GeneralInquiry, booking.BookingType);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Equal("Har ni ledigt i sommar?", booking.Message);
        Assert.Null(booking.CheckIn);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(12), booking.DeleteAfter);

        Assert.Contains(email.Sent, e => e.ToAddress == "david@test.se");
        Assert.Contains(email.Sent, e => e.ToAddress == "admin@test.se");
    }

    [Fact]
    public async Task FacilitySnapshot_SurvivesLaterFacilityEdit()
    {
        // NFR-34: the agreed facility price is preserved even if the facility is later edited.
        var provider = NewProvider(out _);
        Guid bookingId;
        Guid facilityId;

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var facility = await SeedFacilityAsync(db, amount: 1000m, vat: 0.25m);
            facilityId = facility.Id;

            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            var booking = await service.CreateStayRequestAsync(new StayRequestInput
            {
                ContactName = "Eva",
                ContactEmail = "eva@test.se",
                CheckIn = new DateOnly(2026, 11, 1),
                CheckOut = new DateOnly(2026, 11, 3),
                OvernightGuestCount = 2,
                MealPlan = MealPlan.FullBoardAll,
                SelectedFacilityIds = [facility.Id],
                AcceptPrivacyPolicy = true
            });
            bookingId = booking.Id;
        }

        using (var scope = provider.CreateScope())
        {
            var facilityService = scope.ServiceProvider.GetRequiredService<FacilityService>();
            var facility = await facilityService.GetAsync(facilityId);
            facility!.Name = "Nytt namn";
            facility.AmountExVat = 9999m;
            await facilityService.UpdateAsync(facility);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var selection = await db.BookingFacilitySelections.SingleAsync(s => s.BookingId == bookingId);
            Assert.NotEqual("Nytt namn", selection.FacilityNameSnapshot);
            Assert.Equal(1000m, selection.AmountExVatSnapshot); // unchanged snapshot
        }
    }
}
