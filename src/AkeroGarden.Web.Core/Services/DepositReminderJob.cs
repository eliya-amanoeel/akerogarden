using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Sends the one deposit reminder (requirements.md FR-116): every booking still
/// <see cref="BookingStatus.AwaitingDeposit"/> whose due date has passed and that has not been
/// reminded gets an email saying the deposit has not arrived. The client's terms (§6) let an unpaid
/// booking be treated as cancelled only <i>after a reminder</i> — this is that reminder.
///
/// <para><b>It never cancels or releases anything.</b> After the reminder the admin decides; the
/// booking shows as overdue on the dashboard. A bank transfer takes a day or two, and releasing the
/// dates while the money is in flight would be worse for the guest than holding them.</para>
///
/// <para>Its own job rather than a branch of <see cref="ReminderJob"/>: a different status, a
/// different trigger (a stored due date, not a distance from check-in), and its own line in the job
/// history (FR-114). "Today" is evaluated in Europe/Stockholm.</para>
///
/// <para><b>A failed send is retried the next night</b> — <see cref="Booking.DepositReminderSentAt"/>
/// is set only on a confirmed send, the rule every nightly email follows (NFR-13).</para>
/// </summary>
public class DepositReminderJob(
    ApplicationDbContext db,
    BookingEmailService emails,
    IAuditWriter audit,
    TimeProvider clock) : IScheduledJob
{
    public string Name => "Påminnelse om handpenning";

    public Task RunAsync(CancellationToken cancellationToken) =>
        RunAsync(StockholmTime.Today(clock), cancellationToken);

    /// <summary>Runs the sweep for an explicit <paramref name="today"/> (the test seam). Returns the
    /// reminders that actually reached the guest; a failed send is not counted.</summary>
    public async Task<int> RunAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        // The rule itself, as SQL — DepositPolicy.IsReminderDue: waiting, past the due date (the due
        // date is still a day to pay on), not yet reminded.
        var due = await db.Bookings
            .Where(b => b.Status == BookingStatus.AwaitingDeposit
                        && b.DepositReminderSentAt == null
                        && b.DepositDueDate != null
                        && b.DepositDueDate < today)
            .ToListAsync(cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        var sent = 0;

        foreach (var booking in due)
        {
            var ok = await emails.SendDepositReminderAsync(booking);
            // Recorded either way for the job history (FR-114); a failure marks the run failed.
            await audit.RecordAsync(
                ok ? AuditAction.DepositReminderSent : AuditAction.EmailSendFailed,
                nameof(Booking), booking.Id,
                ok ? "Påminnelse om handpenning skickad." : "Påminnelsen om handpenning kunde inte skickas.",
                booking.Id, booking.BookingReference, AuditActor.System);
            if (!ok) continue; // left unmarked, so tomorrow's run retries

            booking.DepositReminderSentAt = now;
            sent++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return sent;
    }
}
