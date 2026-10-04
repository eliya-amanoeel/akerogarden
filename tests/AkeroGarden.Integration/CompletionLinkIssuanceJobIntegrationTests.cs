using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the deferred completion-link issuance job against real PostgreSQL
/// (§2.6, FR-108): a booking approved beyond the 40-day window receives its link when check-in
/// comes inside it, exactly once, and only while it is still awaiting completion.
/// </summary>
public class CompletionLinkIssuanceJobIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public CompletionLinkIssuanceJobIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private IServiceProvider NewProvider(out FakeEmailService email)
    {
        var fake = new FakeEmailService();
        email = fake;
        return _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(fake);
            // Pinned, not TimeProvider.System: every assertion in this file is about a *distance*
            // (the 40-day issue window, the 14-day nudge, the link outliving it), which is the one
            // thing a midnight crossing breaks. Every date below is then taken from this same clock
            // via DbFixture.TodayIn — a pinned service with a wall-clock seed holds two disagreeing
            // "now"s and is worse than not pinning.
            DbFixture.PinClock(s, DbFixture.FixedNow);
            s.AddScoped<BookingEmailService>();
            s.AddScoped<BookingService>();
            s.AddScoped<CleaningZoneService>();
            s.AddScoped<CompletionLinkIssuanceJob>();
            s.Configure<BookingEmailOptions>(o =>
            {
                o.PublicBaseUrl = "https://akerogarden.se";
                o.AdminBaseUrl = "https://admin.akerogarden.se";
                o.AdminNotificationEmail = "admin@test.se";
                o.ContactEmail = "info@test.se";
                o.PropertyAddress = "Åkerögården, Norrtälje";
            });
        });
    }

    private static async Task<Guid> SeedApprovedAsync(
        IServiceProvider provider,
        DateOnly checkIn,
        BookingStatus status = BookingStatus.Approved,
        DateTime? linkSentAt = null)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // The container's clock, which is pinned — not DateTime.UtcNow. The expiry seeded here is
        // compared against the job's `today`, so the two must come from the same instant.
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingType = BookingType.StayRequest,
            Status = status,
            ContactName = "Gäst",
            ContactEmail = $"g-{Guid.NewGuid():N}@test.se",
            CheckIn = checkIn,
            CheckOut = checkIn.AddDays(3),
            OvernightGuestCount = 4,
            MealPlan = MealPlan.FullBoardAll,
            CompletionLinkSentAt = linkSentAt,
            CompletionToken = linkSentAt is null ? null : "already-issued",
            CompletionTokenExpiry = linkSentAt is null ? null : now.AddDays(20),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        booking.BookingReference = BookingReferencePolicy.Format(booking.SequenceNumber, booking.CreatedAt.Year);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private async Task<int> RunAsync(IServiceProvider provider, DateOnly today)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CompletionLinkIssuanceJob>().RunAsync(today);
    }

    /// <summary>The pinned instant this container's clock holds. Every date a test compares must
    /// come from here, so the seed, the job's <c>today</c> and the assertions share one "now".</summary>
    private static DateTime NowIn(IServiceProvider provider) =>
        provider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

    private async Task<Booking> ReloadAsync(IServiceProvider provider, Guid id)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Bookings.SingleAsync(b => b.Id == id);
    }

    [Fact]
    public async Task WithinTheWindow_IssuesTheLink_EmailsIt_AndDoesNotRepeat()
    {
        var provider = NewProvider(out var email);
        var today = DbFixture.TodayIn(provider);
        var id = await SeedApprovedAsync(provider, checkIn: today.AddDays(30));

        var issued = await RunAsync(provider, today);

        Assert.Equal(1, issued);

        var booking = await ReloadAsync(provider, id);
        Assert.False(string.IsNullOrEmpty(booking.CompletionToken));
        Assert.NotNull(booking.CompletionTokenExpiry);
        Assert.NotNull(booking.CompletionLinkSentAt);

        var mail = Assert.Single(email.Sent, e => e.Subject.Contains("fylla i era uppgifter"));
        Assert.Equal(booking.ContactEmail, mail.ToAddress);
        Assert.Contains("/slutfor/", mail.HtmlBody);

        // The live expiry is what stops a second issue — otherwise the guest is mailed nightly for
        // 40 days, each mail killing the link in the one before it.
        Assert.Equal(0, await RunAsync(provider, today.AddDays(1)));
    }

    [Fact]
    public async Task TheIssuedLinkStillWorksAtTheNudge()
    {
        // Why the lead time is 40 and not 44 (§2.6). If the link the job issues cannot survive to the
        // 14-day nudge that replaces it, the dead-link gap deferred issuance exists to close is back.
        var provider = NewProvider(out _);
        var today = DbFixture.TodayIn(provider);
        var checkIn = today.AddDays(CompletionTokenPolicy.IssueLeadDays);
        var id = await SeedApprovedAsync(provider, checkIn: checkIn);

        Assert.Equal(1, await RunAsync(provider, today));

        var expiry = (await ReloadAsync(provider, id)).CompletionTokenExpiry!.Value;
        var nudgeInstant = StockholmTime.ToUtc(
            checkIn.AddDays(-CompletionTokenPolicy.CheckInLeadDays), CompletionTokenPolicy.StandardCheckInTime);

        Assert.True(CompletionTokenPolicy.IsValidAt(expiry, nudgeInstant.AddSeconds(-1)),
            $"link expired {expiry:u}, before the nudge at {nudgeInstant:u}");
    }

    [Fact]
    public async Task BeyondTheWindow_IssuesNothing()
    {
        var provider = NewProvider(out var email);
        var today = DbFixture.TodayIn(provider);
        var id = await SeedApprovedAsync(provider, checkIn: today.AddDays(60));

        Assert.Equal(0, await RunAsync(provider, today));

        Assert.Null((await ReloadAsync(provider, id)).CompletionToken);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task ABookingHoldingALiveLink_IsLeftAlone()
    {
        // Approval inside the window issues the link itself; the job must not replace it and mail a
        // second one, which would invalidate the URL the guest is already holding.
        var provider = NewProvider(out var email);
        var today = DbFixture.TodayIn(provider);
        var id = await SeedApprovedAsync(provider, checkIn: today.AddDays(20),
            linkSentAt: NowIn(provider).AddDays(-1));

        Assert.Equal(0, await RunAsync(provider, today));

        Assert.Equal("already-issued", (await ReloadAsync(provider, id)).CompletionToken);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task AnEarlySentLinkThatHasExpired_IsReplacedWhenCheckInReachesTheWindow()
    {
        // The admin-resend path (FR-49). A guest asks for the form months ahead; the link they are
        // sent dies at its 30-day ceiling with check-in still far off. Keyed on "was a link ever
        // sent", the job would count this booking as served and never replace it, leaving the guest
        // holding a dead URL until the 14-day nudge — the gap FR-108 exists to close, reopened
        // through the one path that is allowed to issue outside the window (found 2026-08-10).
        var provider = NewProvider(out var email);
        var today = DbFixture.TodayIn(provider);
        var checkIn = today.AddDays(CompletionTokenPolicy.IssueLeadDays);

        var id = await SeedApprovedAsync(provider, checkIn: checkIn);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var b = await db.Bookings.SingleAsync(x => x.Id == id);
            b.CompletionToken = "early-link-now-dead";
            b.CompletionTokenExpiry = NowIn(provider).AddDays(-1); // sent early, since expired
            b.CompletionLinkSentAt = NowIn(provider).AddDays(-31);
            await db.SaveChangesAsync();
        }

        Assert.Equal(1, await RunAsync(provider, today));

        var booking = await ReloadAsync(provider, id);
        Assert.NotEqual("early-link-now-dead", booking.CompletionToken);
        Assert.True(booking.CompletionTokenExpiry > NowIn(provider), "the replacement link must be live");
        Assert.Single(email.Sent, e => e.Subject.Contains("fylla i era uppgifter"));

        // And the replacement survives to the nudge that replaces it in turn (§2.6).
        var nudgeInstant = StockholmTime.ToUtc(
            checkIn.AddDays(-CompletionTokenPolicy.CheckInLeadDays), CompletionTokenPolicy.StandardCheckInTime);
        Assert.True(CompletionTokenPolicy.IsValidAt(booking.CompletionTokenExpiry!.Value, nudgeInstant.AddSeconds(-1)));
    }

    [Fact]
    public async Task AnExpiredLinkIsNotReplacedWhileCheckInIsStillBeyondTheWindow()
    {
        // The mirror of the test above: a dead link on a booking six months out is left dead. The
        // guest does not need one yet, and issuing here would restart the 30-day ceiling early and
        // reproduce the original problem one window further along.
        var provider = NewProvider(out var email);
        var today = DbFixture.TodayIn(provider);
        var id = await SeedApprovedAsync(provider, checkIn: today.AddDays(180));

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var b = await db.Bookings.SingleAsync(x => x.Id == id);
            b.CompletionToken = "early-link-now-dead";
            b.CompletionTokenExpiry = NowIn(provider).AddDays(-1);
            b.CompletionLinkSentAt = NowIn(provider).AddDays(-31);
            await db.SaveChangesAsync();
        }

        Assert.Equal(0, await RunAsync(provider, today));

        Assert.Equal("early-link-now-dead", (await ReloadAsync(provider, id)).CompletionToken);
        Assert.Empty(email.Sent);
    }

    [Theory]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.DetailsComplete)]
    [InlineData(BookingStatus.Denied)]
    [InlineData(BookingStatus.Cancelled)]
    public async Task OnlyBookingsAwaitingCompletionAreIssuedALink(BookingStatus status)
    {
        var provider = NewProvider(out var email);
        var today = DbFixture.TodayIn(provider);
        var id = await SeedApprovedAsync(provider, checkIn: today.AddDays(30), status: status);

        Assert.Equal(0, await RunAsync(provider, today));

        Assert.Null((await ReloadAsync(provider, id)).CompletionToken);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task PastCheckIn_IsNotIssuedALink()
    {
        var provider = NewProvider(out var email);
        var today = DbFixture.TodayIn(provider);
        var id = await SeedApprovedAsync(provider, checkIn: today.AddDays(-1));

        Assert.Equal(0, await RunAsync(provider, today));

        Assert.Null((await ReloadAsync(provider, id)).CompletionToken);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task A_link_email_that_could_not_be_sent_is_recorded_as_a_failed_send()
    {
        // FR-114: the issuance is recorded either way; a failed email adds its own entry, which is
        // what marks the night's run Misslyckades instead of reporting a link nobody received.
        var provider = NewProvider(out var email);
        email.Delivers = false;
        var today = DbFixture.TodayIn(provider);
        var id = await SeedApprovedAsync(provider, checkIn: today.AddDays(29));

        await RunAsync(provider, today);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actions = await db.AuditLogs.Where(a => a.BookingId == id).Select(a => a.Action).ToListAsync();
        Assert.Contains(AuditAction.CompletionLinkIssued, actions);
        Assert.Contains(AuditAction.EmailSendFailed, actions);
    }
}
