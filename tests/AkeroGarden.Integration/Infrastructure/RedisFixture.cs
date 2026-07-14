using Testcontainers.Redis;

namespace AkeroGarden.Integration.Infrastructure;

/// <summary>
/// Spins up a real Redis in a container so the distributed rate limiter (NFR-04) is exercised
/// against the same server it uses in production, not a fake.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.StopAsync();
}
