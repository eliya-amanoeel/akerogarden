namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Pure scheduling decisions for <see cref="DailyJobRunner"/> (Phase 5), unit-testable without a
/// timer or a hosted service (NFR-26). The daily run fires at a configured wall-clock hour in
/// Europe/Stockholm; resolving that hour to a UTC instant has to go through the timezone's rules
/// rather than the offset in force when the delay is computed, or the run drifts by an hour across
/// a DST transition — and on the spring transition day the configured hour may not exist at all.
/// </summary>
public static class DailyRunSchedule
{
    /// <summary>
    /// The next UTC instant at which the daily run fires: today's run if it is still ahead,
    /// otherwise tomorrow's.
    /// </summary>
    public static DateTimeOffset NextRunUtc(DateTimeOffset nowUtc, int runHourLocal)
    {
        var todayLocal = StockholmTime.DateOf(nowUtc.UtcDateTime);

        var next = RunInstantOn(todayLocal, runHourLocal);
        return next <= nowUtc ? RunInstantOn(todayLocal.AddDays(1), runHourLocal) : next;
    }

    /// <summary>Delay from <paramref name="nowUtc"/> until the next run. Never negative.</summary>
    public static TimeSpan TimeUntilNextRun(DateTimeOffset nowUtc, int runHourLocal) =>
        NextRunUtc(nowUtc, runHourLocal) - nowUtc;

    /// <summary>
    /// The UTC instant at which the run falls on a given Stockholm date. On the spring transition
    /// day an hour that does not exist runs at the first one that does; on the autumn day an hour
    /// that occurs twice runs once, in standard time (see <see cref="StockholmTime.ToUtc"/>).
    /// </summary>
    public static DateTimeOffset RunInstantOn(DateOnly localDate, int runHourLocal) =>
        new(StockholmTime.ToUtc(localDate, new TimeOnly(runHourLocal, 0)), TimeSpan.Zero);
}
