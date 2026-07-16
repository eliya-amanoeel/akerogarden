namespace AkeroGarden.Web.Core.Services;

/// <summary>Configuration for the daily background-job runner (config section <c>Jobs</c>).</summary>
public class JobOptions
{
    /// <summary>Master switch — when false, <see cref="DailyJobRunner"/> does not schedule any run
    /// (used to keep local/dev quiet or disable the loop in specific environments).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Hour of day (0–23, Europe/Stockholm) at which the daily run fires. Default 03:00.</summary>
    public int DailyRunHourLocal { get; set; } = 3;
}
