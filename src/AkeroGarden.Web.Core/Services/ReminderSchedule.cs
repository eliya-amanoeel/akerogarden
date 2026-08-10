using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Pure decisions for the two 14-days-before-check-in emails (requirements.md §9, FR-63/64),
/// unit-testable without a database or clock (NFR-26).
///
/// <para>They are <b>two independent emails with two independent flags</b>, not two variants of one:
/// the <b>pre-arrival reminder</b> goes to a booking whose details are in (<c>DetailsComplete</c> or
/// <c>Active</c>), and the <b>completion nudge</b> goes to one still <c>Approved</c>. A booking can
/// legitimately receive both — nudged at 14 days, submits at 12, then reminded at 10 — and while a
/// single flag guarded both, that guest silently never received the pre-arrival email with the
/// address and times at all. Keep the two questions separate.</para>
/// </summary>
public static class ReminderSchedule
{
    /// <summary>Days before check-in that both 14-day emails fire (FR-63).</summary>
    public const int LeadDays = 14;

    /// <summary>
    /// How long after a link is sent the completion nudge is withheld (FR-63). Without it a booking
    /// approved inside the 14-day window is nudged on the very next nightly run — the guest reads
    /// "your booking is approved" in the afternoon and "you have not filled in your details" at 03:00
    /// the next morning. The nudge is <b>deferred, not skipped</b>: nothing is marked, so a later run
    /// sends it once the grace has passed.
    /// </summary>
    public static readonly TimeSpan NudgeGrace = TimeSpan.FromDays(3);

    /// <summary>Statuses for which a 14-day email is meaningful at all: the guest has been approved
    /// and the stay has not yet ended. Terminal and pre-approval states are excluded.</summary>
    public static bool IsReminderEligible(BookingStatus status) =>
        status is BookingStatus.Approved or BookingStatus.DetailsComplete or BookingStatus.Active;

    /// <summary>True when check-in falls inside the 14-day window and is not already past — the
    /// timing precondition both emails share.</summary>
    public static bool IsInLeadWindow(DateOnly? checkIn, DateOnly today) =>
        checkIn is { } c && c >= today && c <= today.AddDays(LeadDays);

    /// <summary>
    /// True when the <b>pre-arrival reminder</b> (FR-64) is due: the guest's details are in, check-in
    /// is inside the window, and it has not been sent. Guarded by <c>ReminderEmailSentAt</c> alone —
    /// whether the guest was nudged first is irrelevant, which is the point.
    /// </summary>
    public static bool IsPreArrivalReminderDue(
        BookingStatus status, DateOnly? checkIn, DateTime? reminderSentAt, DateOnly today) =>
        reminderSentAt is null
        && status is BookingStatus.DetailsComplete or BookingStatus.Active
        && IsInLeadWindow(checkIn, today);

    /// <summary>
    /// True when the <b>completion nudge</b> (FR-63) is due: still <c>Approved</c>, check-in inside
    /// the window, not already nudged, and the link was sent more than <see cref="NudgeGrace"/> ago.
    ///
    /// <para>A null <paramref name="linkSentAt"/> means no link has ever reached the guest, so there
    /// is nothing they could have ignored and no grace to serve — the nudge (which reissues the link)
    /// is due immediately. Treating null as "just sent" would withhold the link from the one booking
    /// most in need of it.</para>
    /// </summary>
    public static bool IsCompletionNudgeDue(
        BookingStatus status, DateOnly? checkIn, DateTime? nudgeSentAt, DateTime? linkSentAt,
        DateOnly today, DateTime nowUtc) =>
        nudgeSentAt is null
        && status == BookingStatus.Approved
        && IsInLeadWindow(checkIn, today)
        && (linkSentAt is null || nowUtc - linkSentAt.Value > NudgeGrace);
}
