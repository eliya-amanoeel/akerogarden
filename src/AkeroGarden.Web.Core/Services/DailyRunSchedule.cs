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
    private static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    /// <summary>
    /// The next UTC instant at which the daily run fires: today's run if it is still ahead,
    /// otherwise tomorrow's.
    /// </summary>
    public static DateTimeOffset NextRunUtc(DateTimeOffset nowUtc, int runHourLocal)
    {
        var todayLocal = TimeZoneInfo.ConvertTime(nowUtc, Stockholm).Date;

        var next = RunInstantOn(todayLocal, runHourLocal);
        return next <= nowUtc ? RunInstantOn(todayLocal.AddDays(1), runHourLocal) : next;
    }

    /// <summary>Delay from <paramref name="nowUtc"/> until the next run. Never negative.</summary>
    public static TimeSpan TimeUntilNextRun(DateTimeOffset nowUtc, int runHourLocal) =>
        NextRunUtc(nowUtc, runHourLocal) - nowUtc;

    /// <summary>
    /// The UTC instant at which the run falls on a given Stockholm date.
    /// </summary>
    public static DateTimeOffset RunInstantOn(DateTime localDate, int runHourLocal)
    {
        var wall = DateTime.SpecifyKind(localDate.Date.AddHours(runHourLocal), DateTimeKind.Unspecified);

        // Spring forward: this wall time does not exist (02:00–02:59 on the transition day) and
        // converting it would throw. Run at the first instant that does exist instead.
        if (Stockholm.IsInvalidTime(wall))
            wall = wall.AddHours(1);

        // Autumn back: an ambiguous wall time (02:00–02:59 occurs twice) resolves to standard time.
        // Either instant is acceptable — what matters is that the run happens once that day.
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(wall, Stockholm), TimeSpan.Zero);
    }
}
