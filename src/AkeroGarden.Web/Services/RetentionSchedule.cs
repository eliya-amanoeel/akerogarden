namespace AkeroGarden.Web.Services;

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
    /// <see cref="AuditRetentionYears"/> before <paramref name="today"/>, as UTC midnight — the
    /// two-year civil boundary needs no sub-day precision.</summary>
    public static DateTime AuditPurgeCutoffUtc(DateOnly today) =>
        today.AddYears(-AuditRetentionYears).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

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
