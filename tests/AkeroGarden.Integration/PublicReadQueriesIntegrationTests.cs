using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the read-only <c>GetActiveAsync</c> queries that back the
/// public content pages (FR-81 facilities, FR-82 prices + cleaning, FR-83 menu) against
/// real PostgreSQL: each must return active records only, in the public display order.
/// The fixture DB is shared across the class, so every test scopes its assertions to a
/// unique name prefix rather than assuming the table is otherwise empty.
/// </summary>
public class PublicReadQueriesIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public PublicReadQueriesIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private T NewService<T>(out IServiceScope scope) where T : notnull
    {
        var services = _fixture.BuildServices(s =>
        {
            s.AddScoped<FacilityService>();
            s.AddScoped<MealOptionService>();
            s.AddScoped<PriceRuleService>();
            s.AddScoped<CleaningZoneService>();
        });
        scope = services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<T>();
    }

    [Fact]
    public async Task Facilities_GetActive_ExcludesInactive_AndOrdersByDisplayOrder()
    {
        var service = NewService<FacilityService>(out var scope);
        using var _s = scope;
        var prefix = $"Fac-{Guid.NewGuid():N}-";

        await service.CreateAsync(new Facility { Name = $"{prefix}B", DisplayOrder = 2 });
        await service.CreateAsync(new Facility { Name = $"{prefix}A", DisplayOrder = 1 });
        var inactive = new Facility { Name = $"{prefix}Z", DisplayOrder = 3 };
        await service.CreateAsync(inactive);
        await service.SetActiveAsync(inactive.Id, false);

        var mine = (await service.GetActiveAsync())
            .Where(f => f.Name.StartsWith(prefix)).ToList();

        Assert.Equal([$"{prefix}A", $"{prefix}B"], mine.Select(f => f.Name));
        Assert.DoesNotContain(mine, f => f.Name == $"{prefix}Z");
    }

    [Fact]
    public async Task Meals_GetActive_ExcludesInactive_AndOrdersByTypeThenDisplayOrder()
    {
        var service = NewService<MealOptionService>(out var scope);
        using var _s = scope;
        var prefix = $"Meal-{Guid.NewGuid():N}-";

        await service.CreateAsync(new MealOption { Name = $"{prefix}Dinner1", MealType = MealType.Dinner, DisplayOrder = 1 });
        await service.CreateAsync(new MealOption { Name = $"{prefix}Lunch2", MealType = MealType.Lunch, DisplayOrder = 2 });
        await service.CreateAsync(new MealOption { Name = $"{prefix}Lunch1", MealType = MealType.Lunch, DisplayOrder = 1 });
        var inactive = new MealOption { Name = $"{prefix}LunchX", MealType = MealType.Lunch, DisplayOrder = 0 };
        await service.CreateAsync(inactive);
        await service.SetActiveAsync(inactive.Id, false);

        var mine = (await service.GetActiveAsync())
            .Where(m => m.Name.StartsWith(prefix)).ToList();

        // MealType is persisted as a string, so OrderBy(MealType) sorts lexically:
        // "Dinner" < "Lunch". Within a type, display order then applies. The menu page
        // renders Lunch/Dinner in explicit sections, so this cross-type order is
        // presentation-irrelevant; the guarantees under test are active-only + within-type order.
        Assert.Equal([$"{prefix}Dinner1", $"{prefix}Lunch1", $"{prefix}Lunch2"], mine.Select(m => m.Name));
        Assert.DoesNotContain(mine, m => m.Name == $"{prefix}LunchX");
    }

    [Fact]
    public async Task Prices_GetActive_ExcludesInactive_AndOrdersByLabel()
    {
        var service = NewService<PriceRuleService>(out var scope);
        using var _s = scope;
        var prefix = $"Price-{Guid.NewGuid():N}-";

        await service.CreateAsync(new PriceRule { Label = $"{prefix}Beta", AmountExVat = 100, VatRate = 0.12m, Unit = "natt" });
        await service.CreateAsync(new PriceRule { Label = $"{prefix}Alfa", AmountExVat = 200, VatRate = 0.25m, Unit = "natt" });
        var inactive = new PriceRule { Label = $"{prefix}Zeta", AmountExVat = 300, VatRate = 0.12m, Unit = "natt", IsActive = false };
        await service.CreateAsync(inactive);

        var mine = (await service.GetActiveAsync())
            .Where(p => p.Label.StartsWith(prefix)).ToList();

        Assert.Equal([$"{prefix}Alfa", $"{prefix}Beta"], mine.Select(p => p.Label));
    }

    [Fact]
    public async Task CleaningZones_GetActive_ExcludesInactive_AndOrdersByDisplayOrder()
    {
        var service = NewService<CleaningZoneService>(out var scope);
        using var _s = scope;
        var prefix = $"Zone-{Guid.NewGuid():N}-";

        await service.TryCreateAsync(new CleaningZone { Name = $"{prefix}B", AmountExVat = 500, VatRate = 0.25m, DisplayOrder = 2, Kind = CleaningZoneKind.Everything });
        await service.TryCreateAsync(new CleaningZone { Name = $"{prefix}A", AmountExVat = 400, VatRate = 0.25m, DisplayOrder = 1, Kind = CleaningZoneKind.Everything });
        var inactive = new CleaningZone { Name = $"{prefix}Z", AmountExVat = 600, VatRate = 0.25m, DisplayOrder = 3, Kind = CleaningZoneKind.Everything };
        await service.TryCreateAsync(inactive);
        await service.SetActiveAsync(inactive.Id, false);

        var mine = (await service.GetActiveAsync())
            .Where(z => z.Name.StartsWith(prefix)).ToList();

        Assert.Equal([$"{prefix}A", $"{prefix}B"], mine.Select(z => z.Name));
        Assert.DoesNotContain(mine, z => z.Name == $"{prefix}Z");
    }
}
