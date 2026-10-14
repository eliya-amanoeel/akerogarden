using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Reads the job history for the admin page (FR-114): the runs grouped by night, and per run the
/// audit entries it wrote. Read-only — runs are written by <see cref="JobRunRecorder"/> and deleted
/// by the retention job.
/// </summary>
public class JobHistoryService(ApplicationDbContext db)
{
    /// <summary>How many nights one page of the job history shows.</summary>
    public const int NightsPerPage = 10;

    /// <summary>
    /// One page of nights, newest first — a night is the Swedish calendar date a run started on
    /// (NFR-40) — each holding its runs with how many audit entries each wrote. The filters narrow
    /// the <em>runs</em>, and a night appears only if one of its runs matches: <paramref name="jobName"/>
    /// to one job, <paramref name="result"/> to one outcome, <paramref name="date"/> to one night,
    /// and <paramref name="bookingReference"/> to runs that touched a booking (the "did this guest
    /// get it?" question). <paramref name="page"/> is 1-based and kept inside the pages that exist,
    /// so a filter that leaves fewer pages never strands the reader on an empty one.
    /// </summary>
    public async Task<JobNightPage> GetNightsAsync(
        string? jobName = null, string? bookingReference = null, JobRunResult? result = null,
        DateOnly? date = null, int page = 1, int pageSize = NightsPerPage)
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

        if (result is { } wanted)
        {
            runs = wanted switch
            {
                JobRunResult.Succeeded => runs.Where(r => r.Outcome == JobRunOutcome.Succeeded),
                JobRunResult.Failed => runs.Where(r => r.Outcome == JobRunOutcome.Failed),
                _ => runs.Where(r => r.Outcome == null),
            };
        }

        // One Swedish date, as a UTC window, so the filter runs in the database.
        if (date is { } night)
        {
            var from = StockholmTime.ToUtc(night, TimeOnly.MinValue);
            var to = StockholmTime.ToUtc(night.AddDays(1), TimeOnly.MinValue);
            runs = runs.Where(r => r.StartedAt >= from && r.StartedAt < to);
        }

        // The night is a Swedish date, which the database cannot derive from a UTC timestamp without
        // a time-zone conversion of its own. Six months of runs is a few hundred rows of two columns,
        // so the dates are worked out here with the one Swedish-time helper, and only the chosen
        // nights' runs are loaded in full.
        var starts = await runs.Select(r => new { r.Id, r.StartedAt }).ToListAsync();
        var dates = starts
            .Select(s => StockholmTime.DateOf(s.StartedAt))
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();
        var totalPages = Math.Max(1, (dates.Count + pageSize - 1) / pageSize);
        var current = Math.Clamp(page, 1, totalPages);
        var shown = dates.Skip((current - 1) * pageSize).Take(pageSize).ToHashSet();
        var ids = starts.Where(s => shown.Contains(StockholmTime.DateOf(s.StartedAt))).Select(s => s.Id).ToList();

        var items = await db.JobRuns
            .Where(r => ids.Contains(r.Id))
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new JobRunListItem(r, db.AuditLogs.Count(a => a.JobRunId == r.Id)))
            .ToListAsync();

        var grouped = items
            .GroupBy(i => StockholmTime.DateOf(i.Run.StartedAt))
            .OrderByDescending(g => g.Key)
            .Select(g => new JobNight(g.Key, g.ToList()))
            .ToList();

        return new JobNightPage(grouped, current, totalPages);
    }

    /// <summary>The audit entries the given runs wrote, in the order they were written — one query
    /// for a whole night, since the page's DbContext serves one operation at a time.</summary>
    public async Task<List<AuditLog>> GetEntriesAsync(IReadOnlyCollection<Guid> runIds) =>
        await db.AuditLogs
            .Where(a => a.JobRunId != null && runIds.Contains(a.JobRunId.Value))
            .OrderBy(a => a.Timestamp)
            .ToListAsync();

    /// <summary>The job names present in the history, for the filter.</summary>
    public async Task<List<string>> GetJobNamesAsync() =>
        await db.JobRuns.Select(r => r.JobName).Distinct().OrderBy(n => n).ToListAsync();
}

/// <summary>A run as listed on the job history page, with the number of entries it wrote.</summary>
public record JobRunListItem(JobRun Run, int EntryCount);

/// <summary>
/// One night of the job history: the runs that started on that Swedish date, newest first. A night
/// is only as good as its worst run, so one failed run makes the night failed, and a run that never
/// finished leaves it unfinished rather than succeeded.
/// </summary>
public record JobNight(DateOnly Date, IReadOnlyList<JobRunListItem> Runs)
{
    public JobRunOutcome? Outcome =>
        Runs.Any(r => r.Run.Outcome == JobRunOutcome.Failed) ? JobRunOutcome.Failed
        : Runs.All(r => r.Run.Outcome == JobRunOutcome.Succeeded) ? JobRunOutcome.Succeeded
        : null;

    /// <summary>Runs that did not succeed, including one that never finished.</summary>
    public int NotSucceededCount => Runs.Count(r => r.Run.Outcome != JobRunOutcome.Succeeded);

    public int EntryCount => Runs.Sum(r => r.EntryCount);
}

/// <summary>One page of nights: which page it is (1-based) and how many there are.</summary>
public record JobNightPage(IReadOnlyList<JobNight> Nights, int Page, int TotalPages);

/// <summary>The job history's result filter: a run's outcome, with a run that never finished — no
/// outcome recorded, still running or stopped midway — as a value of its own.</summary>
public enum JobRunResult
{
    Succeeded,
    Failed,
    Unfinished,
}
