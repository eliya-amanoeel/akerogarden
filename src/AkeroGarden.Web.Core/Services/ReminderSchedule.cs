using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Pure decisions for the 14-day reminder job (requirements.md §9, FR-63/64), unit-testable without
/// a database or clock (NFR-26). A reminder is due once a booking's check-in falls inside the
/// 14-day window, the booking is in a reminder-eligible status, and no reminder has been sent yet
/// (de-duplicated via <c>ReminderEmailSentAt</c>). The variant then depends on status: still
/// <c>Approved</c> ⇒ a completion nudge (with a regenerated link) plus an admin alert;
/// <c>DetailsComplete</c>/<c>Active</c> ⇒ the standard reminder (FR-64).
/// </summary>
public static class ReminderSchedule
{
    /// <summary>Days before check-in that the reminder fires (FR-63).</summary>
    public const int LeadDays = 14;

    /// <summary>Statuses for which a 14-day reminder is meaningful: the guest has been approved and
    /// the stay has not yet ended. Terminal and pre-approval states are excluded.</summary>
    public static bool IsReminderEligible(BookingStatus status) =>
        status is BookingStatus.Approved or BookingStatus.DetailsComplete or BookingStatus.Active;

    /// <summary>
    /// True when the reminder should fire now: eligible status, not already sent, and check-in is
    /// within the next <see cref="LeadDays"/> days but not in the past.
    /// </summary>
    public static bool IsReminderDue(
        BookingStatus status, DateOnly? checkIn, DateTime? reminderSentAt, DateOnly today) =>
        reminderSentAt is null
        && IsReminderEligible(status)
        && checkIn is { } c
        && c >= today
        && c <= today.AddDays(LeadDays);

    /// <summary>
    /// True if the due reminder is the still-<c>Approved</c> nudge variant (regenerate link + guest
    /// nudge + admin alert); false means the standard reminder for <c>DetailsComplete</c>+ (FR-63).
    /// </summary>
    public static bool IsNudgeVariant(BookingStatus status) => status == BookingStatus.Approved;
}
