using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the pure retention-job decisions (§2.7, FR-66/69/91): when a record is due
/// for deletion and when the 30-day pre-deletion warning fires, de-duplicated by the sent marker.
/// </summary>
public class RetentionScheduleTests
{
    private static readonly DateOnly Today = new(2026, 7, 5);

    [Fact]
    public void IsDeletionDue_OnOrAfterDate_IsTrue()
    {
        Assert.True(RetentionSchedule.IsDeletionDue(Today, Today));               // exactly due
        Assert.True(RetentionSchedule.IsDeletionDue(Today.AddDays(-1), Today));   // overdue
    }

    [Fact]
    public void IsDeletionDue_BeforeDate_OrNull_IsFalse()
    {
        Assert.False(RetentionSchedule.IsDeletionDue(Today.AddDays(1), Today));
        Assert.False(RetentionSchedule.IsDeletionDue(null, Today));
    }

    [Fact]
    public void IsWarningDue_WithinWindow_NotSent_IsTrue()
    {
        Assert.True(RetentionSchedule.IsWarningDue(Today.AddDays(30), null, Today));  // edge of window
        Assert.True(RetentionSchedule.IsWarningDue(Today.AddDays(15), null, Today));
        Assert.True(RetentionSchedule.IsWarningDue(Today.AddDays(1), null, Today));
    }

    [Fact]
    public void IsWarningDue_OutsideWindow_IsFalse()
    {
        Assert.False(RetentionSchedule.IsWarningDue(Today.AddDays(31), null, Today)); // too far out
    }

    [Fact]
    public void IsWarningDue_AtOrPastDeletionDate_IsFalse()
    {
        // A record already due for deletion is deleted, not warned.
        Assert.False(RetentionSchedule.IsWarningDue(Today, null, Today));
        Assert.False(RetentionSchedule.IsWarningDue(Today.AddDays(-1), null, Today));
    }

    [Fact]
    public void IsWarningDue_AlreadySent_IsFalse()
    {
        Assert.False(RetentionSchedule.IsWarningDue(Today.AddDays(10), DateTime.UtcNow, Today));
    }

    [Fact]
    public void IsWarningDue_NullDeleteAfter_IsFalse()
    {
        Assert.False(RetentionSchedule.IsWarningDue(null, null, Today));
    }

    [Fact]
    public void AuditPurgeCutoff_IsTwoYearsBeforeToday_AtSwedishMidnight()
    {
        var cutoff = RetentionSchedule.AuditPurgeCutoffUtc(Today);

        // FR-98: 2 years. Midnight 2024-07-05 in Sweden (summer, UTC+2) is 22:00 UTC the day before.
        Assert.Equal(new DateTime(2024, 7, 4, 22, 0, 0, DateTimeKind.Utc), cutoff);
        Assert.Equal(DateTimeKind.Utc, cutoff.Kind);
    }

    [Fact]
    public void JobRunPurgeCutoff_IsSixMonthsBeforeToday_AtSwedishMidnight()
    {
        var cutoff = RetentionSchedule.JobRunPurgeCutoffUtc(Today);

        // FR-114: 6 months. Midnight 2026-01-05 in Sweden (winter, UTC+1) is 23:00 UTC the day before.
        Assert.Equal(new DateTime(2026, 1, 4, 23, 0, 0, DateTimeKind.Utc), cutoff);
        Assert.Equal(DateTimeKind.Utc, cutoff.Kind);
    }
}
