using System.ComponentModel.DataAnnotations;
using AkeroGarden.Web.Core.Data.Entities;

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

    // Assert the SPECIFIC rule fired — same member + message the entity raises — not merely that
    // "some" validation error exists. Each case starts from a well-formed block and breaks exactly
    // one thing, so a bare NotEmpty would still pass if the wrong rule fired or the intended rule
    // silently stopped firing while another did. Pinning member + message closes that.
    private static void AssertHasError(BlockedPeriod block, string member, string message) =>
        Assert.Contains(Validate(block), r => r.MemberNames.Contains(member) && r.ErrorMessage == message);

    [Fact]
    public void EndBeforeStart_IsInvalid()
    {
        var block = Valid(BlockScope.WholeProperty);
        block.EndDate = block.StartDate.AddDays(-1);
        AssertHasError(block, nameof(BlockedPeriod.EndDate), "Slutdatumet kan inte vara före startdatumet.");
    }

    [Fact]
    public void WholeProperty_WithBuilding_IsInvalid()
    {
        var block = Valid(BlockScope.WholeProperty);
        block.BuildingId = Guid.NewGuid();
        AssertHasError(block, nameof(BlockedPeriod.Scope),
            "En blockering för hela anläggningen kan inte ha en byggnad eller ett rum.");
    }

    [Fact]
    public void Building_WithoutBuildingId_IsInvalid()
    {
        var block = Valid(BlockScope.Building);
        block.BuildingId = null;
        AssertHasError(block, nameof(BlockedPeriod.BuildingId), "En byggnadsblockering måste ange en byggnad.");
    }

    [Fact]
    public void Building_WithRoomId_IsInvalid()
    {
        var block = Valid(BlockScope.Building);
        block.RoomId = Guid.NewGuid();
        AssertHasError(block, nameof(BlockedPeriod.RoomId), "En byggnadsblockering kan inte ange ett rum.");
    }

    [Fact]
    public void Room_WithoutRoomId_IsInvalid()
    {
        var block = Valid(BlockScope.Room);
        block.RoomId = null;
        AssertHasError(block, nameof(BlockedPeriod.RoomId), "En rumsblockering måste ange både byggnad och rum.");
    }
}
