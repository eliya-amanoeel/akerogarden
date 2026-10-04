namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// The one place a UTC instant becomes a Swedish date or clock time, and back. Every date and time
/// the system judges or shows is Europe/Stockholm; every instant it stores is UTC.
/// <para>Why this exists: the App Service runs UTC, so <c>DateTime.Now</c>, <c>ToLocalTime()</c> and
/// <c>DateOnly.FromDateTime(utc)</c> all quietly produce UTC. Between 00:00 and 02:00 Swedish time
/// that is yesterday's date — which decided cancellation deposits, link issuance and the earliest
/// bookable day — and a 14:00 deadline stored as 14:00 UTC was really 15:00/16:00 while every email
/// said 14:00. Setting <c>TZ</c> on the app service is not a fix: it changes what
/// <c>ToLocalTime()</c> returns on one host without changing what is stored.
/// <c>NoAmbientTimeTests</c> keeps conversions out of every other file.</para>
/// </summary>
public static class StockholmTime
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    /// <summary>Today's date in Sweden.</summary>
    public static DateOnly Today(TimeProvider clock) => DateOf(clock.GetUtcNow().UtcDateTime);

    /// <summary>The Swedish date on which a stored UTC instant fell.</summary>
    public static DateOnly DateOf(DateTime utc) => DateOnly.FromDateTime(ToLocal(utc));

    /// <summary>A stored UTC instant as Swedish wall-clock time, for display.</summary>
    public static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    /// <summary>
    /// The UTC instant of a Swedish date and clock time. Spring forward: a wall time that does not
    /// exist (02:00–02:59 on the transition day) moves to the first one that does, rather than
    /// throwing. Autumn back: an ambiguous wall time resolves to standard time.
    /// </summary>
    public static DateTime ToUtc(DateOnly date, TimeOnly time)
    {
        var wall = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (Zone.IsInvalidTime(wall))
            wall = wall.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(wall, Zone);
    }
}
