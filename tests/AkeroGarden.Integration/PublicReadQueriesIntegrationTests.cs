using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the read-only queries that back the public content pages
/// (FR-81 facilities and lodging, FR-82 prices + cleaning, FR-83 menu) against
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
            s.AddScoped<BuildingService>();
            s.AddScoped<PhysicalBuildingService>();
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
    public async Task Lodging_overview_lists_active_houses_with_their_active_rooms_in_display_order()
    {
        // The query half of /faciliteter's Boende section (FR-81); the grouping half is unit-tested in
        // BuildingLodgingGroupingTests. Proves what only the database can: inactive houses and rooms
        // are filtered out by the query, not by the page.
        var buildingService = NewService<BuildingService>(out var scope);
        using var _s = scope;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var prefix = $"Bldg-{Guid.NewGuid():N}-";

        var pb = new PhysicalBuilding { Id = Guid.NewGuid(), Name = $"{prefix}Pb" };
        db.PhysicalBuildings.Add(pb);
        Building House(string name, int order, bool active = true) =>
            new() { Id = Guid.NewGuid(), Name = $"{prefix}{name}", PhysicalBuildingId = pb.Id, DisplayOrder = order, IsActive = active };
        Room RoomIn(Building b, string name, int order, bool active = true) =>
            new() { Id = Guid.NewGuid(), BuildingId = b.Id, Name = name, BedCount = 2, DisplayOrder = order, IsActive = active };

        var a = House("A", 1);
        var b = House("B", 2);
        var inactive = House("Z", 3, active: false);
        var onlyInactiveRooms = House("W", 4);
        db.Buildings.AddRange(a, b, inactive, onlyInactiveRooms);
        db.Rooms.AddRange(
            RoomIn(a, "Rum 2", 2), RoomIn(a, "Rum 1", 1), RoomIn(a, "Rum 3", 3, active: false),
            RoomIn(b, "Rum 1", 1),
            RoomIn(inactive, "Rum 1", 1),
            RoomIn(onlyInactiveRooms, "Rum 1", 1, active: false));
        await db.SaveChangesAsync();

        var overview = await buildingService.GetLodgingOverviewAsync();

        var group = Assert.Single(overview.Groups, g => g.Name == pb.Name);
        Assert.Equal([$"{prefix}A", $"{prefix}B"], group.Houses.Select(h => h.Building.Name));
        Assert.Equal(["Rum 1", "Rum 2"], group.Houses[0].Rooms.Select(r => r.Name));
        Assert.DoesNotContain(overview.Groups.SelectMany(g => g.Houses),
            h => h.Building.Name == $"{prefix}Z" || h.Building.Name == $"{prefix}W");
    }

    [Fact]
    public async Task Meals_GetActive_ExcludesInactive_AndOrdersAsOneMenu()
    {
        var service = NewService<MealOptionService>(out var scope);
        using var _s = scope;
        var prefix = $"Meal-{Guid.NewGuid():N}-";

        // One menu (§4.5): display order is one sequence across every dish, then name breaks ties.
        await service.CreateAsync(new MealOption { Name = $"{prefix}C", DisplayOrder = 2 });
        await service.CreateAsync(new MealOption { Name = $"{prefix}B", DisplayOrder = 1 });
        await service.CreateAsync(new MealOption { Name = $"{prefix}A", DisplayOrder = 1 });
        var inactive = new MealOption { Name = $"{prefix}X", DisplayOrder = 0 };
        await service.CreateAsync(inactive);
        await service.SetActiveAsync(inactive.Id, false);

        var mine = (await service.GetActiveAsync())
            .Where(m => m.Name.StartsWith(prefix)).ToList();

        Assert.Equal([$"{prefix}A", $"{prefix}B", $"{prefix}C"], mine.Select(m => m.Name));
        Assert.DoesNotContain(mine, m => m.Name == $"{prefix}X");
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
