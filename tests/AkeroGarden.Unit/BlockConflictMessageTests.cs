using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// The refusal shown when a block would close a building a live booking has assigned (FR-102).
/// The Blockeringar page compares the service's error against this exact wording to decide whether
/// to list the bookings as links, so the wording is part of the contract, not decoration. Which
/// blocks are refused is <c>BlockedPeriodIntegrationTests</c>.
/// </summary>
public class BlockConflictMessageTests
{
    private static readonly BlockedPeriod BuildingBlock = new() { Scope = BlockScope.Building };
    private static readonly BlockedPeriod WholePropertyBlock = new() { Scope = BlockScope.WholeProperty };

    private static BlockAssignmentConflict Conflict(string reference) => new(Guid.NewGuid(), reference);

    [Fact]
    public void One_booking_on_a_building_names_the_building_and_the_booking()
    {
        var message = BlockedPeriodService.DescribeConflicts(BuildingBlock, "Ljusterö", [Conflict("AG-2026-0042")]);

        Assert.Equal(
            "Byggnaden Ljusterö är tilldelad AG-2026-0042 under perioden. "
                + "Avmarkera byggnaden på bokningen innan du blockerar den.",
            message);
    }

    [Fact]
    public void Several_bookings_are_listed_and_addressed_in_the_plural()
    {
        var message = BlockedPeriodService.DescribeConflicts(
            BuildingBlock, "Ljusterö", [Conflict("AG-2026-0042"), Conflict("AG-2026-0051")]);

        Assert.Equal(
            "Byggnaden Ljusterö är tilldelad AG-2026-0042, AG-2026-0051 under perioden. "
                + "Avmarkera byggnaden på bokningarna innan du blockerar den.",
            message);
    }

    [Fact]
    public void A_whole_property_block_says_so_and_offers_other_dates()
    {
        var message = BlockedPeriodService.DescribeConflicts(WholePropertyBlock, null, [Conflict("AG-2026-0042")]);

        Assert.Equal(
            "Hela anläggningen kan inte blockeras: AG-2026-0042 har byggnader tilldelade under perioden. "
                + "Avmarkera byggnaderna på bokningen eller välj andra datum.",
            message);
    }
}
