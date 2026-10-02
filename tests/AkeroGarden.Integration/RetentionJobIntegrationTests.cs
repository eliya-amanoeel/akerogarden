using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the GDPR retention job against real PostgreSQL (§2.7, FR-66/69/91):
/// records past <c>DeleteAfter</c> are deleted (cascading to participants), the 30-day pre-deletion
/// admin warning fires exactly once (de-duplicated via <c>DeletionWarningEmailSentAt</c>), and live
/// in-progress bookings (null <c>DeleteAfter</c>) are never touched.
/// </summary>
public class RetentionJobIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public RetentionJobIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private IServiceProvider NewProvider(out FakeEmailService email)
    {
        var fake = new FakeEmailService();
        email = fake;
        return _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(fake);
            s.AddSingleton(TimeProvider.System);
            s.AddScoped<BookingEmailService>();
            s.AddScoped<RetentionJob>();
            s.Configure<BookingEmailOptions>(o =>
            {
                o.PublicBaseUrl = "https://akerogarden.se";
                o.AdminBaseUrl = "https://admin.akerogarden.se";
                o.AdminNotificationEmail = "admin@test.se";
                o.ContactEmail = "info@test.se";
            });
        });
    }

    private static async Task<Guid> SeedAsync(
        IServiceProvider provider,
        BookingStatus status,
        DateOnly? deleteAfter,
        DateTime? warningSentAt = null,
        bool withParticipant = false)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingType = BookingType.StayRequest,
            Status = status,
            ContactName = "Gäst",
            ContactEmail = $"g-{Guid.NewGuid():N}@test.se",
            CheckIn = new DateOnly(2026, 1, 1),
            CheckOut = new DateOnly(2026, 1, 4),
            DeleteAfter = deleteAfter,
            DeletionWarningEmailSentAt = warningSentAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
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

    private async Task<RetentionRunSummary> RunAsync(IServiceProvider provider, DateOnly today)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RetentionJob>().RunAsync(today);
    }

    private async Task<bool> ExistsAsync(IServiceProvider provider, Guid id)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Bookings.AnyAsync(b => b.Id == id);
    }

    [Fact]
    public async Task Purges_OldNonBookingAuditEntries_KeepsRecentAndBookingScoped_FR98()
    {
        var provider = NewProvider(out _);
        var today = new DateOnly(2026, 7, 5);
        var cutoff = RetentionSchedule.AuditPurgeCutoffUtc(today); // 2024-07-05 UTC (2 years back)

        // A live booking (null DeleteAfter → never deleted) to anchor a booking-scoped audit row.
        var liveBookingId = await SeedAsync(provider, BookingStatus.Approved, deleteAfter: null);

        var oldNonBooking = Guid.NewGuid();
        var recentNonBooking = Guid.NewGuid();
        var oldBookingScoped = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AuditLogs.AddRange(
                new AuditLog
                {
                    Id = oldNonBooking,
                    Timestamp = cutoff.AddDays(-1),
                    ActorType = AuditActorType.Admin,
                    Action = AuditAction.ReferenceDataUpdated,
                    EntityType = "PriceRule",
                    EntityId = Guid.NewGuid(),
                    Summary = "gammal referensdata",
                },
                new AuditLog
                {
                    Id = recentNonBooking,
                    Timestamp = cutoff.AddDays(1),
                    ActorType = AuditActorType.Admin,
                    Action = AuditAction.ReferenceDataUpdated,
                    EntityType = "PriceRule",
                    EntityId = Guid.NewGuid(),
                    Summary = "ny referensdata",
                },
                new AuditLog
                {
                    Id = oldBookingScoped,
                    Timestamp = cutoff.AddDays(-1),
                    ActorType = AuditActorType.Admin,
                    Action = AuditAction.BookingApproved,
                    EntityType = "Booking",
                    EntityId = liveBookingId,
                    BookingId = liveBookingId,
                    Summary = "bokningsåtgärd",
                });
            await db.SaveChangesAsync();
        }

        var summary = await RunAsync(provider, today);

        Assert.True(summary.AuditPurged >= 1);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False(await db.AuditLogs.AnyAsync(a => a.Id == oldNonBooking));   // purged (FR-98)
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Id == recentNonBooking)); // within retention window
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Id == oldBookingScoped)); // booking-scoped: exempt from age purge
        }
    }

    [Fact]
    public async Task Deletes_RecordsPastDeleteAfter_CascadingParticipants()
    {
        var provider = NewProvider(out _);
        var today = new DateOnly(2026, 7, 5);
        var due = await SeedAsync(provider, BookingStatus.Completed, deleteAfter: today.AddDays(-1), withParticipant: true);
        var future = await SeedAsync(provider, BookingStatus.Completed, deleteAfter: today.AddDays(400));

        var summary = await RunAsync(provider, today);

        Assert.Equal(1, summary.Deleted);
        Assert.False(await ExistsAsync(provider, due));
        Assert.True(await ExistsAsync(provider, future));

        // Participant cascade-deleted with its booking.
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Participants.AnyAsync(p => p.BookingId == due));
    }

    [Fact]
    public async Task Deletes_OnExactDeleteAfterDate()
    {
        var provider = NewProvider(out _);
        var today = new DateOnly(2026, 7, 5);
        var id = await SeedAsync(provider, BookingStatus.Cancelled, deleteAfter: today);

        Assert.Equal(1, (await RunAsync(provider, today)).Deleted);
        Assert.False(await ExistsAsync(provider, id));
    }

    [Theory]
    [InlineData(BookingStatus.Pending)]   // FR-89
    [InlineData(BookingStatus.Closed)]    // FR-90
    [InlineData(BookingStatus.Denied)]    // FR-68
    public async Task Deletes_AnyTerminalOrPendingRecord_PastDeleteAfter(BookingStatus status)
    {
        var provider = NewProvider(out _);
        var today = new DateOnly(2026, 7, 5);
        var id = await SeedAsync(provider, status, deleteAfter: today.AddDays(-1));

        Assert.Equal(1, (await RunAsync(provider, today)).Deleted);
        Assert.False(await ExistsAsync(provider, id));
    }

    [Fact]
    public async Task LiveInProgressBooking_WithNullDeleteAfter_IsNeverTouched()
    {
        var provider = NewProvider(out var email);
        var today = new DateOnly(2026, 7, 5);
        var id = await SeedAsync(provider, BookingStatus.Active, deleteAfter: null);

        var summary = await RunAsync(provider, today);

        Assert.Equal(0, summary.Deleted);
        Assert.Equal(0, summary.Warned);
        Assert.True(await ExistsAsync(provider, id));
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task Warns_Once_WithinThirtyDayWindow_AndSetsSentMarker()
    {
        var provider = NewProvider(out var email);
        var today = new DateOnly(2026, 7, 5);
        var id = await SeedAsync(provider, BookingStatus.Completed, deleteAfter: today.AddDays(20));

        var first = await RunAsync(provider, today);
        Assert.Equal(1, first.Warned);
        Assert.Single(email.Sent);
        Assert.Contains(email.Sent, e => e.ToAddress == "admin@test.se" && e.Subject.Contains("gallring", StringComparison.OrdinalIgnoreCase));

        // Marker persisted.
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.NotNull((await db.Bookings.SingleAsync(b => b.Id == id)).DeletionWarningEmailSentAt);
        }

        // Second run does not re-warn (dedupe).
        var second = await RunAsync(provider, today);
        Assert.Equal(0, second.Warned);
        Assert.Single(email.Sent); // still just the one
    }

    /// <summary>
    /// A pre-deletion warning that could not be sent must NOT be recorded as sent (post-launch security
    /// review, finding #11). Sending is non-throwing by design (NFR-13), so before this the job stamped
    /// <c>DeletionWarningEmailSentAt</c> regardless of outcome: an SMTP outage on the one night a
    /// booking entered the window meant the FR-66 warning was marked delivered, never retried, and the
    /// record was hard-deleted 30 days later with nobody told.
    ///
    /// <para>Asserts the whole claim rather than just "the marker is null": the run must also report 0
    /// warned, and — the part that matters to the admin — a later run with mail working must actually
    /// deliver the warning it previously failed to send.</para>
    /// </summary>
    [Fact]
    public async Task FailedWarningEmail_LeavesNoSentMarker_AndIsRetriedOnTheNextRun()
    {
        var provider = NewProvider(out var email);
        var today = new DateOnly(2026, 7, 5);
        var id = await SeedAsync(provider, BookingStatus.Completed, deleteAfter: today.AddDays(20));

        // The mail server is down for this run.
        email.Delivers = false;

        var failed = await RunAsync(provider, today);

        Assert.Equal(0, failed.Warned);
        Assert.Empty(email.Sent);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Null((await db.Bookings.SingleAsync(b => b.Id == id)).DeletionWarningEmailSentAt);
        }

        // Mail is restored; the booking is still inside the window and must now actually be warned.
        email.Delivers = true;

        var recovered = await RunAsync(provider, today);

        Assert.Equal(1, recovered.Warned);
        Assert.Single(email.Sent);
        Assert.Contains(email.Sent, e =>
            e.ToAddress == "admin@test.se"
            && e.Subject.Contains("gallring", StringComparison.OrdinalIgnoreCase));
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.NotNull((await db.Bookings.SingleAsync(b => b.Id == id)).DeletionWarningEmailSentAt);
        }
    }

    [Fact]
    public async Task DoesNotWarn_OutsideWindow_OrWhenAlreadyWarned()
    {
        var provider = NewProvider(out var email);
        var today = new DateOnly(2026, 7, 5);
        await SeedAsync(provider, BookingStatus.Completed, deleteAfter: today.AddDays(31));                 // too far out
        await SeedAsync(provider, BookingStatus.Completed, deleteAfter: today.AddDays(10), warningSentAt: DateTime.UtcNow); // already warned

        var summary = await RunAsync(provider, today);

        Assert.Equal(0, summary.Warned);
        Assert.Empty(email.Sent);
    }

    // ── Job history (FR-114) ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_deleted_booking_leaves_a_deletion_entry_that_survives_its_own_cascade()
    {
        // Written without BookingId on purpose: booking-scoped entries are cascade-deleted with the
        // booking (FR-98), which would erase the very record that it was deleted.
        var provider = NewProvider(out _);
        var today = new DateOnly(2026, 7, 5);
        var id = await SeedAsync(provider, BookingStatus.Denied, deleteAfter: today.AddDays(-1));

        await RunAsync(provider, today);

        Assert.False(await ExistsAsync(provider, id));
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entry = await db.AuditLogs.SingleAsync(a => a.EntityId == id && a.Action == AuditAction.BookingDeletedByRetention);
        Assert.Null(entry.BookingId);
        Assert.False(string.IsNullOrEmpty(entry.BookingReferenceSnapshot));
        Assert.Equal(AuditActorType.System, entry.ActorType);
    }

    [Fact]
    public async Task A_sent_deletion_warning_is_recorded_and_a_failed_one_is_recorded_as_failed()
    {
        var provider = NewProvider(out var email);
        var today = new DateOnly(2026, 7, 5);
        var warned = await SeedAsync(provider, BookingStatus.Denied, deleteAfter: today.AddDays(10));

        await RunAsync(provider, today);

        email.Delivers = false;
        var notWarned = await SeedAsync(provider, BookingStatus.Denied, deleteAfter: today.AddDays(11));
        await RunAsync(provider, today);

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Single(db.AuditLogs.Where(a => a.BookingId == warned), a => a.Action == AuditAction.DeletionWarningSent);
        Assert.Single(db.AuditLogs.Where(a => a.BookingId == notWarned), a => a.Action == AuditAction.EmailSendFailed);
    }

    [Fact]
    public async Task Job_runs_older_than_six_months_are_deleted_but_their_audit_entries_stay()
    {
        var provider = NewProvider(out _);
        var today = new DateOnly(2026, 7, 5);
        var cutoff = RetentionSchedule.JobRunPurgeCutoffUtc(today);
        var oldRun = Guid.NewGuid();
        var recentRun = Guid.NewGuid();
        var entryOfOldRun = Guid.NewGuid();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.JobRuns.AddRange(
                new JobRun { Id = oldRun, JobName = "GDPR-gallring", StartedAt = cutoff.AddDays(-1), Outcome = JobRunOutcome.Succeeded },
                new JobRun { Id = recentRun, JobName = "GDPR-gallring", StartedAt = cutoff.AddDays(1), Outcome = JobRunOutcome.Succeeded });
            db.AuditLogs.Add(new AuditLog
            {
                Id = entryOfOldRun,
                Timestamp = DateTime.UtcNow,
                ActorType = AuditActorType.System,
                Action = AuditAction.BookingDeletedByRetention,
                EntityType = "Booking",
                EntityId = Guid.NewGuid(),
                BookingReferenceSnapshot = "AG-2026-0001",
                Summary = "Bokningen raderades automatiskt enligt gallringsreglerna.",
                JobRunId = oldRun,
            });
            await db.SaveChangesAsync();
        }

        var summary = await RunAsync(provider, today);

        using var check = provider.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await checkDb.JobRuns.AnyAsync(r => r.Id == oldRun));
        Assert.True(await checkDb.JobRuns.AnyAsync(r => r.Id == recentRun));
        Assert.True(summary.JobRunsPurged >= 1);
        // ON DELETE SET NULL: the trail keeps the entry, under its own retention (FR-98).
        var entry = await checkDb.AuditLogs.SingleAsync(a => a.Id == entryOfOldRun);
        Assert.Null(entry.JobRunId);
    }
}
