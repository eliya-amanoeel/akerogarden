namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Pure decisions for the GDPR retention job (requirements.md §2.7, FR-66/69/91), unit-testable
/// without a database or clock (NFR-26). A booking's <see cref="RetentionCalculator"/>-computed
/// <c>DeleteAfter</c> date drives both the 30-day pre-deletion admin warning and the deletion
/// itself; this type encodes exactly when each is due for a given "today".
/// </summary>
public static class RetentionSchedule
{
    /// <summary>Days before <c>DeleteAfter</c> that the admin pre-deletion warning fires (FR-66).</summary>
    public const int WarningLeadDays = 30;

    /// <summary>Years a non-booking audit entry (reference-data / admin-account) is retained before
    /// it is age-purged (FR-98). Booking-scoped audit entries instead follow their booking's
    /// retention via the cascade FK (§8.15).</summary>
    public const int AuditRetentionYears = 2;

    /// <summary>The instant before which non-booking audit entries are purged (FR-98):
    /// <see cref="AuditRetentionYears"/> before <paramref name="today"/>, at Swedish midnight —
    /// the start of that Swedish day, as every other date in the system is.</summary>
    public static DateTime AuditPurgeCutoffUtc(DateOnly today) =>
        StockholmTime.ToUtc(today.AddYears(-AuditRetentionYears), TimeOnly.MinValue);

    /// <summary>Months a nightly job run is kept in the job history before it is deleted (FR-114).
    /// Long enough to look back over a season; the audit entries it points to outlive it (FR-98).</summary>
    public const int JobRunRetentionMonths = 6;

    /// <summary>The instant before which job runs are deleted (FR-114):
    /// <see cref="JobRunRetentionMonths"/> before <paramref name="today"/>, at Swedish midnight —
    /// same shape as <see cref="AuditPurgeCutoffUtc"/>.</summary>
    public static DateTime JobRunPurgeCutoffUtc(DateOnly today) =>
        StockholmTime.ToUtc(today.AddMonths(-JobRunRetentionMonths), TimeOnly.MinValue);

    /// <summary>True if the record is at or past its deletion date and must be deleted (FR-69/91).</summary>
    public static bool IsDeletionDue(DateOnly? deleteAfter, DateOnly today) =>
        deleteAfter is { } d && d <= today;

    /// <summary>
    /// True if the record is inside the 30-day pre-deletion window but not yet due for deletion,
    /// and no warning has been sent yet (FR-66). De-duplication is via
    /// <paramref name="warningSentAt"/> (the persisted <c>DeletionWarningEmailSentAt</c>), so the
    /// warning fires exactly once even across restarts or repeated daily runs.
    /// </summary>
    public static bool IsWarningDue(DateOnly? deleteAfter, DateTime? warningSentAt, DateOnly today) =>
        deleteAfter is { } d
        && warningSentAt is null
        && d > today                            // not already due for deletion this run
        && d <= today.AddDays(WarningLeadDays);  // within the 30-day lead window
}
