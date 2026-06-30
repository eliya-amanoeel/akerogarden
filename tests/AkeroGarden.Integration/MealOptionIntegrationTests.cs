using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for MealOption reference data against real PostgreSQL
/// (FR-22–26, NFR-14): create with the string-persisted MealType enum,
/// soft-deactivate-without-data-loss, and hard-delete (no referents exist until Phase 4).
/// Tests use unique names because the fixture's database is shared across the class.
/// </summary>
public class MealOptionIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public MealOptionIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private MealOptionService NewService(out IServiceScope scope)
    {
        var services = _fixture.BuildServices(s => s.AddScoped<MealOptionService>());
        scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<MealOptionService>();
    }

    [Fact]
    public async Task Create_PersistsMealTypeAsString_AndAppearsInList()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var name = $"Pannkakor-{Guid.NewGuid():N}";
        var option = new MealOption { Name = name, MealType = MealType.Dinner, IsVegetarian = true };
        await service.CreateAsync(option);

        var reloaded = await service.GetAsync(option.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(MealType.Dinner, reloaded!.MealType);
        Assert.True(reloaded.IsVegetarian);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task MealType_StoredAsStringInDatabase()
    {
        var service = NewService(out var scope);
        using var _s = scope;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var option = new MealOption { Name = $"Fisk-{Guid.NewGuid():N}", MealType = MealType.Lunch };
        await service.CreateAsync(option);

        // Read the raw column value to confirm the string conversion, not an int.
        // EF Core maps a scalar SqlQuery result from a column named "Value".
        var raw = await db.Database
            .SqlQuery<string>($"SELECT \"MealType\" AS \"Value\" FROM \"MealOptions\" WHERE \"Id\" = {option.Id}")
            .SingleAsync();
        Assert.Equal("Lunch", raw);
    }

    [Fact]
    public async Task Deactivate_KeepsRecord_AndFlipsFlag()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var option = new MealOption { Name = $"Köttbullar-{Guid.NewGuid():N}", MealType = MealType.Lunch };
        await service.CreateAsync(option);

        await service.SetActiveAsync(option.Id, false);

        var reloaded = await service.GetAsync(option.Id);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.IsActive);
    }

    [Fact]
    public async Task HardDelete_Succeeds_WhenUnreferenced()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var option = new MealOption { Name = $"Korv-{Guid.NewGuid():N}", MealType = MealType.Dinner };
        await service.CreateAsync(option);

        var result = await service.TryHardDeleteAsync(option.Id);

        Assert.Equal(HardDeleteResult.Deleted, result);
        Assert.Null(await service.GetAsync(option.Id));
    }
}
