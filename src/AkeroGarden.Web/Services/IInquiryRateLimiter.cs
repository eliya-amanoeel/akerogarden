namespace AkeroGarden.Web.Services;

/// <summary>
/// Rate limiter for the public inquiry endpoint (NFR-04). An infrastructure seam (backed by
/// Redis) so it can be substituted in tests; the limit is distributed so it holds even if the
/// App Service scales to multiple instances.
/// </summary>
public interface IInquiryRateLimiter
{
    /// <summary>
    /// Records one attempt for <paramref name="clientId"/> and returns whether it is within the
    /// allowed limit. Returns <c>true</c> when the submission may proceed, <c>false</c> when the
    /// client has exceeded the limit for the current window.
    /// </summary>
    Task<bool> TryAcquireAsync(string clientId, CancellationToken cancellationToken = default);
}
