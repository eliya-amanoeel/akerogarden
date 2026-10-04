using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Issues the deferred completion magic link (requirements.md §2.6, FR-108). On each daily run it
/// finds every booking still <c>Approved</c> whose check-in has come within
/// <see cref="CompletionTokenPolicy.IssueLeadDays"/> and that holds <b>no usable link</b> — no token
/// at all, or one whose expiry has already passed — issues one, and emails the guest that the form
/// is now open.
///
/// <para><b>Why this job exists at all.</b> The link used to be issued at approval. A group booking
/// six months or a year ahead cannot name its participants or choose its meals yet, so that link sat
/// unused and expired against its 30-day ceiling — leaving the guest holding a dead URL with no
/// further contact until the 14-day nudge. Issuing the link when it is usable removes the condition
/// rather than warning about it, which is why the planned link-expiry warning email was retired
/// before it was built (FR-99).</para>
///
/// <para><b>"No usable link", not "never sent".</b> The obvious trigger — a null
/// <c>CompletionLinkSentAt</c> — reopens the very gap this job closes. An admin may send the link
/// early on request (FR-49); on a booking six months out that link dies at its 30-day ceiling, and
/// keying on "never sent" would then treat the booking as already served and never replace it, so
/// the guest holds a dead URL for months. Keying on the live expiry instead makes the job restore
/// the §2.6 invariant — no link ever expires while the guest still needs it — from whatever caused
/// it to break. A booking holding a valid link is skipped, so a live URL is never invalidated
/// underneath a guest who is using it.</para>
///
/// <para>Separate from <see cref="ReminderJob"/> on purpose: a different distance from check-in, a
/// different query, and a different email. "Today" is evaluated in Europe/Stockholm.</para>
/// </summary>
public class CompletionLinkIssuanceJob(
    ApplicationDbContext db,
    BookingService bookings,
    BookingEmailService emails,
    IAuditWriter audit,
    TimeProvider clock) : IScheduledJob
{
    public string Name => "Utfärdande av slutförandelänk";

    public Task RunAsync(CancellationToken cancellationToken) =>
        RunAsync(StockholmTime.Today(clock), cancellationToken);

    /// <summary>Runs the issuance sweep for an explicit <paramref name="today"/> (the test seam).</summary>
    public async Task<int> RunAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        // The date range below is the issue rule itself, expressed as SQL: "check-in at most
        // IssueLeadDays away" is exactly CompletionTokenPolicy.ShouldIssueOn, off the same constant.
        // It is not a coarse pre-filter with a second check in the loop — that second check could
        // never be false, and dead code that looks like a guard invites someone to trust it.
        var windowEnd = today.AddDays(CompletionTokenPolicy.IssueLeadDays);
        var now = clock.GetUtcNow().UtcDateTime;

        var due = await db.Bookings
            .Where(b => b.Status == BookingStatus.Approved
                        // No usable link: never issued, or issued and already dead. See the class
                        // note — "never sent" would strand a booking whose early link has expired.
                        && (b.CompletionTokenExpiry == null || b.CompletionTokenExpiry <= now)
                        && b.CheckIn != null
                        && b.CheckIn >= today
                        && b.CheckIn <= windowEnd)
            .ToListAsync(cancellationToken);

        var issued = 0;

        foreach (var booking in due)
        {
            bookings.IssueCompletionLink(booking, now);
            // System actor (FR-95): the automatic issue involved no admin.
            await audit.RecordAsync(AuditAction.CompletionLinkIssued, nameof(Booking), booking.Id,
                "Slutförandelänk utfärdades automatiskt när incheckningen kom inom 40 dagar.",
                booking.Id, booking.BookingReference, AuditActor.System);
            if (!await emails.SendCompletionLinkAvailableAsync(booking))
            {
                // Sending never throws (NFR-13); recorded so the job history shows the run as failed
                // instead of reporting a link the guest never received (FR-114).
                await audit.RecordAsync(AuditAction.EmailSendFailed, nameof(Booking), booking.Id,
                    "Mejlet med slutförandelänken kunde inte skickas.",
                    booking.Id, booking.BookingReference, AuditActor.System);
            }
            issued++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return issued;
    }
}
