using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the pure hard-delete guard decisions (NFR-26, FR-16/FR-20).
/// The decision is isolated from EF so it is testable without a database; the
/// real reference-counting queries are exercised in the integration suite.
/// </summary>
public class ReferenceDataGuardTests
{
    [Fact]
    public void Building_CanHardDelete_True_WhenNoReferences()
    {
        Assert.True(BuildingService.CanHardDelete(0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Building_CanHardDelete_False_WhenReferenced(int referenceCount)
    {
        Assert.False(BuildingService.CanHardDelete(referenceCount));
    }

    [Fact]
    public void Room_CanHardDelete_True_WhenNoReferences()
    {
        Assert.True(RoomService.CanHardDelete(0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Room_CanHardDelete_False_WhenReferenced(int referenceCount)
    {
        Assert.False(RoomService.CanHardDelete(referenceCount));
    }

    [Fact]
    public void MealOption_CanHardDelete_True_WhenNoReferences()
    {
        Assert.True(MealOptionService.CanHardDelete(0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void MealOption_CanHardDelete_False_WhenReferenced(int referenceCount)
    {
        Assert.False(MealOptionService.CanHardDelete(referenceCount));
    }

    [Fact]
    public void NewBuilding_IsActiveByDefault()
    {
        Assert.True(new Building().IsActive);
    }

    [Fact]
    public void NewRoom_IsActiveByDefault()
    {
        Assert.True(new Room().IsActive);
    }

    [Fact]
    public void NewMealOption_IsActiveByDefault()
    {
        Assert.True(new MealOption().IsActive);
    }
}
