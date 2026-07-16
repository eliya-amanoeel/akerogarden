using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for PhysicalBuilding reference data against real PostgreSQL (§8.14,
/// FR-94, NFR-14): create, soft-deactivate-without-data-loss, and the no-hard-delete-when-
/// referenced rule (a lodging Building pointing at it blocks the delete). Tests use unique
/// names because the fixture's database is shared across the class.
/// </summary>
public class PhysicalBuildingIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public PhysicalBuildingIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private (PhysicalBuildingService physical, BuildingService buildings) NewScope(out IServiceScope scope)
    {
        var services = _fixture.BuildServices(s =>
        {
            s.AddScoped<PhysicalBuildingService>();
            s.AddScoped<BuildingService>();
        });
        scope = services.CreateScope();
        return (
            scope.ServiceProvider.GetRequiredService<PhysicalBuildingService>(),
            scope.ServiceProvider.GetRequiredService<BuildingService>());
    }

    [Fact]
    public async Task CreatePhysicalBuilding_PersistsAndAppearsInList()
    {
        var (physical, _) = NewScope(out var scope);
        using var _s = scope;

        var name = $"Lilla bostadslängan-{Guid.NewGuid():N}";
        var pb = new PhysicalBuilding { Name = name, DisplayOrder = 1 };
        await physical.CreateAsync(pb);

        var all = await physical.GetAllAsync();
        Assert.Contains(all, p => p.Id == pb.Id && p.Name == name && p.IsActive);
    }

    [Fact]
    public async Task Deactivate_KeepsRecord_AndFlipsFlag()
    {
        var (physical, _) = NewScope(out var scope);
        using var _s = scope;

        var pb = new PhysicalBuilding { Name = $"Stora bostadslängan-{Guid.NewGuid():N}" };
        await physical.CreateAsync(pb);

        await physical.SetActiveAsync(pb.Id, false);

        var reloaded = await physical.GetAsync(pb.Id);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.IsActive);

        // Deactivated physical buildings still exist for GetAllAsync (admin can see + reactivate)
        // but drop out of GetActiveAsync (the FK picker on the Building form).
        Assert.Contains(await physical.GetAllAsync(), p => p.Id == pb.Id);
        Assert.DoesNotContain(await physical.GetActiveAsync(), p => p.Id == pb.Id);
    }

    [Fact]
    public async Task HardDelete_Blocked_WhenABuildingReferencesIt()
    {
        var (physical, buildings) = NewScope(out var scope);
        using var _s = scope;

        var pb = new PhysicalBuilding { Name = $"Smedjan-{Guid.NewGuid():N}" };
        await physical.CreateAsync(pb);
        await buildings.TryCreateAsync(new Building { Name = $"Smedjan-lodging-{Guid.NewGuid():N}", PhysicalBuildingId = pb.Id });

        var result = await physical.TryHardDeleteAsync(pb.Id);

        Assert.Equal(HardDeleteResult.Blocked, result);
        Assert.NotNull(await physical.GetAsync(pb.Id));
    }

    [Fact]
    public async Task HardDelete_Succeeds_WhenNothingReferencesIt()
    {
        var (physical, _) = NewScope(out var scope);
        using var _s = scope;

        var pb = new PhysicalBuilding { Name = $"Tallstugan-{Guid.NewGuid():N}" };
        await physical.CreateAsync(pb);

        var result = await physical.TryHardDeleteAsync(pb.Id);

        Assert.Equal(HardDeleteResult.Deleted, result);
        Assert.Null(await physical.GetAsync(pb.Id));
    }

    [Fact]
    public async Task HardDeleteBuilding_LeavesPhysicalBuildingDeletable()
    {
        var (physical, buildings) = NewScope(out var scope);
        using var _s = scope;

        var pb = new PhysicalBuilding { Name = $"Huvudbyggnad-{Guid.NewGuid():N}" };
        await physical.CreateAsync(pb);
        var buildingResult = await buildings.TryCreateAsync(
            new Building { Name = $"Huvudbyggnad-lodging-{Guid.NewGuid():N}", PhysicalBuildingId = pb.Id });
        Assert.True(buildingResult.Succeeded);

        var buildingId = (await buildings.GetAllAsync()).Single(b => b.PhysicalBuildingId == pb.Id).Id;
        await buildings.TryHardDeleteAsync(buildingId);

        var result = await physical.TryHardDeleteAsync(pb.Id);
        Assert.Equal(HardDeleteResult.Deleted, result);
    }
}
