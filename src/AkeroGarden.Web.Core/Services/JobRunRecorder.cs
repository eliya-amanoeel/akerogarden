using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Default <see cref="IJobRunRecorder"/>. <see cref="DailyJobRunner"/> resolves it from a DI scope
/// of its own, so its <see cref="ApplicationDbContext"/> is not the one the jobs use — a job that
/// throws with unsaved changes can never have them committed alongside its own failure record.
/// </summary>
public sealed class JobRunRecorder(ApplicationDbContext db, TimeProvider clock) : IJobRunRecorder
{
    public async Task<Guid> StartAsync(string jobName, CancellationToken cancellationToken)
    {
        var run = new JobRun
        {
            Id = Guid.NewGuid(),
            JobName = jobName,
            StartedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.JobRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run.Id;
    }

    public async Task FinishAsync(Guid runId, Exception? failure, CancellationToken cancellationToken)
    {
        var run = await db.JobRuns.FindAsync([runId], cancellationToken);
        if (run is null)
            return;

        // Sending never throws (NFR-13), so a job whose emails all failed still "succeeds" in code.
        // The EmailSendFailed entries it wrote are what make that visible.
        var failedSends = failure is null
            ? await db.AuditLogs.CountAsync(
                a => a.JobRunId == runId && a.Action == AuditAction.EmailSendFailed, cancellationToken)
            : 0;

        run.FinishedAt = clock.GetUtcNow().UtcDateTime;
        run.Outcome = failure is null && failedSends == 0 ? JobRunOutcome.Succeeded : JobRunOutcome.Failed;
        run.FailureReason = FailureReason(failure, failedSends);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The admin-facing reason, in Swedish. Nothing from the exception itself: its message can carry
    /// an email address or other guest data (NFR-37), and its type name is English (NFR-32). The full
    /// exception is already logged, which is where the maintainer looks.
    /// </summary>
    public static string? FailureReason(Exception? failure, int failedSends) =>
        failure is not null
            ? "Jobbet avbröts av ett tekniskt fel. Se loggarna för detaljer."
            : failedSends switch
            {
                0 => null,
                1 => "1 mejl kunde inte skickas.",
                _ => $"{failedSends} mejl kunde inte skickas.",
            };
}
