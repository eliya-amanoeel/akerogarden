using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;
using AkeroGarden.Web.Core.Services;
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

        // Approval now requires a lodging assignment (FR-103/D1), so a "pending stay ready to be
        // approved" is not realistic without one. See BuildingAssignmentSeed for why this is
        // arranged rather than the precondition being relaxed.
        await BuildingAssignmentSeed.AssignBuildingAsync(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), b.Id);
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
    public async Task Approve_WithinIssueWindow_GeneratesToken_SetsApproved_AndSendsApprovalEmail()
    {
        var provider = NewProvider(out var email);
        // Inside the 40-day issue window but outside the 14-day short-expiry one (FR-51/75/108).
        var id = await CreatePendingStayAsync(provider, checkIn: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));

        ApproveResult result;
        using (var scope = provider.CreateScope())
            result = await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(id);

        Assert.True(result.Succeeded);
        Assert.False(result.ShortExpiryWarning);
        Assert.NotNull(result.TokenExpiry);

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.Approved, booking.Status);
        Assert.False(string.IsNullOrEmpty(booking.CompletionToken));
        Assert.NotNull(booking.CompletionTokenExpiry);
        Assert.NotNull(booking.CompletionLinkSentAt);
        Assert.Null(booking.DeleteAfter); // live state

        var approval = Assert.Single(email.Sent, e => e.Subject.Contains("godkänd"));
        Assert.Contains("/slutfor/", approval.HtmlBody);
    }

    [Fact]
    public async Task Approve_BeyondIssueWindow_IssuesNoLink_AndTheEmailPromisesOneLater()
    {
        // FR-51/52/108: a group booking six months out cannot name its participants yet, so no token
        // is stored and the approval email carries no link — it says one follows nearer the stay.
        var provider = NewProvider(out var email);
        var id = await CreatePendingStayAsync(provider, checkIn: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(180));

        ApproveResult result;
        using (var scope = provider.CreateScope())
            result = await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(id);

        Assert.True(result.Succeeded);
        Assert.Null(result.TokenExpiry);

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.Approved, booking.Status);
        Assert.Null(booking.CompletionToken);
        Assert.Null(booking.CompletionTokenExpiry);
        Assert.Null(booking.CompletionLinkSentAt); // what the issuance job looks for

        // The guest is still told they are approved — but with no link and no expiry to miss.
        var approval = Assert.Single(email.Sent, e => e.Subject.Contains("godkänd"));
        Assert.DoesNotContain("/slutfor/", approval.HtmlBody);
        Assert.Contains("sex veckor före ankomst", approval.HtmlBody);
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

    /// <summary>Forces a booking to a given status directly (bypassing the guest completion form)
    /// so the admin lifecycle-end transitions can be exercised in isolation.</summary>
    private static async Task SetStatusAsync(IServiceProvider provider, Guid id, BookingStatus status)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var booking = await db.Bookings.SingleAsync(b => b.Id == id);
        booking.Status = status;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task MarkArrived_FromDetailsComplete_SetsActive_NoEmail_KeepsDeleteAfterNull()
    {
        var provider = NewProvider(out var email);
        var id = await CreatePendingStayAsync(provider);
        await SetStatusAsync(provider, id, BookingStatus.DetailsComplete);

        using (var scope = provider.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<BookingService>().MarkArrivedAsync(id)).Succeeded);

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.Active, booking.Status);
        Assert.Null(booking.DeleteAfter); // Active is a live state (FR-91)
        Assert.Empty(email.Sent); // no guest email on "arrived"
    }

    [Fact]
    public async Task MarkCompleted_FromActive_SetsCompleted_SetsRetention_AndSendsThankYou()
    {
        var provider = NewProvider(out var email);
        var checkOut = new DateOnly(2026, 12, 4);
        var id = await CreatePendingStayAsync(provider, checkIn: checkOut.AddDays(-3));
        await SetStatusAsync(provider, id, BookingStatus.Active);

        using (var scope = provider.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<BookingService>().MarkCompletedAsync(id)).Succeeded);

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.Completed, booking.Status);
        Assert.Equal(checkOut.AddYears(2), booking.DeleteAfter); // FR-91: check-out + 2 years
        Assert.Contains(email.Sent, e => e.Subject.Contains("Tack för ditt besök"));
    }

    [Fact]
    public async Task MarkCompleted_FromDetailsComplete_IsBlocked_ByStateMachine()
    {
        var provider = NewProvider(out _);
        var id = await CreatePendingStayAsync(provider);
        await SetStatusAsync(provider, id, BookingStatus.DetailsComplete);

        using var scope = provider.CreateScope();
        // §2.5: Completed is only reachable from Active, not directly from DetailsComplete.
        Assert.False((await scope.ServiceProvider.GetRequiredService<BookingService>().MarkCompletedAsync(id)).Succeeded);
    }

    [Fact]
    public async Task MarkArrived_FromApproved_IsBlocked_ByStateMachine()
    {
        var provider = NewProvider(out _);
        var id = await CreatePendingStayAsync(provider);
        await SetStatusAsync(provider, id, BookingStatus.Approved);

        using var scope = provider.CreateScope();
        // §2.5: Active is only reachable from DetailsComplete.
        Assert.False((await scope.ServiceProvider.GetRequiredService<BookingService>().MarkArrivedAsync(id)).Succeeded);
    }

    [Fact]
    public async Task RegenerateLink_ReplacesToken_AndRequiresApproved()
    {
        var provider = NewProvider(out _);
        // Inside the issue window, so approval really does produce a first token to replace.
        var id = await CreatePendingStayAsync(provider, checkIn: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));

        using var scope = provider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<BookingService>();

        Assert.False((await svc.RegenerateCompletionLinkAsync(id)).Succeeded); // not Approved yet

        await svc.ApproveAsync(id);
        var first = (await ReloadAsync(provider, id)).CompletionToken;
        Assert.False(string.IsNullOrEmpty(first));

        Assert.True((await svc.RegenerateCompletionLinkAsync(id)).Succeeded);
        var second = (await ReloadAsync(provider, id)).CompletionToken;

        Assert.False(string.IsNullOrEmpty(second));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task RegenerateLink_SendsTheLinkEarly_ToABookingThatHasNoneYet()
    {
        // FR-49/108: this is how a guest who already knows their participants gets the form before
        // the automatic issue at 40 days. It must send the "you can fill in your details" email, not
        // a second copy of the approval the guest read months ago.
        var provider = NewProvider(out var email);
        var id = await CreatePendingStayAsync(provider, checkIn: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(180));

        using var scope = provider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<BookingService>();
        await svc.ApproveAsync(id);
        Assert.Null((await ReloadAsync(provider, id)).CompletionToken); // precondition: no link yet

        Assert.True((await svc.RegenerateCompletionLinkAsync(id)).Succeeded);

        var booking = await ReloadAsync(provider, id);
        Assert.False(string.IsNullOrEmpty(booking.CompletionToken));
        Assert.NotNull(booking.CompletionLinkSentAt);

        var linkMail = Assert.Single(email.Sent, e => e.Subject.Contains("fylla i era uppgifter"));
        Assert.Contains("/slutfor/", linkMail.HtmlBody);
        Assert.Single(email.Sent, e => e.Subject.Contains("godkänd")); // the approval was not re-sent
    }

    [Fact]
    public async Task GetBookingsAsync_FiltersByReference_FR36_92()
    {
        var provider = NewProvider(out _);
        var wantedId = await CreatePendingStayAsync(provider);
        await CreatePendingStayAsync(provider); // a second, unrelated booking in the same list

        using var scope = provider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<BookingService>();
        var wanted = await ReloadAsync(provider, wantedId);

        var exact = await svc.GetBookingsAsync(reference: wanted.BookingReference);
        Assert.Single(exact);
        Assert.Equal(wantedId, exact[0].Id);

        // Partial match (what an admin would actually type) also finds it.
        var partial = await svc.GetBookingsAsync(reference: wanted.BookingReference[..^2]);
        Assert.Contains(partial, b => b.Id == wantedId);

        var none = await svc.GetBookingsAsync(reference: "AG-9999-9999");
        Assert.Empty(none);
    }
}
