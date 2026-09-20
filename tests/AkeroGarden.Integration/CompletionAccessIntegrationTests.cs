using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;
using AkeroGarden.Web.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the completion-link lookup + access evaluation against real PostgreSQL
/// (Phase 4 slice 2, §2.6, FR-53/54): the token generated on approval resolves to its booking and
/// reports Valid; an unknown token is NotFound; an already-submitted booking is AlreadyCompleted;
/// an expired token is Expired. The token string is the sole key (FR-54).
/// </summary>
public class CompletionAccessIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public CompletionAccessIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private IServiceProvider NewProvider()
    {
        var fake = new FakeEmailService();
        return _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(fake);
            // Pinned. Three separate clocks met in this file before: BookingService mints the token
            // expiry from its injected TimeProvider, the seed derived check-in from DateTime.UtcNow,
            // and each evaluation passed DateTime.UtcNow again. The assertions are about distances
            // (a token still live, one 60 days past expiry), so all three must be the same instant.
            DbFixture.PinClock(s, DbFixture.FixedNow);
            s.AddScoped<BookingEmailService>();
            s.AddScoped<BookingService>();
            s.AddScoped<CleaningZoneService>();
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
    /// Creates a Pending stay, approves it, and returns (bookingId, token).
    ///
    /// <para>Check-in is deliberately inside the 40-day issue window and relative to today: approval
    /// only mints a token when the guest could actually use it (FR-51/108), so a fixed far-future
    /// date would approve without one and every caller here would then be evaluating a null token —
    /// reporting NotFound rather than the access state it means to test.</para>
    /// </summary>
    /// <summary>The pinned instant — the single "now" every date in this file derives from.</summary>
    private static DateTime NowIn(IServiceProvider provider) =>
        provider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

    private static int _window;

    private static async Task<(Guid Id, string Token)> ApprovedBookingAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BookingService>();
        // Own window per seeded stay — approval refuses to double-book (FR-10). Base 4, step 6
        // keeps every window inside the 40-day link-issue window (FR-108), which these tests need:
        // a stay further out than that is approved with no token at all.
        var checkIn = DbFixture.StayWindow(provider, Interlocked.Increment(ref _window), firstDaysOut: 4, stepDays: 6);
        var booking = await service.CreateStayRequestAsync(new StayRequestInput
        {
            ContactName = "Gäst",
            ContactEmail = $"guest-{Guid.NewGuid():N}@test.se",
            CheckIn = checkIn,
            CheckOut = checkIn.AddDays(3),
            OvernightGuestCount = 4,
            MealPlan = MealPlan.FullBoardAll,
            AcceptTerms = true
        }, createdByAdmin: true);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Approval requires a lodging assignment (FR-103/D1).
        await BuildingAssignmentSeed.AssignBuildingAsync(db, booking.Id);
        await service.ApproveAsync(booking.Id);

        // Asserted, not null-forgiven: a `!` here turns "approval issued no link" into a NotFound
        // three tests down, which is how this helper broke silently once already.
        var token = (await db.Bookings.FindAsync(booking.Id))!.CompletionToken;
        Assert.False(string.IsNullOrEmpty(token), "approval did not issue a completion link");
        return (booking.Id, token!);
    }

    [Fact]
    public async Task ApprovedBooking_TokenResolves_AndIsValid()
    {
        var provider = NewProvider();
        var (id, token) = await ApprovedBookingAsync(provider);

        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BookingService>();

        var (access, booking) = await service.EvaluateCompletionAccessAsync(token, NowIn(provider));

        Assert.Equal(CompletionAccess.Valid, access);
        Assert.NotNull(booking);
        Assert.Equal(id, booking!.Id);
    }

    [Theory]
    [InlineData("no-such-token")]
    [InlineData("")]
    public async Task UnknownOrEmptyToken_IsNotFound(string token)
    {
        var provider = NewProvider();
        await ApprovedBookingAsync(provider); // a valid booking exists but with a different token

        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BookingService>();

        var (access, booking) = await service.EvaluateCompletionAccessAsync(token, NowIn(provider));

        Assert.Equal(CompletionAccess.NotFound, access);
        Assert.Null(booking);
    }

    [Fact]
    public async Task ExpiredToken_IsExpired()
    {
        var provider = NewProvider();
        var (_, token) = await ApprovedBookingAsync(provider);

        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BookingService>();

        // Evaluate well after any possible expiry (>30-day ceiling).
        var (access, _) = await service.EvaluateCompletionAccessAsync(token, NowIn(provider).AddDays(60));

        Assert.Equal(CompletionAccess.Expired, access);
    }

    [Fact]
    public async Task SubmittedBooking_IsAlreadyCompleted()
    {
        var provider = NewProvider();
        var (id, token) = await ApprovedBookingAsync(provider);

        // Simulate the slice-3 submission outcome: status advances, token string is retained.
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings.FindAsync(id);
            booking!.Status = BookingStatus.DetailsComplete;
            await db.SaveChangesAsync();
        }

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            var (access, _) = await service.EvaluateCompletionAccessAsync(token, NowIn(provider));
            Assert.Equal(CompletionAccess.AlreadyCompleted, access);
        }
    }
}
