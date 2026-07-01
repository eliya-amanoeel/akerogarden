using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AkeroGarden.Web.Services;

/// <summary>
/// Redis-backed fixed-window rate limiter for the inquiry endpoint (NFR-04). Uses a single Lua
/// script so the increment and the window's expiry are applied atomically — avoiding a key that
/// lingers without a TTL if the process dies between the two operations. Because the counter
/// lives in Redis (not in-memory), the limit holds correctly across multiple App Service
/// instances.
/// </summary>
public class RedisInquiryRateLimiter(IConnectionMultiplexer redis, IOptions<RateLimitOptions> options)
    : IInquiryRateLimiter
{
    private readonly RateLimitOptions _opts = options.Value;

    // INCR the counter; on the first hit in the window, set the expiry. Returns the new count.
    private const string IncrementScript =
        """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        return count
        """;

    public async Task<bool> TryAcquireAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var db = redis.GetDatabase();
        var key = _opts.KeyPrefix + clientId;

        var result = await db.ScriptEvaluateAsync(
            IncrementScript,
            [key],
            [_opts.WindowSeconds]);

        var count = (long)result;
        return count <= _opts.MaxRequests;
    }
}
