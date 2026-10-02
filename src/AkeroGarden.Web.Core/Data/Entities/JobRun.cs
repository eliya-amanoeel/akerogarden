namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// One run of one nightly job (FR-114, §8.17) — the only part of the job history that did not
/// already exist. <b>What the run did is not stored here</b>: it is the <see cref="AuditLog"/>
/// entries written during the run, which carry <see cref="AuditLog.JobRunId"/>. A second per-booking
/// table would have duplicated the audit trail the jobs already write to as the System actor.
///
/// <para><b>No personal data (NFR-37).</b> <see cref="FailureReason"/> is a fixed Swedish text —
/// never the exception message, which can carry an email address. Runs are
/// deleted after six months by the retention job (<see cref="Services.RetentionSchedule"/>); their
/// audit entries stay, under the audit trail's own retention (FR-98).</para>
/// </summary>
public class JobRun
{
    public Guid Id { get; set; }

    /// <summary>The job's Swedish name (<see cref="Services.IScheduledJob.Name"/>) at the time of the run.</summary>
    public string JobName { get; set; } = string.Empty;

    /// <summary>When the run started (UTC).</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>When the run finished (UTC); null only if the process stopped mid-run.</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>Null while running, or if the process stopped mid-run.</summary>
    public JobRunOutcome? Outcome { get; set; }

    /// <summary>Why the run failed, for the admin — Swedish, no personal data. Null on success.</summary>
    public string? FailureReason { get; set; }
}
