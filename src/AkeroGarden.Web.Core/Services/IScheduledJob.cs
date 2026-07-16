namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// A unit of daily background work run by <see cref="DailyJobRunner"/> (Phase 5). Implementations
/// are scoped services resolved fresh inside the runner's per-tick DI scope. Each run must be
/// idempotent — the runner may fire it again after a restart or a missed day — and self-contained:
/// a job that throws is logged and never aborts the others (NFR-13 style).
/// </summary>
public interface IScheduledJob
{
    /// <summary>Human-readable Swedish name for logging.</summary>
    string Name { get; }

    /// <summary>Runs the job once for the current day.</summary>
    Task RunAsync(CancellationToken cancellationToken);
}
