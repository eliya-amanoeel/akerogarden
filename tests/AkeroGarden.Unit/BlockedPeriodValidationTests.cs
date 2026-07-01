using System.ComponentModel.DataAnnotations;
using AkeroGarden.Web.Data.Entities;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the scope/date consistency rules on <see cref="BlockedPeriod"/> (§3.3/§8.10,
/// NFR-26): scope must match which of BuildingId/RoomId are set, and the range must be ordered.
/// </summary>
public class BlockedPeriodValidationTests
{
    private static List<ValidationResult> Validate(BlockedPeriod block)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(block, new ValidationContext(block), results, validateAllProperties: true);
        return results;
    }

    private static BlockedPeriod Valid(BlockScope scope) => new()
    {
        Scope = scope,
        BuildingId = scope is BlockScope.Building or BlockScope.Room ? Guid.NewGuid() : null,
        RoomId = scope == BlockScope.Room ? Guid.NewGuid() : null,
        StartDate = new DateOnly(2026, 6, 1),
        EndDate = new DateOnly(2026, 6, 5)
    };

    [Theory]
    [InlineData(BlockScope.WholeProperty)]
    [InlineData(BlockScope.Building)]
    [InlineData(BlockScope.Room)]
    public void WellFormedBlock_PassesValidation(BlockScope scope)
    {
        Assert.Empty(Validate(Valid(scope)));
    }

    [Fact]
    public void EndBeforeStart_IsInvalid()
    {
        var block = Valid(BlockScope.WholeProperty);
        block.EndDate = block.StartDate.AddDays(-1);
        Assert.NotEmpty(Validate(block));
    }

    [Fact]
    public void WholeProperty_WithBuilding_IsInvalid()
    {
        var block = Valid(BlockScope.WholeProperty);
        block.BuildingId = Guid.NewGuid();
        Assert.NotEmpty(Validate(block));
    }

    [Fact]
    public void Building_WithoutBuildingId_IsInvalid()
    {
        var block = Valid(BlockScope.Building);
        block.BuildingId = null;
        Assert.NotEmpty(Validate(block));
    }

    [Fact]
    public void Building_WithRoomId_IsInvalid()
    {
        var block = Valid(BlockScope.Building);
        block.RoomId = Guid.NewGuid();
        Assert.NotEmpty(Validate(block));
    }

    [Fact]
    public void Room_WithoutRoomId_IsInvalid()
    {
        var block = Valid(BlockScope.Room);
        block.RoomId = null;
        Assert.NotEmpty(Validate(block));
    }
}
