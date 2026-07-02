namespace AkeroGarden.Web.Services;

/// <summary>
/// Fallback rate limiter used when the Redis multiplexer cannot be initialised at startup (e.g. a
/// malformed or unresolved connection string). It always allows: the public inquiry form must
/// never be taken down by a Redis problem, so we fail open (NFR-13 spirit). The outage is logged
/// once at startup where the fallback is chosen.
/// </summary>
public sealed class NoOpInquiryRateLimiter : IInquiryRateLimiter
{
    public Task<bool> TryAcquireAsync(string clientId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
