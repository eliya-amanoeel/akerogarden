using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// The no-op rate limiter (used when Redis can't be initialised) must always allow — the public
/// inquiry form fails open rather than being taken down by a Redis problem.
/// </summary>
public class NoOpInquiryRateLimiterTests
{
    [Fact]
    public async Task TryAcquire_AlwaysAllows()
    {
        var limiter = new NoOpInquiryRateLimiter();
        Assert.True(await limiter.TryAcquireAsync("1.2.3.4"));
        Assert.True(await limiter.TryAcquireAsync("1.2.3.4"));
    }
}
