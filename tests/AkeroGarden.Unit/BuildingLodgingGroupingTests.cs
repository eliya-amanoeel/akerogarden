using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for <see cref="BuildingService.BuildLodgingOverview"/> — the pure grouping,
/// ordering and counting behind the public lodging listing on /faciliteter and the capacity the home
/// and about pages state (NFR-26, FR-81). Isolated from EF so it's testable without a database.
/// </summary>
public class BuildingLodgingGroupingTests
{
    private static PhysicalBuilding PhysicalBuilding(string name, int order) =>
        new() { Id = Guid.NewGuid(), Name = name, DisplayOrder = order };

    private static Building Building(PhysicalBuilding pb, string name, int order, params int[] roomBeds)
    {
        var building = new Building { Id = Guid.NewGuid(), PhysicalBuildingId = pb.Id, Name = name, DisplayOrder = order };
        for (var i = 0; i < roomBeds.Length; i++)
            building.Rooms.Add(new Room { Id = Guid.NewGuid(), BuildingId = building.Id, Name = $"Rum {i + 1}", BedCount = roomBeds[i], DisplayOrder = i + 1 });
        return building;
    }

    [Fact]
    public void A_physical_building_with_several_houses_is_a_group_under_its_own_name_in_display_order()
    {
        var stora = PhysicalBuilding("Stora bostadslängan", 3);
        var lilla = PhysicalBuilding("Lilla bostadslängan", 2);

        // Deliberately fed out of DisplayOrder to prove the result is sorted, not passed through.
        var buildings = new List<Building>
        {
            Building(stora, "Vätö 2", 6, 2),
            Building(lilla, "Singö", 3, 3),
            Building(stora, "Vätö 1", 5, 2),
            Building(lilla, "Blidö", 1, 4),
        };

        var overview = BuildingService.BuildLodgingOverview(buildings, [stora, lilla]);

        Assert.Equal(["Lilla bostadslängan", "Stora bostadslängan"], overview.Groups.Select(g => g.Name));
        Assert.Equal(["Blidö", "Singö"], overview.Groups[0].Houses.Select(h => h.Building.Name));
        Assert.Equal(["Vätö 1", "Vätö 2"], overview.Groups[1].Houses.Select(h => h.Building.Name));
    }

    [Fact]
    public void Houses_alone_in_their_physical_building_share_one_standalone_group_listed_last()
    {
        // Smedjan and Tallstugan: a heading per house would only repeat the house's own name.
        var lilla = PhysicalBuilding("Lilla bostadslängan", 2);
        var smedjan = PhysicalBuilding("Smedjan", 4);
        var tallstugan = PhysicalBuilding("Tallstugan", 5);
        var buildings = new List<Building>
        {
            Building(tallstugan, "Tallstugan", 8, 4),
            Building(lilla, "Blidö", 1, 4),
            Building(smedjan, "Smedjan", 7, 4),
            Building(lilla, "Ljusterö", 2, 3),
        };

        var overview = BuildingService.BuildLodgingOverview(buildings, [lilla, smedjan, tallstugan]);

        Assert.Equal(["Lilla bostadslängan", BuildingService.StandaloneGroupName], overview.Groups.Select(g => g.Name));
        Assert.Equal("Fristående stugor", BuildingService.StandaloneGroupName);
        // In physical-building order, so the admin's Visningsordning still decides who comes first.
        Assert.Equal(["Smedjan", "Tallstugan"], overview.Groups[1].Houses.Select(h => h.Building.Name));
    }

    [Fact]
    public void Rooms_within_a_house_are_ordered_by_DisplayOrder()
    {
        var pb = PhysicalBuilding("Smedjan", 1);
        var house = Building(pb, "Smedjan", 1);
        house.Rooms.Add(new Room { Name = "Rum B", BedCount = 2, DisplayOrder = 2 });
        house.Rooms.Add(new Room { Name = "Rum A", BedCount = 2, DisplayOrder = 1 });

        var overview = BuildingService.BuildLodgingOverview([house], [pb]);

        Assert.Equal(["Rum A", "Rum B"], overview.Groups.Single().Houses.Single().Rooms.Select(r => r.Name));
    }

    [Fact]
    public void A_house_with_no_rooms_is_not_listed_and_not_counted()
    {
        // A house nobody can sleep in is not lodging — it would show as "0 rum · 0 bäddar".
        var lilla = PhysicalBuilding("Lilla bostadslängan", 1);
        var buildings = new List<Building>
        {
            Building(lilla, "Blidö", 1, 4),
            Building(lilla, "Ljusterö", 2, 3),
            Building(lilla, "Under arbete", 3),
        };

        var overview = BuildingService.BuildLodgingOverview(buildings, [lilla]);

        Assert.DoesNotContain(overview.Groups.SelectMany(g => g.Houses), h => h.Building.Name == "Under arbete");
        Assert.Equal(2, overview.Capacity.Houses);
    }

    [Fact]
    public void Capacity_counts_the_listed_beds_houses_and_rooms()
    {
        // The real layout's shape: 79 beds in 8 houses and 28 rooms is what the public pages state.
        var lilla = PhysicalBuilding("Lilla bostadslängan", 2);
        var stora = PhysicalBuilding("Stora bostadslängan", 3);
        var smedjan = PhysicalBuilding("Smedjan", 4);
        var tallstugan = PhysicalBuilding("Tallstugan", 5);
        var buildings = new List<Building>
        {
            Building(lilla, "Blidö", 1, 4, 2, 3, 3),
            Building(lilla, "Ljusterö", 2, 3, 6, 4, 4),
            Building(lilla, "Singö", 3, 3, 5, 4, 4),
            Building(lilla, "Tjockö", 4, 3, 2, 3, 2),
            Building(stora, "Vätö 1", 5, 2, 2, 1, 1, 2),
            Building(stora, "Vätö 2", 6, 2, 1, 1, 2, 2),
            Building(smedjan, "Smedjan", 7, 4),
            Building(tallstugan, "Tallstugan", 8, 4),
        };

        var overview = BuildingService.BuildLodgingOverview(buildings, [lilla, stora, smedjan, tallstugan]);

        Assert.Equal(new LodgingCapacity(Beds: 79, Houses: 8, Rooms: 28), overview.Capacity);
        Assert.Equal(55, overview.Groups[0].Beds);
        Assert.Equal(17, overview.Groups[0].Houses[1].Beds);
    }

    [Fact]
    public void Houses_under_an_inactive_physical_building_stay_on_the_page()
    {
        // Simulates the real caller: only active physical buildings are passed, so a deactivated one
        // is absent even though its houses are still active. They must not silently disappear.
        var active = PhysicalBuilding("Lilla bostadslängan", 1);
        var deactivated = PhysicalBuilding("Stora bostadslängan", 0);
        var buildings = new List<Building>
        {
            Building(active, "Blidö", 1, 4),
            Building(active, "Ljusterö", 2, 3),
            Building(deactivated, "Vätö 1", 5, 2),
            Building(deactivated, "Vätö 2", 6, 2),
        };

        var overview = BuildingService.BuildLodgingOverview(buildings, [active]);

        Assert.Equal(["Lilla bostadslängan", "Övrigt"], overview.Groups.Select(g => g.Name));
        Assert.Equal(4, overview.Capacity.Houses);
    }

    [Fact]
    public void No_houses_yields_no_groups_and_zero_capacity()
    {
        var overview = BuildingService.BuildLodgingOverview([], [PhysicalBuilding("Lilla bostadslängan", 1)]);

        Assert.Empty(overview.Groups);
        Assert.Equal(new LodgingCapacity(0, 0, 0), overview.Capacity);
    }
}
