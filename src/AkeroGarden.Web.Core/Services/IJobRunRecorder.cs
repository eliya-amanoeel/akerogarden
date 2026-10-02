namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Writes the <see cref="Data.Entities.JobRun"/> row for each nightly job run (FR-114): opened before
/// the job, closed after it with the outcome. What the run did is not written here — it is the audit
/// entries the job writes meanwhile, tagged through <see cref="JobRunContext"/>.
/// </summary>
public interface IJobRunRecorder
{
    /// <summary>Records that <paramref name="jobName"/> has started; returns the run's id.</summary>
    Task<Guid> StartAsync(string jobName, CancellationToken cancellationToken);

    /// <summary>
    /// Closes the run. It is <c>Misslyckades</c> when <paramref name="failure"/> is set, or when the
    /// job recorded any <see cref="Data.Entities.AuditAction.EmailSendFailed"/> entry during it.
    /// </summary>
    Task FinishAsync(Guid runId, Exception? failure, CancellationToken cancellationToken);
}
