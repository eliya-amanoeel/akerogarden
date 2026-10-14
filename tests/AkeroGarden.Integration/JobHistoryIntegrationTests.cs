using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// The job history against real PostgreSQL (FR-114): a run is recorded with its outcome, the audit
/// entries written during it are tagged with it, and the admin page's query finds a booking's runs.
/// What each job writes is covered in that job's own test class.
/// </summary>
public class JobHistoryIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public JobHistoryIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private IServiceProvider NewProvider() =>
        _fixture.BuildServices(s =>
        {
            s.AddScoped<IJobRunRecorder, JobRunRecorder>();
            s.AddScoped<JobHistoryService>();
        });

    /// <summary>Starts a run and, inside it, writes audit entries exactly as a job does — through
    /// <see cref="IAuditWriter"/> with the run set on <see cref="JobRunContext"/>.</summary>
    private static async Task<Guid> RunWithEntriesAsync(
        IServiceProvider provider, string jobName, params (AuditAction Action, string Reference)[] entries)
    {
        Guid runId;
        using (var recordScope = provider.CreateScope())
            runId = await recordScope.ServiceProvider.GetRequiredService<IJobRunRecorder>()
                .StartAsync(jobName, CancellationToken.None);

        using (var jobScope = provider.CreateScope())
        {
            jobScope.ServiceProvider.GetRequiredService<JobRunContext>().CurrentRunId = runId;
            var audit = jobScope.ServiceProvider.GetRequiredService<IAuditWriter>();
            foreach (var (action, reference) in entries)
            {
                await audit.RecordAsync(action, nameof(Booking), Guid.NewGuid(), "Testhändelse.",
                    bookingId: null, bookingReference: reference, actor: AuditActor.System);
            }
            await jobScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
        }

        return runId;
    }

    private static async Task<JobRun> FinishAsync(IServiceProvider provider, Guid runId, Exception? failure = null)
    {
        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IJobRunRecorder>().FinishAsync(runId, failure, CancellationToken.None);
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().JobRuns.SingleAsync(r => r.Id == runId);
    }

    private static string NewReference() => $"AG-T-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task Entries_written_during_a_run_are_tagged_with_it_and_entries_outside_are_not()
    {
        var provider = NewProvider();
        var reference = NewReference();
        var runId = await RunWithEntriesAsync(provider, "Utfärdande av slutförandelänk",
            (AuditAction.CompletionLinkIssued, reference));

        // The same writer outside a run — an admin action — must stay untagged.
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuditWriter>().RecordAsync(
                AuditAction.AdminNotesEdited, nameof(Booking), Guid.NewGuid(), "Utanför körning.",
                bookingReference: reference);
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
        }

        using var check = provider.CreateScope();
        var entries = await check.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuditLogs
            .Where(a => a.BookingReferenceSnapshot == reference).ToListAsync();
        Assert.Equal(runId, entries.Single(e => e.Action == AuditAction.CompletionLinkIssued).JobRunId);
        Assert.Null(entries.Single(e => e.Action == AuditAction.AdminNotesEdited).JobRunId);
    }

    [Fact]
    public async Task A_clean_run_is_recorded_as_succeeded()
    {
        var provider = NewProvider();
        var runId = await RunWithEntriesAsync(provider, "14-dagarspåminnelse",
            (AuditAction.PreArrivalReminderSent, NewReference()));

        var run = await FinishAsync(provider, runId);

        Assert.Equal(JobRunOutcome.Succeeded, run.Outcome);
        Assert.Null(run.FailureReason);
        Assert.NotNull(run.FinishedAt);
    }

    [Fact]
    public async Task A_run_with_a_failed_send_is_recorded_as_failed_even_though_nothing_threw()
    {
        // Sending never throws (NFR-13); the EmailSendFailed entry is the only signal.
        var provider = NewProvider();
        var runId = await RunWithEntriesAsync(provider, "14-dagarspåminnelse",
            (AuditAction.PreArrivalReminderSent, NewReference()),
            (AuditAction.EmailSendFailed, NewReference()));

        var run = await FinishAsync(provider, runId);

        Assert.Equal(JobRunOutcome.Failed, run.Outcome);
        Assert.Equal("1 mejl kunde inte skickas.", run.FailureReason);
    }

    [Fact]
    public async Task A_run_that_threw_is_recorded_as_failed_with_a_swedish_reason()
    {
        var provider = NewProvider();
        var runId = await RunWithEntriesAsync(provider, "GDPR-gallring");

        var run = await FinishAsync(provider, runId, new TimeoutException("anna@example.se svarade inte"));

        Assert.Equal(JobRunOutcome.Failed, run.Outcome);
        Assert.Equal("Jobbet avbröts av ett tekniskt fel. Se loggarna för detaljer.", run.FailureReason);
    }

    [Fact]
    public async Task Searching_a_reference_finds_only_the_runs_that_touched_it_with_their_entry_counts()
    {
        var provider = NewProvider();
        var reference = NewReference();
        var touching = await RunWithEntriesAsync(provider, "14-dagarspåminnelse",
            (AuditAction.PreArrivalReminderSent, reference),
            (AuditAction.PreArrivalReminderSent, NewReference()));
        var notTouching = await RunWithEntriesAsync(provider, "14-dagarspåminnelse",
            (AuditAction.PreArrivalReminderSent, NewReference()));

        using var scope = provider.CreateScope();
        var history = scope.ServiceProvider.GetRequiredService<JobHistoryService>();
        var page = await history.GetNightsAsync(bookingReference: reference);

        // The filter narrows the runs, so the night holds only the run that touched the booking —
        // not the other run started the same moment.
        var night = Assert.Single(page.Nights);
        var found = Assert.Single(night.Runs);
        Assert.Equal(touching, found.Run.Id);
        Assert.Equal(2, found.EntryCount);
        Assert.DoesNotContain(page.Nights.SelectMany(n => n.Runs), r => r.Run.Id == notTouching);
        Assert.Equal(2, (await history.GetEntriesAsync([touching])).Count);
    }

    // ── Nights (FR-114) — each test on its own database: the subject is how the whole table groups
    //    and pages, which another class's runs in the shared database would change. ──────────────

    private Task<IServiceProvider> IsolatedProviderAsync() =>
        _fixture.BuildIsolatedServicesAsync(s => s.AddScoped<JobHistoryService>());

    private static JobRun Run(DateOnly date, TimeOnly time, string job, JobRunOutcome? outcome = JobRunOutcome.Succeeded) => new()
    {
        Id = Guid.NewGuid(),
        JobName = job,
        StartedAt = StockholmTime.ToUtc(date, time),
        FinishedAt = outcome is null ? null : StockholmTime.ToUtc(date, time).AddSeconds(5),
        Outcome = outcome,
    };

    [Fact]
    public async Task Runs_are_grouped_by_swedish_night_newest_first_ten_to_a_page()
    {
        var provider = await IsolatedProviderAsync();
        var today = StockholmTime.Today(TimeProvider.System);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // 32 nights of two jobs each, at the hour they really run.
            for (var i = 1; i <= 32; i++)
            {
                var night = today.AddDays(-i);
                db.JobRuns.AddRange(Run(night, new TimeOnly(3, 0), "GDPR-gallring"), Run(night, new TimeOnly(3, 0, 5), "14-dagarspåminnelse"));
            }
            // The edges of a Swedish date, which a UTC grouping gets wrong for an hour or two every
            // day: 00:30 belongs to its own date, 23:30 to the date before.
            db.JobRuns.Add(Run(today.AddDays(-1), new TimeOnly(0, 30), "Påminnelse om handpenning"));
            db.JobRuns.Add(Run(today.AddDays(-2), new TimeOnly(23, 30), "Påminnelse om handpenning"));
            await db.SaveChangesAsync();
        }

        await using var read = provider.CreateAsyncScope();
        var history = read.ServiceProvider.GetRequiredService<JobHistoryService>();

        var first = await history.GetNightsAsync();
        Assert.Equal(1, first.Page);
        Assert.Equal(4, first.TotalPages);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => today.AddDays(-i)), first.Nights.Select(n => n.Date));
        Assert.Equal(3, first.Nights[0].Runs.Count);
        Assert.Equal(3, first.Nights[1].Runs.Count);
        Assert.Equal(2, first.Nights[2].Runs.Count);
        // Newest run first inside a night too.
        Assert.True(first.Nights[1].Runs[0].Run.StartedAt > first.Nights[1].Runs[1].Run.StartedAt);

        var last = await history.GetNightsAsync(page: 4);
        Assert.Equal(new[] { today.AddDays(-31), today.AddDays(-32) }, last.Nights.Select(n => n.Date));

        // A page past the end is kept inside the pages that exist, never an empty page.
        var beyond = await history.GetNightsAsync(page: 9);
        Assert.Equal(4, beyond.Page);
        Assert.Equal(last.Nights.Select(n => n.Date), beyond.Nights.Select(n => n.Date));

        // One date is that night alone, by the Swedish clock: the 23:30 run is in, the next
        // morning's 00:30 run is not.
        var oneNight = await history.GetNightsAsync(date: today.AddDays(-2));
        var night2 = Assert.Single(oneNight.Nights);
        Assert.Equal(today.AddDays(-2), night2.Date);
        Assert.Equal(3, night2.Runs.Count);
        Assert.Equal(1, oneNight.TotalPages);
    }

    [Fact]
    public async Task A_night_is_as_good_as_its_worst_run_and_the_result_filter_keeps_only_matching_runs()
    {
        var provider = await IsolatedProviderAsync();
        var today = StockholmTime.Today(TimeProvider.System);
        var bad = today.AddDays(-1);
        var good = today.AddDays(-2);
        var unfinished = today.AddDays(-3);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.JobRuns.AddRange(
                Run(bad, new TimeOnly(3, 0), "GDPR-gallring"),
                Run(bad, new TimeOnly(3, 0, 5), "14-dagarspåminnelse", JobRunOutcome.Failed),
                Run(good, new TimeOnly(3, 0), "GDPR-gallring"),
                Run(unfinished, new TimeOnly(3, 0), "GDPR-gallring"),
                Run(unfinished, new TimeOnly(3, 0, 5), "14-dagarspåminnelse", outcome: null));
            await db.SaveChangesAsync();
        }

        await using var read = provider.CreateAsyncScope();
        var history = read.ServiceProvider.GetRequiredService<JobHistoryService>();

        var all = await history.GetNightsAsync();
        Assert.Equal(JobRunOutcome.Failed, all.Nights.Single(n => n.Date == bad).Outcome);
        Assert.Equal(1, all.Nights.Single(n => n.Date == bad).NotSucceededCount);
        Assert.Equal(JobRunOutcome.Succeeded, all.Nights.Single(n => n.Date == good).Outcome);
        Assert.Null(all.Nights.Single(n => n.Date == unfinished).Outcome);

        var failed = await history.GetNightsAsync(result: JobRunResult.Failed);
        var failedNight = Assert.Single(failed.Nights);
        Assert.Equal(bad, failedNight.Date);
        Assert.Equal(JobRunOutcome.Failed, Assert.Single(failedNight.Runs).Run.Outcome);

        var notFinished = await history.GetNightsAsync(result: JobRunResult.Unfinished);
        var unfinishedNight = Assert.Single(notFinished.Nights);
        Assert.Equal(unfinished, unfinishedNight.Date);
        Assert.Null(Assert.Single(unfinishedNight.Runs).Run.Outcome);

        // Every night had one job that succeeded; filtered to those, each night shows only that job.
        var succeeded = await history.GetNightsAsync(result: JobRunResult.Succeeded);
        Assert.Equal(new[] { bad, good, unfinished }, succeeded.Nights.Select(n => n.Date));
        Assert.All(succeeded.Nights, n => Assert.Equal(JobRunOutcome.Succeeded, Assert.Single(n.Runs).Run.Outcome));
    }
}
