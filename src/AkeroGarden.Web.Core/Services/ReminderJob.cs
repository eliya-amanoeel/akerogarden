using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// The two 14-days-before-check-in emails (requirements.md §9, FR-63/64). On each daily run it
/// asks two <b>independent</b> questions of every booking whose check-in falls inside the window:
/// <list type="bullet">
///   <item><c>DetailsComplete</c>/<c>Active</c> and not yet reminded — send the pre-arrival reminder
///   with the address and the booking's own times, guarded by <c>ReminderEmailSentAt</c>.</item>
///   <item>still <c>Approved</c>, not yet nudged, and the link went out more than three days ago —
///   reissue the link, nudge the guest and alert the admin, guarded by
///   <c>CompletionNudgeSentAt</c>.</item>
/// </list>
///
/// <para><b>Two flags, not one.</b> The two branches are mutually exclusive within a single run — a
/// booking is either still <c>Approved</c> or it is not — so what the split fixes is not one run but
/// two: a booking nudged at 14 days that then submits at 12 must still receive the pre-arrival email
/// at 10. While both emails shared <c>ReminderEmailSentAt</c>, the nudge consumed it on the first run
/// and the second found it already set, so that guest never got the address or the times at all —
/// every booking approved inside the 14-day window lost it. Do not merge the flags back.</para>
///
/// <para>"Today" is evaluated in Europe/Stockholm.</para>
/// </summary>
public class ReminderJob(
    ApplicationDbContext db,
    BookingService bookings,
    BookingEmailService emails,
    IAuditWriter audit,
    TimeProvider clock) : IScheduledJob
{
    public string Name => "14-dagarspåminnelse";

    public Task RunAsync(CancellationToken cancellationToken) =>
        RunAsync(RetentionJob.StockholmToday(clock), cancellationToken);

    /// <summary>Runs the reminder sweep for an explicit <paramref name="today"/> (the test seam).</summary>
    public async Task<ReminderRunSummary> RunAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        var windowEnd = today.AddDays(ReminderSchedule.LeadDays);
        var due = await db.Bookings
            .Include(b => b.Participants)
            .Include(b => b.MealSelections) // the group's chosen dishes, counted in the reminder (FR-64)
            .Include(b => b.BuildingAssignments) // named in the reminder (FR-64)
            .Where(b => b.CheckIn != null
                        && b.CheckIn >= today
                        && b.CheckIn <= windowEnd
                        && (b.ReminderEmailSentAt == null || b.CompletionNudgeSentAt == null)
                        && (b.Status == BookingStatus.Approved
                            || b.Status == BookingStatus.DetailsComplete
                            || b.Status == BookingStatus.Active))
            .ToListAsync(cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        int standard = 0, nudged = 0;

        foreach (var booking in due)
        {
            if (ReminderSchedule.IsPreArrivalReminderDue(
                    booking.Status, booking.CheckIn, booking.ReminderEmailSentAt, today))
            {
                var sent = await emails.SendTwoWeekReminderAsync(booking);
                // Recorded either way for the job history (FR-114); a failure marks the run failed.
                await audit.RecordAsync(
                    sent ? AuditAction.PreArrivalReminderSent : AuditAction.EmailSendFailed,
                    nameof(Booking), booking.Id,
                    sent ? "Påminnelse inför ankomst skickad." : "Påminnelsen inför ankomst kunde inte skickas.",
                    booking.Id, booking.BookingReference, AuditActor.System);
                booking.ReminderEmailSentAt = now;
                standard++;
            }

            if (ReminderSchedule.IsCompletionNudgeDue(
                    booking.Status, booking.CheckIn, booking.CompletionNudgeSentAt,
                    booking.CompletionLinkSentAt, today, now))
            {
                // Reissue first so the nudge never carries an already-expired link (I13). This also
                // sets CompletionLinkSentAt — harmless here, since the nudge is guarded by its own
                // flag and will not re-evaluate its own grace period.
                bookings.IssueCompletionLink(booking, now);
                // System actor (FR-63/95): the automated nudge issued the link — no admin involved.
                await audit.RecordAsync(AuditAction.CompletionLinkIssued, nameof(Booking), booking.Id,
                    "Slutförandelänk utfärdades automatiskt av 14-dagarspåminnelsen.",
                    booking.Id, booking.BookingReference, AuditActor.System);
                // The nudge itself is the CompletionLinkIssued entry above; only failures add a line.
                if (!await emails.SendCompletionNudgeAsync(booking))
                {
                    await audit.RecordAsync(AuditAction.EmailSendFailed, nameof(Booking), booking.Id,
                        "Påminnelsen om att fylla i uppgifterna kunde inte skickas till gästen.",
                        booking.Id, booking.BookingReference, AuditActor.System);
                }
                if (!await emails.SendAdminGuestNotCompletedAsync(booking))
                {
                    await audit.RecordAsync(AuditAction.EmailSendFailed, nameof(Booking), booking.Id,
                        "Meddelandet till administratören om att gästen inte fyllt i kunde inte skickas.",
                        booking.Id, booking.BookingReference, AuditActor.System);
                }
                booking.CompletionNudgeSentAt = now;
                nudged++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ReminderRunSummary(standard, nudged);
    }
}

/// <summary>Outcome of one <see cref="ReminderJob"/> run — pre-arrival-reminder and nudge counts.</summary>
public record ReminderRunSummary(int StandardReminders, int Nudges);
