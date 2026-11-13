using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for MealOption reference data against real PostgreSQL
/// (FR-22–26, NFR-14): create with the string-persisted diet enum, soft-deactivate-without-data-loss,
/// and hard-delete when nothing references the option.
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
    public async Task Create_PersistsDiet_AndDefaultsToActive()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var name = $"Linsgryta-{Guid.NewGuid():N}";
        var option = new MealOption { Name = name, Diet = DishDiet.Vegan };
        await service.CreateAsync(option);

        var reloaded = await service.GetAsync(option.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(DishDiet.Vegan, reloaded!.Diet);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task Update_ChangesDiet()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var option = new MealOption { Name = $"Nudlar-{Guid.NewGuid():N}", Diet = DishDiet.Vegetarian };
        await service.CreateAsync(option);

        await service.UpdateAsync(new MealOption { Id = option.Id, Name = option.Name, Diet = DishDiet.Vegan });

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reloaded = await db.MealOptions.AsNoTracking().SingleAsync(m => m.Id == option.Id);
        Assert.Equal(DishDiet.Vegan, reloaded.Diet);
    }

    [Fact]
    public async Task Diet_StoredAsStringInDatabase()
    {
        var service = NewService(out var scope);
        using var _s = scope;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var option = new MealOption { Name = $"Fisk-{Guid.NewGuid():N}" };
        await service.CreateAsync(option);

        // Read the raw column value to confirm the string conversion, not an int.
        // EF Core maps a scalar SqlQuery result from a column named "Value".
        var raw = await db.Database
            .SqlQuery<string>($"SELECT \"Diet\" AS \"Value\" FROM \"MealOptions\" WHERE \"Id\" = {option.Id}")
            .SingleAsync();
        Assert.Equal("None", raw);
    }

    [Fact]
    public async Task Deactivate_KeepsRecord_AndFlipsFlag()
    {
        var service = NewService(out var scope);
        using var _s = scope;

        var option = new MealOption { Name = $"Köttbullar-{Guid.NewGuid():N}" };
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

        var option = new MealOption { Name = $"Korv-{Guid.NewGuid():N}" };
        await service.CreateAsync(option);

        var result = await service.TryHardDeleteAsync(option.Id);

        Assert.Equal(HardDeleteResult.Deleted, result);
        Assert.Null(await service.GetAsync(option.Id));
    }
}
