namespace AkeroGarden.Web.Core.Services;

/// <summary>Configuration for the daily background-job runner (config section <c>Jobs</c>).</summary>
public class JobOptions
{
    /// <summary>Master switch — when false, <see cref="DailyJobRunner"/> does not schedule any run
    /// (used to keep local/dev quiet or disable the loop in specific environments).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hour of day (0–23, <b>Europe/Stockholm</b>, not the machine's zone) at which the
    /// daily run fires. Default 03:00.</summary>
    public int DailyRunHourLocal { get; set; } = 3;

    /// <summary>
    /// Development seam: run the daily jobs once immediately at startup, then continue on the normal
    /// schedule. Off by default and never set in production.
    ///
    /// <para>Exists because the scheduled emails (the 14-day reminder, the completion nudge, the
    /// link-expiry warning, the pre-deletion warning) are otherwise unreachable in a dev inbox
    /// without either waiting for the next occurrence of <see cref="DailyRunHourLocal"/> — up to a
    /// day — or hand-editing dates in the database. Same shape as the existing <c>DemoSeed:Reset</c>
    /// and <c>E2ETestHooks</c> seams.</para>
    /// </summary>
    public bool RunOnStartup { get; set; }
}
