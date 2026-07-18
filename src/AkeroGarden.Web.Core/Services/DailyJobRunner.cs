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
        var nowUtc = clock.GetUtcNow();
        var todayLocal = TimeZoneInfo.ConvertTime(nowUtc, Stockholm).Date;

        var next = RunInstantOn(todayLocal);
        if (next <= nowUtc)
            next = RunInstantOn(todayLocal.AddDays(1));

        return next - nowUtc;
    }

    /// <summary>
    /// The UTC instant at which the daily run falls on a given Stockholm date. Resolves the wall-clock
    /// hour through the timezone's rules rather than the offset in force now, so the run lands on the
    /// configured hour on both sides of a DST transition.
    /// </summary>
    private DateTimeOffset RunInstantOn(DateTime localDate)
    {
        var wall = DateTime.SpecifyKind(localDate.AddHours(_opts.DailyRunHourLocal), DateTimeKind.Unspecified);

        // Spring forward: this wall time does not exist (02:00–02:59 on the transition day), and
        // converting it would throw. Run at the first instant that does exist.
        if (Stockholm.IsInvalidTime(wall))
            wall = wall.AddHours(1);

        // Autumn back: an ambiguous wall time (02:00–02:59) resolves to standard time. Either instant
        // is acceptable — what matters is that the run happens once that day.
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(wall, Stockholm), TimeSpan.Zero);
    }
}
