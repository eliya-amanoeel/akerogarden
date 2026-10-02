using Microsoft.Extensions.Options;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// The single in-process scheduler for Phase 5's daily automation. Once per day (at
/// <see cref="JobOptions.DailyRunHourLocal"/>, Europe/Stockholm) it opens a fresh DI scope and runs
/// every registered <see cref="IScheduledJob"/> in turn; a job that throws is logged and never
/// aborts the others or the loop. No external scheduler and no new Azure resource — but on App
/// Service B1 the app is unloaded after idle unless <c>alwaysOn</c> is enabled, so Always On is
/// required (set in <c>main.bicep</c>) for the daily tick to fire reliably. B1 is single-instance,
/// so no leader election is needed; idempotent jobs also make an accidental double-run harmless.
/// Each run is recorded for the admin's job history (FR-114) via <see cref="IJobRunRecorder"/>.
/// </summary>
public class DailyJobRunner(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<JobOptions> options,
    ILogger<DailyJobRunner> logger) : BackgroundService
{
    private readonly JobOptions _opts = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opts.Enabled)
        {
            logger.LogInformation("Schemalagda jobb är avstängda (Jobs:Enabled = false).");
            return;
        }

        // Development seam (Jobs:RunOnStartup): one pass now, then the normal schedule. Without it a
        // scheduled email cannot be seen locally without waiting for the next occurrence of the run
        // hour — which is in Stockholm time, so "set it to the next hour" is not even reliable
        // advice. Off by default; never set in production.
        if (_opts.RunOnStartup)
        {
            logger.LogInformation("Jobs:RunOnStartup är på — kör jobben en gång direkt innan schemat tar över.");
            await RunAllJobsAsync(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun();
            logger.LogInformation("Nästa schemalagda jobbkörning om {Delay} (kl. {Hour:00}:00 svensk tid).",
                delay, _opts.DailyRunHourLocal);
            try
            {
                // Wait on the injected clock, not the ambient one, so the schedule is driven by a
                // single time source (and the loop is testable with a fake clock).
                await Task.Delay(delay, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break; // shutdown
            }

            await RunAllJobsAsync(stoppingToken);
        }
    }

    private async Task RunAllJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        // Run records (FR-114) go through a scope of their own: see JobRunRecorder.
        using var recordScope = scopeFactory.CreateScope();
        var recorder = recordScope.ServiceProvider.GetRequiredService<IJobRunRecorder>();
        var runContext = scope.ServiceProvider.GetRequiredService<JobRunContext>();

        foreach (var job in scope.ServiceProvider.GetServices<IScheduledJob>())
        {
            var runId = await TryStartRunAsync(recorder, job.Name, cancellationToken);
            runContext.CurrentRunId = runId;
            Exception? failure = null;
            try
            {
                logger.LogInformation("Kör schemalagt jobb: {Job}.", job.Name);
                await job.RunAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // One failing job must never stop the others or the loop (NFR-13 style).
                logger.LogError(ex, "Schemalagt jobb misslyckades: {Job}.", job.Name);
                failure = ex;
            }
            finally
            {
                runContext.CurrentRunId = null;
            }

            if (runId is { } id)
                await TryFinishRunAsync(recorder, id, failure, job.Name, cancellationToken);
        }
    }

    /// <summary>
    /// The history is a record of the jobs, never a condition for them: if writing it fails, the job
    /// still runs and the failure is logged. Without this, a database hiccup on the history table
    /// would escape the loop and stop every nightly job — GDPR deletion included.
    /// </summary>
    private async Task<Guid?> TryStartRunAsync(IJobRunRecorder recorder, string jobName, CancellationToken cancellationToken)
    {
        try
        {
            return await recorder.StartAsync(jobName, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Jobbhistoriken kunde inte skrivas för {Job}.", jobName);
            return null;
        }
    }

    private async Task TryFinishRunAsync(
        IJobRunRecorder recorder, Guid runId, Exception? failure, string jobName, CancellationToken cancellationToken)
    {
        try
        {
            await recorder.FinishAsync(runId, failure, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Jobbhistoriken kunde inte skrivas för {Job}.", jobName);
        }
    }

    /// <summary>
    /// Delay from now until the next daily run instant. The scheduling arithmetic — including both
    /// DST transitions — lives in <see cref="DailyRunSchedule"/> so it is testable as a pure
    /// function rather than inferred from whether a timer happened to fire.
    /// </summary>
    private TimeSpan TimeUntilNextRun() =>
        DailyRunSchedule.TimeUntilNextRun(clock.GetUtcNow(), _opts.DailyRunHourLocal);
}
