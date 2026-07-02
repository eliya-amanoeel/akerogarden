namespace AkeroGarden.Web.Services;

/// <summary>
/// Configuration for the inquiry-endpoint rate limit (NFR-04): at most
/// <see cref="MaxRequests"/> submissions per client per <see cref="WindowSeconds"/> window.
/// Bound from the "RateLimit" configuration section.
/// </summary>
public class RateLimitOptions
{
    /// <summary>Maximum submissions allowed per client within the window (NFR-04: 5).</summary>
    public int MaxRequests { get; set; } = 5;

    /// <summary>Length of the fixed window in seconds (NFR-04: 10 minutes = 600 s).</summary>
    public int WindowSeconds { get; set; } = 600;

    /// <summary>Redis key prefix for the per-client counters.</summary>
    public string KeyPrefix { get; set; } = "ratelimit:inquiry:";
}
