using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the two 14-day emails against real PostgreSQL (§9, FR-63/64): the
/// pre-arrival reminder for DetailsComplete+, the regenerate-link + nudge + admin-alert path for
/// still-Approved bookings, the nudge's 3-day grace period, once-only firing of each under its own
/// flag, and correct exclusion of out-of-window / ineligible / past-check-in bookings.
/// </summary>
public class ReminderJobIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public ReminderJobIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private IServiceProvider NewProvider(out FakeEmailService email)
    {
        var fake = new FakeEmailService();
        email = fake;
        return _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(fake);
            // Pinned: this file asserts distances — the 14-day nudge, the grace period before it,
            // and how much life the replacement link has left. Every date below comes from this
            // same clock, never from DateTime.UtcNow, so the seed and the job's `today` cannot
            // disagree across a midnight.
            DbFixture.PinClock(s, DbFixture.FixedNow);
            s.AddScoped<BookingEmailService>();
            s.AddScoped<BookingService>();
            s.AddScoped<CleaningZoneService>();
            s.AddScoped<ReminderJob>();
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

    private static async Task<Guid> SeedAsync(
        IServiceProvider provider,
        BookingStatus status,
        DateOnly checkIn,
        bool withToken = false,
        bool withParticipant = false,
        DateTime? linkSentAt = null,
        DateTime? nudgeSentAt = null,
        DateTime? reminderSentAt = null)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
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
            CompletionToken = withToken ? "old-token" : null,
            CompletionTokenExpiry = withToken ? now.AddDays(1) : null,
            CompletionLinkSentAt = linkSentAt,
            CompletionNudgeSentAt = nudgeSentAt,
            ReminderEmailSentAt = reminderSentAt,
            CreatedAt = now,
            UpdatedAt = now,
        };
        if (withParticipant)
            booking.Participants.Add(new Participant
            {
                Id = Guid.NewGuid(),
                FullName = "Deltagare",
                ParticipantType = ParticipantType.Overnight,
                AgeCategory = AgeCategory.Adult,
            });
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        booking.BookingReference = BookingReferencePolicy.Format(booking.SequenceNumber, booking.CreatedAt.Year);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    /// <summary>The pinned instant this container holds — the only source of "now" in this file.</summary>
    private static DateTime NowIn(IServiceProvider provider) =>
        provider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

    private async Task<ReminderRunSummary> RunAsync(IServiceProvider provider, DateOnly today)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ReminderJob>().RunAsync(today);
    }

    private async Task<Booking> ReloadAsync(IServiceProvider provider, Guid id)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Bookings.SingleAsync(b => b.Id == id);
    }

    [Fact]
    public async Task DetailsComplete_WithinWindow_SendsStandardReminder_Once()
    {
        var provider = NewProvider(out var email);
        var today = new DateOnly(2026, 7, 5);
        var id = await SeedAsync(provider, BookingStatus.DetailsComplete, checkIn: today.AddDays(14), withParticipant: true);

        var summary = await RunAsync(provider, today);

        Assert.Equal(1, summary.StandardReminders);
        Assert.Equal(0, summary.Nudges);
        Assert.Contains(email.Sent, e => e.ToAddress != "admin@test.se" && e.Subject.Contains("Påminnelse inför"));
        Assert.NotNull((await ReloadAsync(provider, id)).ReminderEmailSentAt);

        // Second run does not re-send.
        var second = await RunAsync(provider, today);
        Assert.Equal(0, second.StandardReminders);
    }

    [Fact]
    public async Task Approved_WithinWindow_RegeneratesLink_NudgesGuest_AndAlertsAdmin()
    {
        var provider = NewProvider(out var email);
        var today = new DateOnly(2026, 7, 5);
        var id = await SeedAsync(provider, BookingStatus.Approved, checkIn: today.AddDays(10),
            withToken: true, linkSentAt: NowIn(provider).AddDays(-10)); // link long past its grace

        var summary = await RunAsync(provider, today);

        Assert.Equal(1, summary.Nudges);
        Assert.Equal(0, summary.StandardReminders);

        var booking = await ReloadAsync(provider, id);
        Assert.NotEqual("old-token", booking.CompletionToken); // regenerated (I13)
        Assert.NotNull(booking.CompletionNudgeSentAt);
        Assert.Null(booking.ReminderEmailSentAt); // the nudge must not consume the pre-arrival flag

        Assert.Contains(email.Sent, e => e.ToAddress != "admin@test.se" && e.Subject.Contains("slutför dina"));       // guest nudge
        Assert.Contains(email.Sent, e => e.ToAddress == "admin@test.se" && e.Subject.Contains("inte slutfört"));       // admin alert

        // Second run does not re-nudge.
        var second = await RunAsync(provider, today);
        Assert.Equal(0, second.Nudges);
    }

    [Fact]
    public async Task TheNudgesFreshLink_RunsToThreeDaysBeforeCheckIn()
    {
        // Dates are relative to the real clock on purpose: the nudge computes its expiry from
        // TimeProvider, so a fixed seeded 'today' would compare against a check-in months in the
        // past and the assertion would be meaningless.
        //
        // The nudge is the "you are late" email, and the link it carries used to last 48 hours
        // because the 03:00 run read as earlier than the 14:00 check-in instant. It must give the
        // same window a late approval gets (FR-53).
        var provider = NewProvider(out _);
        var today = DbFixture.TodayIn(provider);
        var checkIn = today.AddDays(ReminderSchedule.LeadDays);
        var id = await SeedAsync(provider, BookingStatus.Approved, checkIn: checkIn,
            withToken: true, linkSentAt: NowIn(provider).AddDays(-10));

        Assert.Equal(1, (await RunAsync(provider, today)).Nudges);

        var expiry = (await ReloadAsync(provider, id)).CompletionTokenExpiry!.Value;
        var expected = StockholmTime.ToUtc(
            checkIn.AddDays(-CompletionTokenPolicy.LateIssueLeadDays), CompletionTokenPolicy.StandardCheckInTime);

        Assert.Equal(expected, expiry);
        Assert.True(expiry - NowIn(provider) > TimeSpan.FromDays(10),
            $"the nudge link expires {expiry:u}, leaving the guest under ten days to respond");
    }

    [Fact]
    public async Task Nudge_IsHeldForThreeDaysAfterTheLinkIsSent_ThenFires()
    {
        // FR-63: approved at 15:00 must not be told at 03:00 the next morning that they have not
        // filled in their details. The nudge is deferred, not skipped — so the second run, once the
        // grace has passed on the same unchanged booking, must send it.
        var provider = NewProvider(out var email);
        var today = new DateOnly(2026, 7, 5);
        var id = await SeedAsync(provider, BookingStatus.Approved, checkIn: today.AddDays(10),
            withToken: true, linkSentAt: NowIn(provider).AddHours(-13));

        var first = await RunAsync(provider, today);

        Assert.Equal(0, first.Nudges);
        Assert.Empty(email.Sent);
        Assert.Null((await ReloadAsync(provider, id)).CompletionNudgeSentAt);

        // Age the link past the grace; nothing else about the booking changes.
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var b = await db.Bookings.SingleAsync(x => x.Id == id);
            b.CompletionLinkSentAt = NowIn(provider) - ReminderSchedule.NudgeGrace.Add(TimeSpan.FromHours(1));
            await db.SaveChangesAsync();
        }

        var second = await RunAsync(provider, today.AddDays(1));

        Assert.Equal(1, second.Nudges);
        Assert.NotNull((await ReloadAsync(provider, id)).CompletionNudgeSentAt);
    }

    [Fact]
    public async Task NudgedGuestWhoThenSubmits_StillReceivesThePreArrivalReminder()
    {
        // The defect the two flags exist to fix. While one flag guarded both emails, the nudge set it
        // and this guest never received the address or their stated times at all.
        var provider = NewProvider(out var email);
        var today = new DateOnly(2026, 7, 5);
        var checkIn = today.AddDays(14);
        var id = await SeedAsync(provider, BookingStatus.Approved, checkIn: checkIn,
            withToken: true, linkSentAt: NowIn(provider).AddDays(-10), withParticipant: true);

        var nudgeRun = await RunAsync(provider, today);
        Assert.Equal(1, nudgeRun.Nudges);

        // The guest fills in the form two days later.
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var b = await db.Bookings.SingleAsync(x => x.Id == id);
            b.Status = BookingStatus.DetailsComplete;
            await db.SaveChangesAsync();
        }

        var reminderRun = await RunAsync(provider, today.AddDays(2));

        Assert.Equal(1, reminderRun.StandardReminders);
        Assert.NotNull((await ReloadAsync(provider, id)).ReminderEmailSentAt);
        Assert.Contains(email.Sent, e => e.Subject.Contains("Påminnelse inför"));
    }

    [Fact]
    public async Task OutsideWindow_Ineligible_OrPastCheckIn_AreSkipped()
    {
        var provider = NewProvider(out var email);
        var today = new DateOnly(2026, 7, 5);
        await SeedAsync(provider, BookingStatus.DetailsComplete, checkIn: today.AddDays(20));  // too far out
        await SeedAsync(provider, BookingStatus.Pending, checkIn: today.AddDays(7));           // ineligible status
        await SeedAsync(provider, BookingStatus.Completed, checkIn: today.AddDays(7));         // terminal
        await SeedAsync(provider, BookingStatus.DetailsComplete, checkIn: today.AddDays(-1));  // check-in already passed

        var summary = await RunAsync(provider, today);

        Assert.Equal(0, summary.StandardReminders);
        Assert.Equal(0, summary.Nudges);
        Assert.Empty(email.Sent);
    }

    // ── Job history (FR-114): what the run did is in the audit trail ────────────────

    private async Task<List<AuditLog>> AuditFor(IServiceProvider provider, Guid bookingId)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AuditLogs.Where(a => a.BookingId == bookingId).ToListAsync();
    }

    [Fact]
    public async Task A_sent_pre_arrival_reminder_is_recorded_for_the_job_history()
    {
        var provider = NewProvider(out _);
        var today = new DateOnly(2026, 7, 5);
        var id = await SeedAsync(provider, BookingStatus.DetailsComplete, checkIn: today.AddDays(13), withParticipant: true);

        await RunAsync(provider, today);

        var entry = Assert.Single(await AuditFor(provider, id));
        Assert.Equal(AuditAction.PreArrivalReminderSent, entry.Action);
        Assert.Equal(AuditActorType.System, entry.ActorType);
    }

    [Fact]
    public async Task A_reminder_that_could_not_be_sent_is_recorded_as_a_failed_send()
    {
        // Sending never throws (NFR-13). Without this entry the failure left no trace in the data.
        var provider = NewProvider(out var email);
        email.Delivers = false;
        var today = new DateOnly(2026, 7, 5);
        var id = await SeedAsync(provider, BookingStatus.DetailsComplete, checkIn: today.AddDays(12), withParticipant: true);

        await RunAsync(provider, today);

        var entry = Assert.Single(await AuditFor(provider, id));
        Assert.Equal(AuditAction.EmailSendFailed, entry.Action);
    }

    [Fact]
    public async Task A_nudge_whose_emails_fail_records_one_failed_send_per_email()
    {
        var provider = NewProvider(out var email);
        email.Delivers = false;
        var today = new DateOnly(2026, 7, 5);
        var id = await SeedAsync(provider, BookingStatus.Approved, checkIn: today.AddDays(9),
            withToken: true, linkSentAt: NowIn(provider).AddDays(-10));

        await RunAsync(provider, today);

        var entries = await AuditFor(provider, id);
        Assert.Single(entries, e => e.Action == AuditAction.CompletionLinkIssued);
        Assert.Equal(2, entries.Count(e => e.Action == AuditAction.EmailSendFailed)); // guest + admin
    }
}
