using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;
using AkeroGarden.Web.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the audit trail against real PostgreSQL (Phase 11, §6.9, FR-95/98,
/// NFR-36/37). Asserts that booking-lifecycle actions each write one correctly-shaped
/// <see cref="AuditLog"/> row, attributed to the acting admin (or the guest for a completion
/// submission), that the summaries carry <b>no guest personal data</b>, and that a booking's audit
/// rows cascade-delete with the booking (FR-98).
/// </summary>
public class AuditTrailIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public AuditTrailIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private static readonly Guid AdminId = Guid.NewGuid();
    private const string AdminEmail = "anna@akerogarden.se";
    private const string AdminName = "Anna Andersson";

    private IServiceProvider NewProvider()
    {
        var fake = new FakeEmailService();
        return _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(fake);
            s.AddScoped<BookingEmailService>();
            s.AddScoped<BookingService>();
            s.AddScoped<CleaningZoneService>();
            // Override the fixture's default System accessor with a known admin (last wins).
            s.AddScoped<IAuditActorAccessor>(_ =>
                new TestAuditActorAccessor(AuditActor.Admin(AdminId, AdminEmail, AdminName)));
            s.Configure<BookingEmailOptions>(o =>
            {
                o.PublicBaseUrl = "https://akerogarden.se";
                o.AdminBaseUrl = "https://admin.akerogarden.se";
                o.AdminNotificationEmail = "admin@test.se";
                o.ContactEmail = "info@test.se";
            });
        });
    }

    /// <summary>
    /// Creates a Pending stay with a lodging assignment, ready to approve. <paramref name="checkIn"/>
    /// defaults to well beyond the 40-day issue window, so approving it issues no completion link
    /// (FR-51/108) — pass a nearer date to exercise the link-issued audit row.
    /// </summary>
    private static async Task<(Guid Id, string GuestEmail, string Reference)> CreatePendingStayAsync(
        IServiceProvider provider, DateOnly? checkIn = null)
    {
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BookingService>();
        var guestEmail = $"guest-{Guid.NewGuid():N}@test.se";
        var arrival = checkIn ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(180);
        var booking = await service.CreateStayRequestAsync(new StayRequestInput
        {
            ContactName = "Gäst Gästsson",
            ContactEmail = guestEmail,
            CheckIn = arrival,
            CheckOut = arrival.AddDays(3),
            OvernightGuestCount = 3,
            MealPlan = MealPlan.FullBoardAll,
            AcceptPrivacyPolicy = true
        }, createdByAdmin: true);

        // Approval requires a lodging assignment (FR-103/D1).
        await BuildingAssignmentSeed.AssignBuildingAsync(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), booking.Id);
        return (booking.Id, guestEmail, booking.BookingReference);
    }

    private static async Task<List<AuditLog>> AuditRowsAsync(IServiceProvider provider, Guid bookingId, AuditAction action)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AuditLogs
            .Where(a => a.BookingId == bookingId && a.Action == action)
            .ToListAsync();
    }

    [Fact]
    public async Task AdminCreation_WritesOneRow_AttributedToAdmin()
    {
        var provider = NewProvider();
        var (id, guestEmail, reference) = await CreatePendingStayAsync(provider);

        var row = Assert.Single(await AuditRowsAsync(provider, id, AuditAction.BookingCreatedByAdmin));
        Assert.Equal(AuditActorType.Admin, row.ActorType);
        Assert.Equal(AdminId, row.ActorUserId);
        Assert.Equal(AdminEmail, row.ActorEmailSnapshot);
        Assert.Equal(AdminName, row.ActorNameSnapshot);
        Assert.Equal(nameof(Booking), row.EntityType);
        Assert.Equal(id, row.EntityId);
        Assert.Equal(reference, row.BookingReferenceSnapshot);
        Assert.DoesNotContain(guestEmail, row.Summary); // NFR-37
    }

    [Fact]
    public async Task Approve_WritesApprovedRow_WithNoGuestPii()
    {
        var provider = NewProvider();
        var (id, guestEmail, reference) = await CreatePendingStayAsync(provider);

        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(id);

        var row = Assert.Single(await AuditRowsAsync(provider, id, AuditAction.BookingApproved));
        Assert.Equal(AuditActorType.Admin, row.ActorType);
        Assert.Equal(reference, row.BookingReferenceSnapshot);
        Assert.DoesNotContain(guestEmail, row.Summary);
        Assert.DoesNotContain("Gästsson", row.Summary); // guest name never in the trail (NFR-37)
    }

    [Fact]
    public async Task Approve_WithinTheIssueWindow_AlsoWritesALinkIssuedRow()
    {
        // Every other path that hands a guest a link records one (FR-95). Since FR-108 made the link
        // at approval conditional, without this row "was this guest ever sent a link, and when" is
        // unanswerable for the most common case of all.
        var provider = NewProvider();
        var (id, guestEmail, reference) = await CreatePendingStayAsync(
            provider, checkIn: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));

        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(id);

        var row = Assert.Single(await AuditRowsAsync(provider, id, AuditAction.CompletionLinkIssued));
        Assert.Equal(AuditActorType.Admin, row.ActorType);
        Assert.Equal(reference, row.BookingReferenceSnapshot);

        // The trail records that a link went out, never the link itself — the token is a
        // password-free credential, and the guest's address is PII (NFR-37, §6.9).
        Assert.DoesNotContain(guestEmail, row.Summary);
        Assert.DoesNotContain("Gästsson", row.Summary);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var token = (await db.Bookings.FindAsync(id))!.CompletionToken!;
            Assert.DoesNotContain(token, row.Summary);
        }
    }

    [Fact]
    public async Task Approve_BeyondTheIssueWindow_WritesNoLinkIssuedRow()
    {
        // No link was issued, so claiming one was would be a false record — and the trail is the
        // thing you consult precisely when the guest says they never received it.
        var provider = NewProvider();
        var (id, _, _) = await CreatePendingStayAsync(provider); // 180 days out

        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(id);

        Assert.Single(await AuditRowsAsync(provider, id, AuditAction.BookingApproved));
        Assert.Empty(await AuditRowsAsync(provider, id, AuditAction.CompletionLinkIssued));
    }

    [Fact]
    public async Task Deny_WritesRow_WithoutTheDenialReason()
    {
        var provider = NewProvider();
        var (id, _, _) = await CreatePendingStayAsync(provider);

        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BookingService>()
                .DenyAsync(id, "Gästen är inte välkommen"); // free-text that could name/insult the guest

        var row = Assert.Single(await AuditRowsAsync(provider, id, AuditAction.BookingDenied));
        Assert.DoesNotContain("välkommen", row.Summary); // reason is deliberately not recorded (NFR-37)
    }

    [Fact]
    public async Task Cancel_WritesRow_WithInitiatorButNotReason()
    {
        var provider = NewProvider();
        var (id, _, _) = await CreatePendingStayAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<BookingService>();
            await svc.ApproveAsync(id);
            await svc.CancelAsync(id, CancellationInitiator.PropertyInitiated, "internt känsligt skäl");
        }

        var row = Assert.Single(await AuditRowsAsync(provider, id, AuditAction.BookingCancelled));
        Assert.Contains("anläggningsinitierad", row.Summary); // enum label, not PII
        Assert.DoesNotContain("känsligt", row.Summary);       // free-text reason omitted
    }

    [Fact]
    public async Task CompletionSubmission_IsAttributedToGuest_NotAnAdmin()
    {
        var provider = NewProvider();

        // Seed active meal options + an approved 2-night booking.
        Guid bookingId;
        string token;
        ResolvedMealOccasions occasions;
        Guid lunchId, dinnerId;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            var lunch = new MealOption { Id = Guid.NewGuid(), Name = $"L-{Guid.NewGuid():N}", MealType = MealType.Lunch, IsActive = true };
            var dinner = new MealOption { Id = Guid.NewGuid(), Name = $"D-{Guid.NewGuid():N}", MealType = MealType.Dinner, IsActive = true };
            db.MealOptions.AddRange(lunch, dinner);
            await db.SaveChangesAsync();
            lunchId = lunch.Id; dinnerId = dinner.Id;

            var booking = await service.CreateStayRequestAsync(new StayRequestInput
            {
                ContactName = "Berit",
                ContactEmail = $"berit-{Guid.NewGuid():N}@test.se",
                CheckIn = new DateOnly(2026, 8, 10),
                CheckOut = new DateOnly(2026, 8, 12),
                OvernightGuestCount = 1,
                MealPlan = MealPlan.FullBoardAll,
                AcceptPrivacyPolicy = true
            }, createdByAdmin: true);
            // Approval requires a lodging assignment (FR-103/D1).
            await BuildingAssignmentSeed.AssignBuildingAsync(db, booking.Id);
            await service.ApproveAsync(booking.Id);
            bookingId = booking.Id;
            token = (await db.Bookings.FindAsync(bookingId))!.CompletionToken!;
            occasions = MealOccasionResolver.Resolve((await db.Bookings.FindAsync(bookingId))!);
        }

        var form = new CompletionFormModel
        {
            ArrivalDinner = true,
            DepartureBreakfast = true,
            MealChoices = occasions.AllSittings
                .Select(s => (string?)(s.MealType == MealType.Lunch ? lunchId : dinnerId).ToString())
                .ToList(),
        };
        form.Overnight.Add(new OvernightParticipantEntry
        {
            FullName = "Hemligt Namn", // must never reach the audit trail
            AgeCategory = AgeCategory.Adult,
            Allergies = [Allergy.Nut],  // art. 9 health data — must never reach it either (NFR-37)
        });

        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BookingService>()
                .SubmitCompletionAsync(token, form, DateTime.UtcNow);

        var row = Assert.Single(await AuditRowsAsync(provider, bookingId, AuditAction.CompletionSubmitted));
        Assert.Equal(AuditActorType.Guest, row.ActorType);
        Assert.Null(row.ActorUserId);
        Assert.Null(row.ActorEmailSnapshot);
        Assert.Null(row.ActorNameSnapshot);
        Assert.DoesNotContain("Hemligt Namn", row.Summary); // participant name never stored (NFR-37)
    }

    [Fact]
    public async Task DeletingBooking_CascadeDeletesItsAuditRows_FR98()
    {
        var provider = NewProvider();
        var (id, _, _) = await CreatePendingStayAsync(provider);
        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(id);

        // Sanity: rows exist before deletion.
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.True(await db.AuditLogs.AnyAsync(a => a.BookingId == id));

            var booking = await db.Bookings.SingleAsync(b => b.Id == id);
            db.Bookings.Remove(booking);
            await db.SaveChangesAsync();
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False(await db.AuditLogs.AnyAsync(a => a.BookingId == id)); // cascade (FR-98)
        }
    }
}
