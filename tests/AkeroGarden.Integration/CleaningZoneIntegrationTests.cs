using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for CleaningZone reference data against real PostgreSQL
/// (FR-85–88, NFR-14): create, the uuid[] bundle round-trip, bundle validation against
/// persisted zones, soft-deactivate, and hard-delete (no referents until Phase 3).
/// Tests use unique names because the fixture's database is shared across the class.
/// Zones use <see cref="CleaningZoneKind.Everything"/> throughout — the only Kind that needs no
/// PhysicalBuildingId (§8.11) — since these tests exercise CRUD/bundle mechanics, not
/// Kind/PhysicalBuilding applicability (covered separately).
/// </summary>
public class CleaningZoneIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public CleaningZoneIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private CleaningZoneService NewService(out IServiceScope scope)
    {
        var services = _fixture.BuildServices(s => s.AddScoped<CleaningZoneService>());
        scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<CleaningZoneService>();
    }

    [Fact]
    public async Task Create_RoundTripsAmountsAndIsActive()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var zone = new CleaningZone { Name = $"Huvudbyggnad-{Guid.NewGuid():N}", AmountExVat = 2000m, VatRate = 0.12m, Kind = CleaningZoneKind.Everything };
        var result = await service.TryCreateAsync(zone);
        Assert.True(result.Succeeded);

        var reloaded = await service.GetAsync(zone.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(2000m, reloaded!.AmountExVat);
        Assert.Empty(reloaded.IncludesZoneIds);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task CreateBundle_RoundTripsIncludedZoneIds()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var a = new CleaningZone { Name = $"Lilla-{Guid.NewGuid():N}", AmountExVat = 2000m, VatRate = 0.12m, Kind = CleaningZoneKind.Everything };
        var b = new CleaningZone { Name = $"Stora-{Guid.NewGuid():N}", AmountExVat = 2000m, VatRate = 0.12m, Kind = CleaningZoneKind.Everything };
        Assert.True((await service.TryCreateAsync(a)).Succeeded);
        Assert.True((await service.TryCreateAsync(b)).Succeeded);

        var bundle = new CleaningZone
        {
            Name = $"Hela lägret-{Guid.NewGuid():N}",
            AmountExVat = 8000m,
            VatRate = 0.12m,
            Kind = CleaningZoneKind.Everything,
            IncludesZoneIds = [a.Id, b.Id]
        };
        Assert.True((await service.TryCreateAsync(bundle)).Succeeded);

        var reloaded = await service.GetAsync(bundle.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded!.IncludesZoneIds.Count);
        Assert.Contains(a.Id, reloaded.IncludesZoneIds);
        Assert.Contains(b.Id, reloaded.IncludesZoneIds);
    }

    [Fact]
    public async Task CreateBundle_IncludingAnotherBundle_IsRejected()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var leaf = new CleaningZone { Name = $"Konferens-{Guid.NewGuid():N}", AmountExVat = 2000m, VatRate = 0.12m, Kind = CleaningZoneKind.Everything };
        Assert.True((await service.TryCreateAsync(leaf)).Succeeded);

        var bundle = new CleaningZone { Name = $"Bunt-{Guid.NewGuid():N}", AmountExVat = 4000m, VatRate = 0.12m, Kind = CleaningZoneKind.Everything, IncludesZoneIds = [leaf.Id] };
        Assert.True((await service.TryCreateAsync(bundle)).Succeeded);

        // A bundle that tries to include the existing bundle must be rejected.
        var nested = new CleaningZone { Name = $"Nästlad-{Guid.NewGuid():N}", AmountExVat = 9000m, VatRate = 0.12m, Kind = CleaningZoneKind.Everything, IncludesZoneIds = [bundle.Id] };
        var result = await service.TryCreateAsync(nested);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Null(await service.GetAsync(nested.Id));
    }

    [Fact]
    public async Task Deactivate_KeepsRecord_AndFlipsFlag()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var zone = new CleaningZone { Name = $"Zon-{Guid.NewGuid():N}", AmountExVat = 2000m, VatRate = 0.12m, Kind = CleaningZoneKind.Everything };
        Assert.True((await service.TryCreateAsync(zone)).Succeeded);

        await service.SetActiveAsync(zone.Id, false);

        var reloaded = await service.GetAsync(zone.Id);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.IsActive);
    }

    [Fact]
    public async Task HardDelete_Succeeds_WhenUnreferenced()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var zone = new CleaningZone { Name = $"Tillfällig-{Guid.NewGuid():N}", AmountExVat = 2000m, VatRate = 0.12m, Kind = CleaningZoneKind.Everything };
        Assert.True((await service.TryCreateAsync(zone)).Succeeded);

        var result = await service.TryHardDeleteAsync(zone.Id);

        Assert.Equal(HardDeleteResult.Deleted, result);
        Assert.Null(await service.GetAsync(zone.Id));
    }
}
