using System.Diagnostics;
using AkeroGarden.Web.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AkeroGarden.Unit;

/// <summary>
/// The daily runner is the only thing that fires GDPR retention deletion and the 14-day reminders
/// (Phase 5), so both halves of its contract matter: it must fire at the configured Europe/Stockholm
/// hour, and one job throwing must never stop the remaining jobs or the loop itself — otherwise a
/// failing reminder would silently suppress retention deletion indefinitely.
/// </summary>
public class DailyJobRunnerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class RecordingJob(string name, bool throws = false) : IScheduledJob
    {
        private int _runCount;

        public string Name => name;
        public int RunCount => Volatile.Read(ref _runCount);

        public Task RunAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runCount);
            return throws
                ? Task.FromException(new InvalidOperationException($"{name} misslyckades"))
                : Task.CompletedTask;
        }
    }

    private static DailyJobRunner CreateRunner(
        FakeTimeProvider clock, JobOptions options, params IScheduledJob[] jobs)
    {
        var services = new ServiceCollection();
        foreach (var job in jobs)
            services.AddScoped(_ => job);

        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new DailyJobRunner(
            scopeFactory, clock, Options.Create(options), NullLogger<DailyJobRunner>.Instance);
    }

    /// <summary>
    /// Starts the runner and lets it reach its first <c>Task.Delay</c>, so the timer is registered on
    /// the fake clock before any <c>Advance</c> — advancing a clock the runner has not yet subscribed
    /// to would silently skip the scheduled instant.
    /// </summary>
    private static async Task StartAndSettleAsync(DailyJobRunner runner)
    {
        await runner.StartAsync(CancellationToken.None);
        await Task.Delay(50);
    }

    /// <summary>Polls until the job has run at least <paramref name="expected"/> times, or gives up.</summary>
    private static async Task<bool> WaitForRunsAsync(RecordingJob job, int expected)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < Timeout)
        {
            if (job.RunCount >= expected)
                return true;
            await Task.Delay(10);
        }

        return job.RunCount >= expected;
    }

    /// <summary>2026-01-15 00:00 UTC — 01:00 Europe/Stockholm (CET, +01:00), well clear of any DST edge.</summary>
    private static FakeTimeProvider WinterClock() =>
        new(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Disabled_RunsNoJobsAtAll()
    {
        var job = new RecordingJob("Gallring");
        var runner = CreateRunner(WinterClock(), new JobOptions { Enabled = false }, job);

        await StartAndSettleAsync(runner);

        Assert.Equal(0, job.RunCount);
        await runner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunsAtTheConfiguredLocalHour_NotBefore()
    {
        // 01:00 local, run hour 03:00 ⇒ the first run is two hours away.
        var clock = WinterClock();
        var job = new RecordingJob("Gallring");
        var runner = CreateRunner(clock, new JobOptions { DailyRunHourLocal = 3 }, job);

        await StartAndSettleAsync(runner);

        clock.Advance(TimeSpan.FromHours(2) - TimeSpan.FromMinutes(1));
        await Task.Delay(50);
        Assert.Equal(0, job.RunCount);

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(await WaitForRunsAsync(job, 1), "Jobbet kördes inte vid den schemalagda timmen.");

        await runner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OneJobThrowing_DoesNotPreventTheOthers()
    {
        var failing = new RecordingJob("Påminnelser", throws: true);
        var following = new RecordingJob("Gallring");
        var clock = WinterClock();
        var runner = CreateRunner(clock, new JobOptions { DailyRunHourLocal = 3 }, failing, following);

        await StartAndSettleAsync(runner);
        clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(1));

        Assert.True(await WaitForRunsAsync(following, 1),
            "Ett kastande jobb stoppade det efterföljande jobbet.");
        Assert.Equal(1, failing.RunCount);

        await runner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OneJobThrowing_DoesNotKillTheLoop_NextDayStillRuns()
    {
        var failing = new RecordingJob("Påminnelser", throws: true);
        var clock = WinterClock();
        var runner = CreateRunner(clock, new JobOptions { DailyRunHourLocal = 3 }, failing);

        await StartAndSettleAsync(runner);

        clock.Advance(TimeSpan.FromHours(2) + TimeSpan.FromMinutes(1));
        Assert.True(await WaitForRunsAsync(failing, 1), "Första körningen uteblev.");

        clock.Advance(TimeSpan.FromDays(1));
        Assert.True(await WaitForRunsAsync(failing, 2),
            "Loopen dog efter att ett jobb kastat — nästa dags körning uteblev.");

        await runner.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// The run instant is resolved through the timezone's rules, not the offset in force when the delay
    /// is computed — so on the March transition day (Stockholm jumps 02:00 CET → 03:00 CEST) the job
    /// still fires at 03:00 local, not an hour late.
    /// </summary>
    [Fact]
    public async Task SpringDstTransition_StillRunsAtTheConfiguredLocalHour()
    {
        // 2026-03-28 23:30 UTC = 2026-03-29 00:30 local (CET, +01:00); DST starts at 02:00 local,
        // so true 03:00 CEST is 01:00 UTC — 90 minutes away.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 28, 23, 30, 0, TimeSpan.Zero));
        var job = new RecordingJob("Gallring");
        var runner = CreateRunner(clock, new JobOptions { DailyRunHourLocal = 3 }, job);

        await StartAndSettleAsync(runner);

        clock.Advance(TimeSpan.FromMinutes(89));
        await Task.Delay(50);
        Assert.Equal(0, job.RunCount);

        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True(await WaitForRunsAsync(job, 1),
            "Jobbet kördes inte kl. 03:00 lokal tid vid sommartidsomställningen.");

        await runner.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// 02:00 does not exist on the spring transition day. The configured hour must still resolve to a
    /// real instant (the first one after the jump) rather than throwing or silently skipping the day.
    /// </summary>
    [Fact]
    public async Task SpringDstTransition_RunHourThatDoesNotExist_RunsAtTheFirstRealInstant()
    {
        // Run hour 02:00 is skipped entirely on 2026-03-29; the first real instant is 03:00 CEST = 01:00 UTC.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 28, 23, 30, 0, TimeSpan.Zero));
        var job = new RecordingJob("Gallring");
        var runner = CreateRunner(clock, new JobOptions { DailyRunHourLocal = 2 }, job);

        await StartAndSettleAsync(runner);

        clock.Advance(TimeSpan.FromMinutes(91));
        Assert.True(await WaitForRunsAsync(job, 1),
            "Körningen uteblev när den konfigurerade timmen inte existerade p.g.a. sommartid.");

        await runner.StopAsync(CancellationToken.None);
    }
}
