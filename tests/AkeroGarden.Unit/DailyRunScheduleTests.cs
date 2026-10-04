using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// The daily run instant is pure arithmetic over Europe/Stockholm's rules, so it is asserted
/// directly rather than inferred from whether a background timer happened to fire. The DST cases are
/// the point: resolving the configured wall-clock hour with the offset in force *now* drifts the run
/// by an hour across the spring transition, and on that day the hour 02:00 does not exist at all.
/// </summary>
public class DailyRunScheduleTests
{
    private static DateTimeOffset Utc(int y, int m, int d, int h, int min = 0) =>
        new(y, m, d, h, min, 0, TimeSpan.Zero);

    [Fact]
    public void Winter_ResolvesTheConfiguredHourAtStandardTime()
    {
        // 2026-01-15, CET (+01:00): 03:00 local is 02:00 UTC.
        Assert.Equal(Utc(2026, 1, 15, 2), DailyRunSchedule.RunInstantOn(new DateOnly(2026, 1, 15), 3));
    }

    [Fact]
    public void Summer_ResolvesTheConfiguredHourAtDaylightTime()
    {
        // 2026-07-15, CEST (+02:00): 03:00 local is 01:00 UTC.
        Assert.Equal(Utc(2026, 7, 15, 1), DailyRunSchedule.RunInstantOn(new DateOnly(2026, 7, 15), 3));
    }

    [Fact]
    public void NextRun_LaterToday_IsToday()
    {
        // 01:00 local (00:00 UTC) on a winter day; 03:00 local is still ahead.
        var next = DailyRunSchedule.NextRunUtc(Utc(2026, 1, 15, 0), 3);

        Assert.Equal(Utc(2026, 1, 15, 2), next);
        Assert.Equal(TimeSpan.FromHours(2), DailyRunSchedule.TimeUntilNextRun(Utc(2026, 1, 15, 0), 3));
    }

    [Fact]
    public void NextRun_AlreadyPastToday_RollsToTomorrow()
    {
        // 04:00 local (03:00 UTC) — today's 03:00 has gone, so the next run is tomorrow's.
        var next = DailyRunSchedule.NextRunUtc(Utc(2026, 1, 15, 3), 3);

        Assert.Equal(Utc(2026, 1, 16, 2), next);
        Assert.Equal(TimeSpan.FromHours(23), DailyRunSchedule.TimeUntilNextRun(Utc(2026, 1, 15, 3), 3));
    }

    [Fact]
    public void NextRun_ExactlyAtTheRunInstant_RollsToTomorrow()
    {
        // Boundary: standing precisely on today's run instant must not schedule a zero-length wait
        // that would spin the loop.
        Assert.Equal(Utc(2026, 1, 16, 2), DailyRunSchedule.NextRunUtc(Utc(2026, 1, 15, 2), 3));
    }

    [Fact]
    public void SpringForward_StillRunsAtTheConfiguredLocalHour()
    {
        // 2026-03-29: Stockholm jumps 02:00 CET → 03:00 CEST. True 03:00 local is 01:00 UTC.
        // Resolving with the offset in force at midnight (+01:00) would give 02:00 UTC — an hour late.
        Assert.Equal(Utc(2026, 3, 29, 1), DailyRunSchedule.RunInstantOn(new DateOnly(2026, 3, 29), 3));
    }

    [Fact]
    public void SpringForward_FromBeforeTheTransition_TargetsTheCorrectInstant()
    {
        // 2026-03-29 00:30 local (2026-03-28 23:30 UTC) — 90 minutes to the real 03:00 CEST.
        Assert.Equal(
            TimeSpan.FromMinutes(90),
            DailyRunSchedule.TimeUntilNextRun(Utc(2026, 3, 28, 23, 30), 3));
    }

    [Fact]
    public void SpringForward_ConfiguredHourDoesNotExist_UsesTheFirstRealInstant()
    {
        // 02:00 is skipped entirely on 2026-03-29; converting it directly would throw. The run moves
        // to 03:00 CEST = 01:00 UTC rather than being lost.
        Assert.Equal(Utc(2026, 3, 29, 1), DailyRunSchedule.RunInstantOn(new DateOnly(2026, 3, 29), 2));
    }

    [Fact]
    public void AutumnBack_ResolvesTheAmbiguousHourOnce()
    {
        // 2026-10-25: 03:00 CEST → 02:00 CET, so 02:00 local occurs twice. Either instant is fine;
        // what matters is that it resolves to exactly one and the job runs once. Standard time (CET,
        // +01:00) gives 01:00 UTC.
        Assert.Equal(Utc(2026, 10, 25, 1), DailyRunSchedule.RunInstantOn(new DateOnly(2026, 10, 25), 2));
    }

    [Fact]
    public void AutumnBack_UnambiguousHourIsUnaffected()
    {
        // 03:00 on the autumn transition day is not ambiguous: CET (+01:00) ⇒ 02:00 UTC.
        Assert.Equal(Utc(2026, 10, 25, 2), DailyRunSchedule.RunInstantOn(new DateOnly(2026, 10, 25), 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(23)]
    public void AnyConfiguredHour_ResolvesToThatLocalHour(int hour)
    {
        var instant = DailyRunSchedule.RunInstantOn(new DateOnly(2026, 1, 15), hour);
        var local = TimeZoneInfo.ConvertTime(
            instant, TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm"));

        Assert.Equal(hour, local.Hour);
        Assert.Equal(new DateTime(2026, 1, 15), local.Date);
    }
}
