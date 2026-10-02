namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// The nightly job run currently in progress in this DI scope, if any (FR-114). Set by
/// <see cref="DailyJobRunner"/> around each job; read by <see cref="AuditWriter"/>, which tags every
/// audit entry written meanwhile with the run. That is how a run's "what it did" is recorded without
/// any job having to pass the run along — the jobs keep calling <see cref="IAuditWriter"/> exactly
/// as before. Null everywhere outside a job run (admin actions, the public site).
/// </summary>
public sealed class JobRunContext
{
    public Guid? CurrentRunId { get; set; }
}
