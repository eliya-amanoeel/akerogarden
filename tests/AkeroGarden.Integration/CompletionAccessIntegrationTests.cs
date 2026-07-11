using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Models;
using AkeroGarden.Web.Services;
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
            s.AddScoped<BookingEmailService>();
            s.AddScoped<RoomAvailabilityService>();
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

    /// <summary>Creates a Pending stay, approves it, and returns (bookingId, token).</summary>
    private static async Task<(Guid Id, string Token)> ApprovedBookingAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BookingService>();
        var booking = await service.CreateStayRequestAsync(new StayRequestInput
        {
            ContactName = "Gäst",
            ContactEmail = $"guest-{Guid.NewGuid():N}@test.se",
            CheckIn = new DateOnly(2026, 12, 1),
            CheckOut = new DateOnly(2026, 12, 4),
            OvernightGuestCount = 4,
            MealPlan = MealPlan.FullBoardAll,
            AcceptPrivacyPolicy = true
        }, createdByAdmin: true);
        await service.ApproveAsync(booking.Id);

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var token = (await db.Bookings.FindAsync(booking.Id))!.CompletionToken!;
        return (booking.Id, token);
    }

    [Fact]
    public async Task ApprovedBooking_TokenResolves_AndIsValid()
    {
        var provider = NewProvider();
        var (id, token) = await ApprovedBookingAsync(provider);

        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BookingService>();

        var (access, booking) = await service.EvaluateCompletionAccessAsync(token, DateTime.UtcNow);

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

        var (access, booking) = await service.EvaluateCompletionAccessAsync(token, DateTime.UtcNow);

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
        var (access, _) = await service.EvaluateCompletionAccessAsync(token, DateTime.UtcNow.AddDays(60));

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
            var (access, _) = await service.EvaluateCompletionAccessAsync(token, DateTime.UtcNow);
            Assert.Equal(CompletionAccess.AlreadyCompleted, access);
        }
    }
}
