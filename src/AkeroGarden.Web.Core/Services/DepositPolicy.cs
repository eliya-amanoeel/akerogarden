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

    /// <summary>The deposit as a share of the estimated price, in percent — the client's 20 % (D2).
    /// One constant, so the calculation, the guest's email and the admin's hint cannot disagree.</summary>
    public const int Percent = 20;

    /// <summary>Upper bound on an entered price — a sanity check against a mistyped extra digit, far
    /// above any real booking (the whole property for a week is well under a million).</summary>
    public const decimal MaxPrice = 5_000_000m;

    /// <summary>
    /// The deposit for an estimated price (FR-115, §5.6): <see cref="Percent"/> of it, rounded to whole
    /// kronor (half away from zero). Always computed, never stored, so it cannot drift from the price.
    /// </summary>
    public static decimal DepositFor(decimal estimatedPrice) =>
        Math.Round(estimatedPrice * Percent / 100m, 0, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Why an entered estimated price is refused, or null when it is valid (FR-38): required, whole
    /// kronor, above zero and at most <see cref="MaxPrice"/>. Checked by the page before the dialog
    /// opens and by the service on every call — the service is the rule, the page a convenience.
    /// </summary>
    public static string? ValidatePrice(decimal? estimatedPrice) => estimatedPrice switch
    {
        null => "Ange bokningens beräknade pris.",
        <= 0m => "Det beräknade priset måste vara större än noll.",
        > MaxPrice => "Det beräknade priset verkar orimligt högt. Kontrollera beloppet.",
        { } p when p != decimal.Truncate(p) => "Ange det beräknade priset i hela kronor.",
        _ => null,
    };

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
