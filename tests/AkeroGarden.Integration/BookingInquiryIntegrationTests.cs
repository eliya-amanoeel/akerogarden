using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;
using AkeroGarden.Web.Core.Services;
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
            // Pinned because two tests here assert an exact retention date: DeleteAfter is computed
            // by BookingService from its own TimeProvider at creation, and the assertion recomputes
            // the expected value. Read from two clocks a few milliseconds apart, those agree — until
            // the run straddles midnight, when they differ by exactly one day and the test goes red
            // with no code change. Found by the 2026-09-08 sweep; it was a latent flake, not a
            // failing test.
            DbFixture.PinClock(s, DbFixture.FixedNow);
            s.AddScoped<BookingEmailService>();
            s.AddScoped<BookingService>();
            s.AddScoped<CleaningZoneService>();
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

    /// <summary>FR-111: the guest's acceptance of the privacy policy and the booking terms is
    /// recorded, not validated and discarded. The clock is pinned because the assertion is the exact
    /// instant, not merely that something was stamped — an assertion on "not null" would pass on a
    /// value read from the wrong clock.
    ///
    /// <para>The three cases are one test because the point is the *contrast* between them: a
    /// guest-submitted stay request records the acceptance, and the two paths where nobody ticked
    /// anything must record nothing rather than a plausible-looking timestamp.</para></summary>
    [Fact]
    public async Task Terms_acceptance_is_recorded_for_a_guest_submission_and_for_nothing_else()
    {
        var pinned = new DateTimeOffset(2026, 8, 17, 10, 15, 0, TimeSpan.Zero);
        var provider = _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(new FakeEmailService());
            s.AddScoped<BookingEmailService>();
            s.AddScoped<BookingService>();
            s.AddScoped<CleaningZoneService>();
            s.AddScoped<FacilityService>();
            s.Configure<BookingEmailOptions>(o =>
            {
                o.PublicBaseUrl = "https://akerogarden.se";
                o.AdminBaseUrl = "https://admin.akerogarden.se";
                o.AdminNotificationEmail = "admin@test.se";
                o.ContactEmail = "info@test.se";
            });
            DbFixture.PinClock(s, pinned);
        });

        static StayRequestInput Stay() => new()
        {
            ContactName = "Anna Andersson",
            ContactEmail = $"anna-{Guid.NewGuid():N}@test.se",
            CheckIn = new DateOnly(2026, 9, 1),
            CheckOut = new DateOnly(2026, 9, 5),
            OvernightGuestCount = 12,
            MealPlan = MealPlan.FullBoardAll,
            AcceptTerms = true,
        };

        Guid guestId, adminId, generalId;
        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            guestId = (await service.CreateStayRequestAsync(Stay())).Id;
            adminId = (await service.CreateStayRequestAsync(Stay(), createdByAdmin: true)).Id;
            generalId = (await service.CreateGeneralInquiryAsync(new GeneralInquiryInput
            {
                ContactName = "Berit",
                ContactEmail = $"berit-{Guid.NewGuid():N}@test.se",
                Message = "En fråga",
                AcceptPrivacyPolicy = true,
            })).Id;
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // The guest ticked the box: the instant is the injected clock's, not wall-clock now.
            var guest = await db.Bookings.SingleAsync(b => b.Id == guestId);
            Assert.Equal(pinned.UtcDateTime, guest.TermsAcceptedAt);

            // Created on the guest's behalf (FR-47). ManualBooking sets AcceptTerms itself purely to
            // satisfy validation, so trusting that flag here would manufacture evidence of an
            // acceptance that never happened.
            var admin = await db.Bookings.SingleAsync(b => b.Id == adminId);
            Assert.Null(admin.TermsAcceptedAt);

            // A question is not a booking, so no terms apply to it and there is nothing to record.
            var general = await db.Bookings.SingleAsync(b => b.Id == generalId);
            Assert.Null(general.TermsAcceptedAt);
        }
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

    [Fact]
    public async Task CreateStayRequest_PersistsPending_WithSnapshots_AndSendsBothEmails()
    {
        var provider = NewProvider(out var email);
        Guid bookingId;
        Facility facility;

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            facility = await SeedFacilityAsync(db, amount: 1000m, vat: 0.25m);

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
                AcceptTerms = true
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
            Assert.Equal(DbFixture.TodayIn(provider).AddMonths(12), reloaded.DeleteAfter);
            // FR-92: reference is assigned at creation, format AG-YYYY-NNNN from the real DB sequence.
            Assert.Matches(@"^AG-\d{4}-\d{4}$", reloaded.BookingReference);
            Assert.True(reloaded.SequenceNumber > 0);

            var fs = Assert.Single(reloaded.FacilitySelections);
            Assert.Equal(facility.Name, fs.FacilityNameSnapshot);
            Assert.Equal(1000m, fs.AmountExVatSnapshot);
            Assert.Equal(0.25m, fs.VatRateSnapshot);

            // Cleaning is not selected at inquiry (FR-03) — it moves to the completion form (FR-93).
            Assert.Empty(reloaded.CleaningSelections);
            Assert.Null(reloaded.MainBuildingCleaningPurchased);
            Assert.Null(reloaded.SleepingCleaningPurchased);
            Assert.Null(reloaded.ConferenceCleaningPurchased);
        }

        Assert.Contains(email.Sent, e => e.ToAddress == "anna@test.se");   // guest ack (FR-05)
        var adminNotification = Assert.Single(email.Sent, e => e.ToAddress == "admin@test.se"); // admin notify (FR-06)
        // Reply-To must be the guest, not the notification mailbox itself — otherwise hitting
        // "Reply" in the mail client silently sends back to admin@test.se instead of the guest.
        Assert.Equal("anna@test.se", adminNotification.ReplyToAddress);
        Assert.Equal("Anna Andersson", adminNotification.ReplyToName);
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
            AcceptTerms = true
        });

        Assert.Null(booking.DayGuestCount);
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
        Assert.Equal(DbFixture.TodayIn(provider).AddMonths(12), booking.DeleteAfter);

        Assert.Contains(email.Sent, e => e.ToAddress == "david@test.se");
        var adminNotification = Assert.Single(email.Sent, e => e.ToAddress == "admin@test.se");
        // Reply-To must reach the guest directly, not loop back to the notification mailbox.
        Assert.Equal("david@test.se", adminNotification.ReplyToAddress);
        Assert.Equal("David", adminNotification.ReplyToName);
        // FR-92: general inquiries get a reference too, not just stay requests.
        Assert.Matches(@"^AG-\d{4}-\d{4}$", booking.BookingReference);
    }

    [Fact]
    public async Task BookingReference_IsUniquePerBooking_AcrossStayRequestsAndGeneralInquiries()
    {
        // FR-92: the global sequence is shared across both booking types and never repeats — no
        // per-year reset, so this only asserts uniqueness and monotonic growth, not a reset to 1.
        var provider = NewProvider(out _);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BookingService>();

        var first = await service.CreateGeneralInquiryAsync(new GeneralInquiryInput
        {
            ContactName = "Filip",
            ContactEmail = "filip@test.se",
            Message = "Fråga ett",
            AcceptPrivacyPolicy = true
        });
        var second = await service.CreateStayRequestAsync(new StayRequestInput
        {
            ContactName = "Greta",
            ContactEmail = "greta@test.se",
            CheckIn = new DateOnly(2026, 12, 1),
            CheckOut = new DateOnly(2026, 12, 3),
            OvernightGuestCount = 2,
            MealPlan = MealPlan.FullBoardAll,
            AcceptTerms = true
        });

        Assert.NotEqual(first.BookingReference, second.BookingReference);
        Assert.True(second.SequenceNumber > first.SequenceNumber);
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
                AcceptTerms = true
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
