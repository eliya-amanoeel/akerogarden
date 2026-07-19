using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for <see cref="BuildingService.GroupByPhysicalBuilding"/> — the pure
/// grouping/ordering logic behind the public facilities page's "Boende" section (NFR-26).
/// Isolated from EF so it's testable without a database.
/// </summary>
public class BuildingLodgingGroupingTests
{
    private static PhysicalBuilding PhysicalBuilding(string name, int order) =>
        new() { Id = Guid.NewGuid(), Name = name, DisplayOrder = order };

    private static Building Building(PhysicalBuilding pb, string name, int order) =>
        new() { Id = Guid.NewGuid(), PhysicalBuildingId = pb.Id, Name = name, DisplayOrder = order };

    [Fact]
    public void Groups_are_ordered_by_PhysicalBuilding_DisplayOrder_not_encounter_order()
    {
        var stora = PhysicalBuilding("Stora bostadslängan", 3);
        var lilla = PhysicalBuilding("Lilla bostadslängan", 2);
        var smedjan = PhysicalBuilding("Smedjan", 4);

        // Deliberately fed out of DisplayOrder to prove the result is sorted, not passed through.
        var buildings = new List<Building>
        {
            Building(stora, "Vätö 1", 5),
            Building(smedjan, "Smedjan", 7),
            Building(lilla, "Blidö", 1),
            Building(lilla, "Ljusterö", 2),
        };

        var groups = BuildingService.GroupByPhysicalBuilding(buildings, [stora, lilla, smedjan]);

        Assert.Equal(["Lilla bostadslängan", "Stora bostadslängan", "Smedjan"],
            groups.Select(g => g.PhysicalBuildingName));
    }

    [Fact]
    public void Buildings_within_a_group_are_ordered_by_DisplayOrder()
    {
        var lilla = PhysicalBuilding("Lilla bostadslängan", 1);
        var buildings = new List<Building>
        {
            Building(lilla, "Tjockö", 4),
            Building(lilla, "Blidö", 1),
            Building(lilla, "Singö", 3),
        };

        var groups = BuildingService.GroupByPhysicalBuilding(buildings, [lilla]);

        Assert.Equal(["Blidö", "Singö", "Tjockö"], groups.Single().Buildings.Select(b => b.Name));
    }

    [Fact]
    public void Building_under_a_deactivated_PhysicalBuilding_falls_into_Ovrigt_sorted_last()
    {
        var active = PhysicalBuilding("Lilla bostadslängan", 1);
        var deactivated = PhysicalBuilding("Smedjan", 0); // lower DisplayOrder, but excluded from the active list
        var orphan = Building(deactivated, "Smedjan", 1);
        var buildings = new List<Building> { orphan, Building(active, "Blidö", 1) };

        // Simulates the real caller: physicalBuildings comes from PhysicalBuildingService.GetActiveAsync(),
        // so a deactivated physical building is absent even though its building is still active.
        var groups = BuildingService.GroupByPhysicalBuilding(buildings, [active]);

        Assert.Equal(["Lilla bostadslängan", "Övrigt"], groups.Select(g => g.PhysicalBuildingName));
        Assert.Equal([orphan], groups.Last().Buildings);
    }

    [Fact]
    public void Empty_building_list_yields_no_groups()
    {
        Assert.Empty(BuildingService.GroupByPhysicalBuilding([], [PhysicalBuilding("Lilla bostadslängan", 1)]));
    }
}
