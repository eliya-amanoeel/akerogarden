using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Pure rules for a booking waiting for its deposit (requirements.md FR-115/116), unit-testable
/// without a database or clock (NFR-26). Callers supply "today" as a Swedish date.
/// </summary>
public static class DepositPolicy
{
    /// <summary>Days the group has to pay, from the day the deposit is asked for — the client's
    /// answer of 2026-10-08.</summary>
    public const int DaysToPay = 7;

    /// <summary>
    /// The day the deposit is due: <see cref="DaysToPay"/> days from <paramref name="today"/>, but
    /// never later than the day before check-in — a due date after arrival would be meaningless — and
    /// never before today, for a booking recorded at the last minute or after the fact (FR-112).
    /// </summary>
    public static DateOnly DueDate(DateOnly today, DateOnly checkIn)
    {
        var due = today.AddDays(DaysToPay);
        var dayBeforeArrival = checkIn.AddDays(-1);
        if (due > dayBeforeArrival) due = dayBeforeArrival;
        return due < today ? today : due;
    }

    /// <summary>True once the due date has passed — the due date itself is still a day to pay on.
    /// Drives the overdue state on the dashboard (FR-116).</summary>
    public static bool IsOverdue(DateOnly? dueDate, DateOnly today) =>
        dueDate is { } due && due < today;

    /// <summary>
    /// True when the one deposit reminder is due (FR-116): still waiting, past the due date, and not
    /// yet sent. The reminder exists because the client's terms (§6) only let an unpaid booking be
    /// treated as cancelled <i>after a reminder</i>; it never cancels anything itself.
    /// </summary>
    public static bool IsReminderDue(
        BookingStatus status, DateOnly? dueDate, DateTime? reminderSentAt, DateOnly today) =>
        status == BookingStatus.AwaitingDeposit
        && reminderSentAt is null
        && IsOverdue(dueDate, today);
}
