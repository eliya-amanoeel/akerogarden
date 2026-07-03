using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Models;
using AkeroGarden.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for admin booking management against real PostgreSQL (FR-38/50/76–80,
/// §2.5): approve/deny/close/cancel behave per spec with token generation and the one-way state
/// machine enforced (illegal transitions and wrong-type actions are blocked). Emails are captured
/// via <see cref="FakeEmailService"/>.
/// </summary>
public class BookingAdminIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public BookingAdminIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private IServiceProvider NewProvider(out FakeEmailService email)
    {
        var fake = new FakeEmailService();
        email = fake;
        return _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(fake);
            s.AddScoped<BookingEmailService>();
            s.AddScoped<RoomAvailabilityService>();
            s.AddScoped<BookingService>();
            s.Configure<BookingEmailOptions>(o =>
            {
                o.PublicBaseUrl = "https://akerogarden.se";
                o.AdminBaseUrl = "https://admin.akerogarden.se";
                o.AdminNotificationEmail = "admin@test.se";
                o.ContactEmail = "info@test.se";
            });
        });
    }

    private static async Task<Guid> CreatePendingStayAsync(IServiceProvider provider, DateOnly? checkIn = null)
    {
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BookingService>();
        var b = await service.CreateStayRequestAsync(new StayRequestInput
        {
            ContactName = "Gäst",
            ContactEmail = $"guest-{Guid.NewGuid():N}@test.se",
            CheckIn = checkIn ?? new DateOnly(2026, 12, 1),
            CheckOut = (checkIn ?? new DateOnly(2026, 12, 1)).AddDays(3),
            OvernightGuestCount = 5,
            MealPlan = MealPlan.FullBoardAll,
            AcceptPrivacyPolicy = true
        }, createdByAdmin: true); // suppress creation emails so we can assert transition emails
        return b.Id;
    }

    private static async Task<Guid> CreatePendingGeneralAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BookingService>();
        var b = await service.CreateGeneralInquiryAsync(new GeneralInquiryInput
        {
            ContactName = "Fråga",
            ContactEmail = $"ask-{Guid.NewGuid():N}@test.se",
            Message = "Hej",
            AcceptPrivacyPolicy = true
        }, createdByAdmin: true);
        return b.Id;
    }

    private static async Task<Booking> ReloadAsync(IServiceProvider provider, Guid id)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Bookings.SingleAsync(b => b.Id == id);
    }

    [Fact]
    public async Task Approve_GeneratesToken_SetsApproved_AndSendsApprovalEmail()
    {
        var provider = NewProvider(out var email);
        var id = await CreatePendingStayAsync(provider, checkIn: new DateOnly(2026, 12, 1));

        ApproveResult result;
        using (var scope = provider.CreateScope())
            result = await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(id);

        Assert.True(result.Succeeded);
        Assert.False(result.ShortExpiryWarning); // check-in far away

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.Approved, booking.Status);
        Assert.False(string.IsNullOrEmpty(booking.CompletionToken));
        Assert.NotNull(booking.CompletionTokenExpiry);
        Assert.Null(booking.DeleteAfter); // live state
        Assert.Contains(email.Sent, e => e.Subject.Contains("godkänd"));
    }

    [Fact]
    public async Task Approve_WithNearCheckIn_FlagsShortExpiryWarning()
    {
        var provider = NewProvider(out _);
        var id = await CreatePendingStayAsync(provider, checkIn: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5));

        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(id);

        Assert.True(result.Succeeded);
        Assert.True(result.ShortExpiryWarning); // FR-75
    }

    [Fact]
    public async Task Approve_RejectsGeneralInquiry()
    {
        var provider = NewProvider(out _);
        var id = await CreatePendingGeneralAsync(provider);

        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(id);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Deny_RequiresReason_ThenSetsDenied_AndSendsEmail()
    {
        var provider = NewProvider(out var email);
        var id = await CreatePendingStayAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<BookingService>();
            Assert.False((await svc.DenyAsync(id, "  ")).Succeeded); // blank reason rejected
            Assert.True((await svc.DenyAsync(id, "Fullbokat")).Succeeded);
        }

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.Denied, booking.Status);
        Assert.Equal("Fullbokat", booking.DenialReason);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(12), booking.DeleteAfter);
        Assert.Contains(email.Sent, e => e.Subject.Contains("bokningsförfrågan"));
    }

    [Fact]
    public async Task Close_GeneralInquiry_SetsClosed_NoEmail()
    {
        var provider = NewProvider(out var email);
        var id = await CreatePendingGeneralAsync(provider);

        using (var scope = provider.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<BookingService>().CloseAsync(id)).Succeeded);

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.Closed, booking.Status);
        Assert.Empty(email.Sent); // FR-80: admin replies outside the system
    }

    [Fact]
    public async Task Close_RejectsStayRequest()
    {
        var provider = NewProvider(out _);
        var id = await CreatePendingStayAsync(provider);

        using var scope = provider.CreateScope();
        Assert.False((await scope.ServiceProvider.GetRequiredService<BookingService>().CloseAsync(id)).Succeeded);
    }

    [Fact]
    public async Task Cancel_FromApproved_SetsCancelled_AndSendsEmail()
    {
        var provider = NewProvider(out var email);
        var id = await CreatePendingStayAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<BookingService>();
            await svc.ApproveAsync(id);
            Assert.True((await svc.CancelAsync(id, CancellationInitiator.PropertyInitiated, "Renovering")).Succeeded);
        }

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.NotNull(booking.CancelledAt);
        Assert.Equal(CancellationInitiator.PropertyInitiated, booking.CancellationInitiator);
        Assert.Equal("Renovering", booking.CancellationReason);
        Assert.Contains(email.Sent, e => e.Subject.Contains("avbokats"));
    }

    [Fact]
    public async Task Cancel_FromPending_IsBlocked()
    {
        var provider = NewProvider(out _);
        var id = await CreatePendingStayAsync(provider);

        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<BookingService>()
            .CancelAsync(id, CancellationInitiator.GuestInitiated, null);
        Assert.False(result.Succeeded); // §2.5: Pending is denied, not cancelled
    }

    [Fact]
    public async Task Approve_Twice_IsBlocked_ByStateMachine()
    {
        var provider = NewProvider(out _);
        var id = await CreatePendingStayAsync(provider);

        using var scope = provider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<BookingService>();
        Assert.True((await svc.ApproveAsync(id)).Succeeded);
        Assert.False((await svc.ApproveAsync(id)).Succeeded); // Approved -> Approved not allowed
    }

    [Fact]
    public async Task RegenerateLink_ReplacesToken_AndRequiresApproved()
    {
        var provider = NewProvider(out _);
        var id = await CreatePendingStayAsync(provider);

        using var scope = provider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<BookingService>();

        Assert.False((await svc.RegenerateCompletionLinkAsync(id)).Succeeded); // not Approved yet

        await svc.ApproveAsync(id);
        var first = (await ReloadAsync(provider, id)).CompletionToken;
        Assert.True((await svc.RegenerateCompletionLinkAsync(id)).Succeeded);
        var second = (await ReloadAsync(provider, id)).CompletionToken;

        Assert.NotEqual(first, second);
    }
}
