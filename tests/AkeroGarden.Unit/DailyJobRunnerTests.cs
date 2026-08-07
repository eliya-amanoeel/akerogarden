using System.Diagnostics;
using AkeroGarden.Web.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AkeroGarden.Unit;

/// <summary>
/// The runner's loop behaviour: it must honour the enabled switch, and one job throwing must never
/// stop the remaining jobs or kill the loop — otherwise a failing reminder would silently suppress
/// GDPR retention deletion indefinitely (§2.7, FR-63/69).
///
/// These tests deliberately assert only that a run *eventually* happens, never precisely when. The
/// exact instant is pure arithmetic and is asserted directly in <see cref="DailyRunScheduleTests"/>.
/// Pinning timing here would mean racing the hosted service's timer registration against a fake
/// clock — a bet that passes locally and fails on a loaded CI agent, which is exactly what happened
/// on 2026-07-18.
/// </summary>
public class DailyJobRunnerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

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
    /// Advances the fake clock in day-long steps until the job has run <paramref name="expected"/>
    /// times. Re-advancing is what makes this race-free: if the runner had not yet registered its
    /// timer when an advance landed, the next one still carries it past the scheduled instant, so
    /// the test cannot lose to a slow agent. It never asserts *when* the run happened.
    /// </summary>
    private static async Task<bool> AdvanceUntilRunAsync(
        FakeTimeProvider clock, RecordingJob job, int expected)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < Timeout)
        {
            if (job.RunCount >= expected)
                return true;

            clock.Advance(TimeSpan.FromDays(1));
            await Task.Delay(20);
        }

        return job.RunCount >= expected;
    }

    /// <summary>2026-01-15 00:00 UTC — 01:00 Europe/Stockholm, clear of any DST edge.</summary>
    private static FakeTimeProvider WinterClock() =>
        new(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Disabled_RunsNoJobsAtAll()
    {
        var clock = WinterClock();
        var job = new RecordingJob("Gallring");
        var runner = CreateRunner(clock, new JobOptions { Enabled = false }, job);

        await runner.StartAsync(CancellationToken.None);

        // Push the clock well past several scheduled instants; a disabled runner must ignore them.
        for (var i = 0; i < 3; i++)
        {
            clock.Advance(TimeSpan.FromDays(1));
            await Task.Delay(20);
        }

        Assert.Equal(0, job.RunCount);
        await runner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Enabled_RunsTheJob()
    {
        var clock = WinterClock();
        var job = new RecordingJob("Gallring");
        var runner = CreateRunner(clock, new JobOptions { DailyRunHourLocal = 3 }, job);

        await runner.StartAsync(CancellationToken.None);

        Assert.True(await AdvanceUntilRunAsync(clock, job, 1), "Jobbet kördes aldrig.");

        await runner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunOnStartup_RunsOnceImmediately_WithoutAdvancingTheClock()
    {
        // The point of the seam: a scheduled email is reachable in a dev inbox without waiting for
        // the next run hour. Asserted with the clock held still, which is the whole claim.
        var clock = WinterClock();
        var job = new RecordingJob("Påminnelser");
        var runner = CreateRunner(clock, new JobOptions { RunOnStartup = true, DailyRunHourLocal = 3 }, job);

        await runner.StartAsync(CancellationToken.None);

        var sw = Stopwatch.StartNew();
        while (job.RunCount == 0 && sw.Elapsed < Timeout)
            await Task.Delay(20);

        Assert.True(job.RunCount >= 1, "Jobbet kördes inte direkt vid start.");
        await runner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunOnStartup_StillFollowsTheNormalScheduleAfterwards()
    {
        // A one-off run must not replace the loop — otherwise turning the seam on in a long-running
        // process would silently stop retention deletion (§2.7, FR-69).
        var clock = WinterClock();
        var job = new RecordingJob("Gallring");
        var runner = CreateRunner(clock, new JobOptions { RunOnStartup = true, DailyRunHourLocal = 3 }, job);

        await runner.StartAsync(CancellationToken.None);

        Assert.True(await AdvanceUntilRunAsync(clock, job, 2), "Schemat tog inte över efter startkörningen.");

        await runner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task RunOnStartup_IsOffByDefault()
    {
        // It is a development seam; a production process must not run jobs the moment it boots.
        Assert.False(new JobOptions().RunOnStartup);
    }

    [Fact]
    public async Task Disabled_BeatsRunOnStartup()
    {
        var clock = WinterClock();
        var job = new RecordingJob("Gallring");
        var runner = CreateRunner(clock, new JobOptions { Enabled = false, RunOnStartup = true }, job);

        await runner.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        Assert.Equal(0, job.RunCount);
        await runner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OneJobThrowing_DoesNotPreventTheOthers()
    {
        var failing = new RecordingJob("Påminnelser", throws: true);
        var following = new RecordingJob("Gallring");
        var clock = WinterClock();
        var runner = CreateRunner(clock, new JobOptions { DailyRunHourLocal = 3 }, failing, following);

        await runner.StartAsync(CancellationToken.None);

        Assert.True(await AdvanceUntilRunAsync(clock, following, 1),
            "Ett kastande jobb stoppade det efterföljande jobbet.");
        Assert.True(failing.RunCount >= 1, "Det kastande jobbet kördes aldrig.");

        await runner.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task OneJobThrowing_DoesNotKillTheLoop_ItRunsAgain()
    {
        var failing = new RecordingJob("Påminnelser", throws: true);
        var clock = WinterClock();
        var runner = CreateRunner(clock, new JobOptions { DailyRunHourLocal = 3 }, failing);

        await runner.StartAsync(CancellationToken.None);

        Assert.True(await AdvanceUntilRunAsync(clock, failing, 2),
            "Loopen dog efter att ett jobb kastat — den körde inte igen.");

        await runner.StopAsync(CancellationToken.None);
    }
}
