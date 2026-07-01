using System.ComponentModel.DataAnnotations;
using AkeroGarden.Web.Data.Entities;

namespace AkeroGarden.Unit;

/// <summary>
/// Regression coverage for the DisplayOrder ≥ 0 rule across the reference entities
/// that expose it. Guards against the [Range] attribute being dropped.
/// </summary>
public class DisplayOrderValidationTests
{
    private static bool IsValid(object entity) =>
        Validator.TryValidateObject(entity, new ValidationContext(entity), null, validateAllProperties: true);

    public static IEnumerable<object[]> Entities(int displayOrder) =>
    [
        [new Building { Name = "X", DisplayOrder = displayOrder }],
        [new Room { Name = "X", BedCount = 1, DisplayOrder = displayOrder }],
        [new MealOption { Name = "X", DisplayOrder = displayOrder }],
        [new Facility { Name = "X", DisplayOrder = displayOrder }],
        [new CleaningZone { Name = "X", DisplayOrder = displayOrder }],
    ];

    public static IEnumerable<object[]> NegativeDisplayOrder() => Entities(-1);
    public static IEnumerable<object[]> ZeroDisplayOrder() => Entities(0);

    [Theory]
    [MemberData(nameof(NegativeDisplayOrder))]
    public void NegativeDisplayOrder_IsInvalid(object entity)
    {
        Assert.False(IsValid(entity));
    }

    [Theory]
    [MemberData(nameof(ZeroDisplayOrder))]
    public void ZeroDisplayOrder_IsValid(object entity)
    {
        Assert.True(IsValid(entity));
    }
}
