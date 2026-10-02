using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Reads the job history for the admin page (FR-114): the runs, and per run the audit entries it
/// wrote. Read-only — runs are written by <see cref="JobRunRecorder"/> and deleted by the retention job.
/// </summary>
public class JobHistoryService(ApplicationDbContext db)
{
    /// <summary>
    /// Runs newest first, each with how many audit entries it wrote. Narrowed to one job by
    /// <paramref name="jobName"/>, and to runs that touched a booking by
    /// <paramref name="bookingReference"/> — the "did this guest get it?" question.
    /// </summary>
    public async Task<List<JobRunListItem>> GetRunsAsync(string? jobName = null, string? bookingReference = null)
    {
        var runs = db.JobRuns.AsQueryable();

        if (!string.IsNullOrWhiteSpace(jobName))
            runs = runs.Where(r => r.JobName == jobName);

        if (!string.IsNullOrWhiteSpace(bookingReference))
        {
            var reference = bookingReference.Trim();
            runs = runs.Where(r => db.AuditLogs.Any(a => a.JobRunId == r.Id
                && a.BookingReferenceSnapshot != null
                && EF.Functions.ILike(a.BookingReferenceSnapshot, $"%{reference}%")));
        }

        return await runs
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new JobRunListItem(r, db.AuditLogs.Count(a => a.JobRunId == r.Id)))
            .ToListAsync();
    }

    /// <summary>The audit entries one run wrote, in the order it wrote them.</summary>
    public async Task<List<AuditLog>> GetEntriesAsync(Guid runId) =>
        await db.AuditLogs
            .Where(a => a.JobRunId == runId)
            .OrderBy(a => a.Timestamp)
            .ToListAsync();

    /// <summary>The job names present in the history, for the filter.</summary>
    public async Task<List<string>> GetJobNamesAsync() =>
        await db.JobRuns.Select(r => r.JobName).Distinct().OrderBy(n => n).ToListAsync();
}

/// <summary>A run as listed on the job history page, with the number of entries it wrote.</summary>
public record JobRunListItem(JobRun Run, int EntryCount);
