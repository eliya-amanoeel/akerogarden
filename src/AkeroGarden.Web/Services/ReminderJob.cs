using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Services;

/// <summary>
/// The 14-day reminder job (requirements.md §9, FR-63/64). On each daily run it finds every
/// reminder-eligible booking whose check-in is within the next 14 days and that has not yet been
/// reminded (<c>ReminderEmailSentAt</c> guards a once-only send), then, per status:
/// <list type="bullet">
///   <item><c>DetailsComplete</c>/<c>Active</c> — sends the standard guest reminder (FR-64).</item>
///   <item>still <c>Approved</c> — regenerates the completion token (fresh link ≥48 h, invalidating
///   the prior one), sends the guest a completion nudge, and alerts the admin (FR-63).</item>
/// </list>
/// "Today" is evaluated in Europe/Stockholm.
/// </summary>
public class ReminderJob(
    ApplicationDbContext db,
    BookingService bookings,
    BookingEmailService emails,
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
            .Include(b => b.Participants).ThenInclude(p => p.MealPreferences)
            .Where(b => b.ReminderEmailSentAt == null
                        && b.CheckIn != null
                        && b.CheckIn >= today
                        && b.CheckIn <= windowEnd
                        && (b.Status == BookingStatus.Approved
                            || b.Status == BookingStatus.DetailsComplete
                            || b.Status == BookingStatus.Active))
            .ToListAsync(cancellationToken);

        var now = clock.GetUtcNow().UtcDateTime;
        int standard = 0, nudged = 0;

        foreach (var booking in due)
        {
            if (ReminderSchedule.IsNudgeVariant(booking.Status))
            {
                // Regenerate the link first so the nudge never carries an already-expired token (I13).
                bookings.RegenerateCompletionToken(booking);
                await emails.SendCompletionNudgeAsync(booking);
                await emails.SendAdminGuestNotCompletedAsync(booking);
                nudged++;
            }
            else
            {
                await emails.SendTwoWeekReminderAsync(booking);
                standard++;
            }
            booking.ReminderEmailSentAt = now; // dedupe marker — reminder fires once
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ReminderRunSummary(standard, nudged);
    }
}

/// <summary>Outcome of one <see cref="ReminderJob"/> run — standard-reminder and nudge counts.</summary>
public record ReminderRunSummary(int StandardReminders, int Nudges);
