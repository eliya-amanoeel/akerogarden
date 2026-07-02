using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Services;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the Redis-backed inquiry rate limiter against real Redis (NFR-04):
/// the 5-per-window limit holds, and clients are counted independently. A unique key prefix per
/// test isolates counters within the shared container.
/// </summary>
public class RateLimiterIntegrationTests : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private readonly RedisFixture _fixture;
    private ConnectionMultiplexer _redis = default!;

    public RateLimiterIntegrationTests(RedisFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() =>
        _redis = await ConnectionMultiplexer.ConnectAsync(_fixture.ConnectionString);

    public async Task DisposeAsync() => await _redis.DisposeAsync();

    private RedisInquiryRateLimiter NewLimiter(int max = 5) =>
        new(_redis, Options.Create(new RateLimitOptions
        {
            MaxRequests = max,
            WindowSeconds = 600,
            KeyPrefix = $"test:{Guid.NewGuid():N}:"
        }));

    [Fact]
    public async Task Allows_UpToLimit_ThenBlocks()
    {
        var limiter = NewLimiter(max: 5);
        const string ip = "203.0.113.7";

        for (var i = 1; i <= 5; i++)
            Assert.True(await limiter.TryAcquireAsync(ip), $"request {i} should be allowed");

        Assert.False(await limiter.TryAcquireAsync(ip)); // 6th blocked
        Assert.False(await limiter.TryAcquireAsync(ip)); // still blocked within the window
    }

    [Fact]
    public async Task CountsClientsIndependently()
    {
        var limiter = NewLimiter(max: 1);

        Assert.True(await limiter.TryAcquireAsync("198.51.100.1"));
        Assert.False(await limiter.TryAcquireAsync("198.51.100.1")); // this client exhausted
        Assert.True(await limiter.TryAcquireAsync("198.51.100.2"));  // a different client is unaffected
    }
}
