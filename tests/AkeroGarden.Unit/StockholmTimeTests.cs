using AkeroGarden.Web.Core.Services;
using Microsoft.Extensions.Time.Testing;

namespace AkeroGarden.Unit;

/// <summary>
/// The one UTC ↔ Swedish conversion point. The cases that matter are the two hours after Swedish
/// midnight (still yesterday in UTC) and the two clock-change days.
/// </summary>
public class StockholmTimeTests
{
    private static DateTime Utc(int y, int m, int d, int h, int min = 0) =>
        new(y, m, d, h, min, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(2026, 7, 14, 22, 30, 2026, 7, 15)]  // 00:30 summer time — UTC is still the 14th
    [InlineData(2026, 1, 14, 23, 30, 2026, 1, 15)]  // 00:30 winter time
    [InlineData(2026, 7, 15, 21, 59, 2026, 7, 15)]  // 23:59 summer time — still the 15th
    [InlineData(2026, 7, 15, 22, 0, 2026, 7, 16)]   // midnight summer time
    public void Today_IsTheSwedishDate_AfterMidnight(int y, int m, int d, int h, int min, int ey, int em, int ed)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(Utc(y, m, d, h, min)));

        Assert.Equal(new DateOnly(ey, em, ed), StockholmTime.Today(clock));
        Assert.Equal(new DateOnly(ey, em, ed), StockholmTime.DateOf(Utc(y, m, d, h, min)));
    }

    [Theory]
    [InlineData(2026, 1, 15, 2)] // winter: UTC+1 — the 03:00 run is stored as 02:00 UTC
    [InlineData(2026, 7, 15, 1)] // summer: UTC+2 — the 03:00 run is stored as 01:00 UTC
    public void ToLocal_ShowsTheNightlyRunAtThreeOClock_InBothSeasons(int y, int m, int d, int utcHour)
    {
        // FR-114: the job history must read 03:00, the hour the admin is told the jobs run — not the
        // server's own zone, and not one hour off for half the year.
        Assert.Equal(new DateTime(y, m, d, 3, 0, 0), StockholmTime.ToLocal(Utc(y, m, d, utcHour)));
    }

    [Theory]
    [InlineData(2026, 7, 7, 12)]  // summer: 14:00 Swedish = 12:00 UTC
    [InlineData(2026, 12, 7, 13)] // winter: 14:00 Swedish = 13:00 UTC
    public void ToUtc_IsTheInverseOfToLocal(int y, int m, int d, int expectedUtcHour)
    {
        var utc = StockholmTime.ToUtc(new DateOnly(y, m, d), new TimeOnly(14, 0));

        Assert.Equal(Utc(y, m, d, expectedUtcHour), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(new DateTime(y, m, d, 14, 0, 0), StockholmTime.ToLocal(utc));
    }

    [Fact]
    public void ToUtc_OnTheSpringChange_MovesAMissingHourForward()
    {
        // 2026-03-29: clocks jump from 02:00 to 03:00, so 02:30 does not exist. Moved to 03:30
        // (01:30 UTC) rather than throwing.
        Assert.Equal(Utc(2026, 3, 29, 1, 30), StockholmTime.ToUtc(new DateOnly(2026, 3, 29), new TimeOnly(2, 30)));
    }

    [Fact]
    public void ToUtc_OnTheAutumnChange_ResolvesTheRepeatedHourToStandardTime()
    {
        // 2026-10-25: 02:30 happens twice; standard time (UTC+1) is 01:30 UTC.
        Assert.Equal(Utc(2026, 10, 25, 1, 30), StockholmTime.ToUtc(new DateOnly(2026, 10, 25), new TimeOnly(2, 30)));
    }

    [Fact]
    public void ToLocal_TreatsAnUnspecifiedKindAsUtc()
    {
        // Npgsql hands back timestamptz as Kind=Utc, but a value built in code may be Unspecified.
        // Every stored instant is UTC, so both must read the same.
        var unspecified = new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(StockholmTime.ToLocal(Utc(2026, 7, 15, 12)), StockholmTime.ToLocal(unspecified));
    }
}
