using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for Facility reference data against real PostgreSQL
/// (FR-27–30, NFR-14): create with image URL, soft-deactivate, and hard-delete
/// (no referents until Phase 3). Blob upload itself is an infrastructure adapter
/// (BlobImageStorage), verified in dev / Phase 6 — not exercised here.
/// Tests use unique names because the fixture's database is shared across the class.
/// </summary>
public class FacilityIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public FacilityIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private FacilityService NewService(out IServiceScope scope)
    {
        var services = _fixture.BuildServices(s => s.AddScoped<FacilityService>());
        scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<FacilityService>();
    }

    [Fact]
    public async Task Create_PersistsImageUrl_AndIsActive()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var url = $"http://127.0.0.1:10000/devstoreaccount1/facility-images/{Guid.NewGuid():N}.jpg";
        var facility = new Facility { Name = $"Kapellet-{Guid.NewGuid():N}", ImageUrl = url };
        await service.CreateAsync(facility);

        var reloaded = await service.GetAsync(facility.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(url, reloaded!.ImageUrl);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task Deactivate_KeepsRecord_AndFlipsFlag()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var facility = new Facility { Name = $"Spelrum-{Guid.NewGuid():N}" };
        await service.CreateAsync(facility);

        await service.SetActiveAsync(facility.Id, false);

        var reloaded = await service.GetAsync(facility.Id);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.IsActive);
    }

    [Fact]
    public async Task HardDelete_Succeeds_WhenUnreferenced()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var facility = new Facility { Name = $"Konferensrum-{Guid.NewGuid():N}" };
        await service.CreateAsync(facility);

        var result = await service.TryHardDeleteAsync(facility.Id);

        Assert.Equal(HardDeleteResult.Deleted, result);
        Assert.Null(await service.GetAsync(facility.Id));
    }
}
