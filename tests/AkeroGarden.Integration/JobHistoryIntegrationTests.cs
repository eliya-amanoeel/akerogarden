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
        var runs = await history.GetRunsAsync(bookingReference: reference);

        var found = Assert.Single(runs);
        Assert.Equal(touching, found.Run.Id);
        Assert.Equal(2, found.EntryCount);
        Assert.DoesNotContain(runs, r => r.Run.Id == notTouching);
        Assert.Equal(2, (await history.GetEntriesAsync(touching)).Count);
    }
}
