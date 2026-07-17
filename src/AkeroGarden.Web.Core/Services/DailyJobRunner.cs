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
/// </summary>
public class DailyJobRunner(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<JobOptions> options,
    ILogger<DailyJobRunner> logger) : BackgroundService
{
    private static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");
    private readonly JobOptions _opts = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opts.Enabled)
        {
            logger.LogInformation("Schemalagda jobb är avstängda (Jobs:Enabled = false).");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextRun();
            logger.LogInformation("Nästa schemalagda jobbkörning om {Delay} (kl. {Hour:00}:00 svensk tid).",
                delay, _opts.DailyRunHourLocal);
            try
            {
                await Task.Delay(delay, stoppingToken);
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
        foreach (var job in scope.ServiceProvider.GetServices<IScheduledJob>())
        {
            try
            {
                logger.LogInformation("Kör schemalagt jobb: {Job}.", job.Name);
                await job.RunAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // One failing job must never stop the others or the loop (NFR-13 style).
                logger.LogError(ex, "Schemalagt jobb misslyckades: {Job}.", job.Name);
            }
        }
    }

    /// <summary>Delay from now until the next daily run instant in Europe/Stockholm.</summary>
    private TimeSpan TimeUntilNextRun()
    {
        var nowLocal = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Stockholm);
        var todayRun = new DateTimeOffset(
            nowLocal.Year, nowLocal.Month, nowLocal.Day, _opts.DailyRunHourLocal, 0, 0, nowLocal.Offset);
        var next = nowLocal < todayRun ? todayRun : todayRun.AddDays(1);
        return next - nowLocal;
    }
}
