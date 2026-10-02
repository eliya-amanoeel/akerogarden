using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// The pre-approval findings fall into two kinds that the booking panel presents differently:
/// <see cref="ApprovalWarning.BlocksApproval"/> (refused by ApproveAsync — red) and
/// <see cref="ApprovalWarning.HasAdvisory"/> (the admin may override — amber, under "Varningarna
/// stoppar inte godkännandet"). A finding counted on the wrong side is the defect this guards: it
/// was once one <c>Any</c>, and refusals were shown as advice. Which findings a real booking produces
/// is <c>ApprovalWarningIntegrationTests</c>.
/// </summary>
public class ApprovalWarningTests
{
    private static readonly ApprovalWarning Clear = ApprovalWarning.None;

    [Fact]
    public void Nothing_found_is_neither_kind()
    {
        Assert.False(Clear.BlocksApproval);
        Assert.False(Clear.HasAdvisory);
    }

    [Fact]
    public void No_building_assigned_blocks_and_is_not_advice()
    {
        var warning = Clear with { NoLodgingAssigned = true };

        Assert.True(warning.BlocksApproval);
        Assert.False(warning.HasAdvisory);
    }

    [Fact]
    public void Property_not_free_blocks_and_is_not_advice()
    {
        var warning = Clear with { AvailabilityConflict = "Datumen 10–12 oktober är redan upptagna." };

        Assert.True(warning.BlocksApproval);
        Assert.False(warning.HasAdvisory);
    }

    [Fact]
    public void Too_few_beds_in_the_assigned_buildings_is_advice_only()
    {
        var warning = Clear with { CapacityExceeded = true, RequestedGuests = 50, AvailableBeds = 40 };

        Assert.True(warning.HasAdvisory);
        Assert.False(warning.BlocksApproval);
    }

    [Fact]
    public void A_building_or_room_block_is_advice_only()
    {
        var warning = Clear with { BlockDescriptions = ["Byggnaden Blidö (2026-10-10–2026-10-12)"] };

        Assert.True(warning.HasAdvisory);
        Assert.False(warning.BlocksApproval);
    }

    [Fact]
    public void A_deviating_stated_time_is_advice_only()
    {
        var warning = Clear with { TimeDeviations = ["Incheckning 08:00 avviker från standard 14:00."] };

        Assert.True(warning.HasAdvisory);
        Assert.False(warning.BlocksApproval);
    }

    [Fact]
    public void Both_kinds_can_be_present_at_once()
    {
        var warning = Clear with
        {
            NoLodgingAssigned = true,
            TimeDeviations = ["Utcheckning 20:00 avviker från standard 12:00."],
        };

        Assert.True(warning.BlocksApproval);
        Assert.True(warning.HasAdvisory);
    }
}
