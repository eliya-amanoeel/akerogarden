using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// The GDPR retention job (requirements.md §2.7, FR-66/67/68/69/89/90/91). On each daily run it
/// (1) hard-deletes every booking at or past its <c>DeleteAfter</c> date — cascading to
/// participants, meal preferences, and facility/cleaning selections (configured in
/// <see cref="ApplicationDbContext"/>) — and (2) sends the admin a 30-day pre-deletion warning for
/// records entering the warning window, de-duplicated via <c>DeletionWarningEmailSentAt</c> so each
/// warning fires exactly once, (3) age-purges non-booking audit entries older than
/// <see cref="RetentionSchedule.AuditRetentionYears"/> (FR-98), and (4) deletes job runs older than
/// <see cref="RetentionSchedule.JobRunRetentionMonths"/> (FR-114). "Today" is evaluated in
/// Europe/Stockholm. The <c>DeleteAfter</c> values are set on the retention-relevant transitions
/// (Pending/Denied/Cancelled/Closed/Completed) via <see cref="RetentionCalculator"/>; live
/// in-progress states have a null date and are never touched (FR-91).
/// </summary>
public class RetentionJob(
    ApplicationDbContext db,
    BookingEmailService emails,
    IAuditWriter audit,
    TimeProvider clock) : IScheduledJob
{
    public string Name => "GDPR-gallring";

    public Task RunAsync(CancellationToken cancellationToken) =>
        RunAsync(StockholmTime.Today(clock), cancellationToken);

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
        foreach (var booking in due)
        {
            // Deliberately not booking-scoped (no BookingId): a booking-scoped entry would be
            // cascade-deleted with the booking in this very SaveChanges (FR-98). By reference only, it
            // stays in the job history (FR-114) under the two-year non-booking retention.
            await audit.RecordAsync(AuditAction.BookingDeletedByRetention, nameof(Booking), booking.Id,
                "Bokningen raderades automatiskt enligt gallringsreglerna.",
                bookingId: null, bookingReference: booking.BookingReference, actor: AuditActor.System);
        }
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
        var warned = 0;
        foreach (var booking in toWarn)
        {
            // The marker is written only on a confirmed send (post-launch security review, finding
            // #11). Sending is non-throwing by design (NFR-13), so this loop previously stamped
            // DeletionWarningEmailSentAt whether or not the mail left the building — a silent SMTP
            // failure recorded the FR-66 warning as delivered, and the booking was hard-deleted 30 days
            // later with nobody having been told. Leaving the marker null on failure means the next
            // nightly run retries, which is the right behaviour for a warning that exists to give the
            // admin a chance to intervene before data is destroyed.
            if (!await emails.SendPreDeletionWarningAsync(booking))
            {
                // Retried next night (marker left null); recorded so this run shows as failed (FR-114).
                await audit.RecordAsync(AuditAction.EmailSendFailed, nameof(Booking), booking.Id,
                    "Varningen om kommande radering kunde inte skickas till administratören.",
                    booking.Id, booking.BookingReference, AuditActor.System);
                continue;
            }

            await audit.RecordAsync(AuditAction.DeletionWarningSent, nameof(Booking), booking.Id,
                "Varning om radering om 30 dagar skickad till administratören.",
                booking.Id, booking.BookingReference, AuditActor.System);
            booking.DeletionWarningEmailSentAt = now; // dedupe marker — warning fires once
            warned++;
        }
        await db.SaveChangesAsync(cancellationToken);

        // 3. Audit-log retention (FR-98): non-booking entries (reference-data / admin-account) are
        //    age-purged 2 years after they were written. Booking-scoped entries carry no such age
        //    limit here — they were already cascade-deleted with their booking in step 1 (§8.15).
        var auditCutoff = RetentionSchedule.AuditPurgeCutoffUtc(today);
        var auditPurged = await db.AuditLogs
            .Where(a => a.BookingId == null && a.Timestamp < auditCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        // 4. Job-history retention (FR-114): runs older than six months. Their audit entries are
        //    not deleted — the FK is ON DELETE SET NULL, and the trail keeps its own retention (FR-98).
        var jobRunCutoff = RetentionSchedule.JobRunPurgeCutoffUtc(today);
        var jobRunsPurged = await db.JobRuns
            .Where(r => r.StartedAt < jobRunCutoff)
            .ExecuteDeleteAsync(cancellationToken);

        // `warned` counts confirmed sends, not candidates — a run where the mail server was down now
        // reports 0 warned rather than overstating what the guests' admin was actually told.
        return new RetentionRunSummary(due.Count, warned, auditPurged, jobRunsPurged);
    }
}

/// <summary>Outcome of one <see cref="RetentionJob"/> run — deleted-record, warning, age-purged
/// non-booking audit-entry (FR-98) and job-run (FR-114) counts.</summary>
public record RetentionRunSummary(int Deleted, int Warned, int AuditPurged = 0, int JobRunsPurged = 0);
