using AkeroGarden.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Services;

/// <summary>
/// The GDPR retention job (requirements.md §2.7, FR-66/67/68/69/89/90/91). On each daily run it
/// (1) hard-deletes every booking at or past its <c>DeleteAfter</c> date — cascading to
/// participants, meal preferences, and facility/cleaning selections (configured in
/// <see cref="ApplicationDbContext"/>) — and (2) sends the admin a 30-day pre-deletion warning for
/// records entering the warning window, de-duplicated via <c>DeletionWarningEmailSentAt</c> so each
/// warning fires exactly once, and (3) age-purges non-booking audit entries older than
/// <see cref="RetentionSchedule.AuditRetentionYears"/> (FR-98). "Today" is evaluated in
/// Europe/Stockholm. The <c>DeleteAfter</c> values are set on the retention-relevant transitions
/// (Pending/Denied/Cancelled/Closed/Completed) via <see cref="RetentionCalculator"/>; live
/// in-progress states have a null date and are never touched (FR-91).
/// </summary>
public class RetentionJob(ApplicationDbContext db, BookingEmailService emails, TimeProvider clock)
    : IScheduledJob
{
    public string Name => "GDPR-gallring";

    public Task RunAsync(CancellationToken cancellationToken) =>
        RunAsync(StockholmToday(clock), cancellationToken);

    /// <summary>
    /// Runs the retention sweep for an explicit <paramref name="today"/> — the seam the unit/
    /// integration tests drive so behaviour does not depend on the wall clock. Returns a summary
    /// of what happened.
    /// </summary>
    public async Task<RetentionRunSummary> RunAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        // 1. Deletions first (FR-69). A record past its date is deleted this run, so it never also
        //    gets a "will be deleted in 30 days" warning — the warning query below excludes it.
        var due = await db.Bookings
            .Where(b => b.DeleteAfter != null && b.DeleteAfter <= today)
            .ToListAsync(cancellationToken);
        db.Bookings.RemoveRange(due);
        await db.SaveChangesAsync(cancellationToken);

        // 2. Pre-deletion warnings (FR-66): within the 30-day window, not yet due, not yet warned.
        var warnThreshold = today.AddDays(RetentionSchedule.WarningLeadDays);
        var toWarn = await db.Bookings
            .Where(b => b.DeleteAfter != null
                        && b.DeleteAfter > today
                        && b.DeleteAfter <= warnThreshold
                        && b.DeletionWarningEmailSentAt == null)
            .ToListAsync(cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var booking in toWarn)
        {
            await emails.SendPreDeletionWarningAsync(booking);
            booking.DeletionWarningEmailSentAt = now; // dedupe marker — warning fires once
        }
        await db.SaveChangesAsync(cancellationToken);

        // 3. Audit-log retention (FR-98): non-booking entries (reference-data / admin-account) are
        //    age-purged 2 years after they were written. Booking-scoped entries carry no such age
        //    limit here — they were already cascade-deleted with their booking in step 1 (§8.15).
        var auditCutoff = RetentionSchedule.AuditPurgeCutoffUtc(today);
        var auditPurged = await db.AuditLogs
            .Where(a => a.BookingId == null && a.Timestamp < auditCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        return new RetentionRunSummary(due.Count, toWarn.Count, auditPurged);
    }

    /// <summary>The current date in Europe/Stockholm — retention dates are civil dates, not UTC.</summary>
    public static DateOnly StockholmToday(TimeProvider clock)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz).DateTime);
    }
}

/// <summary>Outcome of one <see cref="RetentionJob"/> run — deleted-record, warning, and
/// age-purged non-booking audit-entry counts (FR-98).</summary>
public record RetentionRunSummary(int Deleted, int Warned, int AuditPurged = 0);
