using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the pure cleaning-zone bundle-integrity rule (NFR-26, FR-85):
/// a bundle may only include existing, active, non-bundle zones, and not itself.
/// </summary>
public class CleaningZoneBundleTests
{
    private static CleaningZone Zone(Guid id, bool active = true, params Guid[] includes) => new()
    {
        Id = id,
        IsActive = active,
        IncludesZoneIds = [.. includes]
    };

    [Fact]
    public void NormalZone_NoIncludes_IsValid()
    {
        var id = Guid.NewGuid();
        Assert.Null(CleaningZoneService.ValidateBundle(id, [], [Zone(id)]));
    }

    [Fact]
    public void Bundle_IncludingActiveSingleZones_IsValid()
    {
        var bundleId = Guid.NewGuid();
        var a = Zone(Guid.NewGuid());
        var b = Zone(Guid.NewGuid());

        var error = CleaningZoneService.ValidateBundle(bundleId, [a.Id, b.Id], [a, b]);

        Assert.Null(error);
    }

    // Each invalid case asserts the SPECIFIC rule's message, not merely that some error came back —
    // otherwise a regression that broke the intended rule while another fired (all four cases isolate
    // one violation from a valid baseline) would still go green.
    [Fact]
    public void Bundle_IncludingItself_IsInvalid()
    {
        var bundleId = Guid.NewGuid();
        var a = Zone(Guid.NewGuid());

        var error = CleaningZoneService.ValidateBundle(bundleId, [bundleId, a.Id], [a]);

        Assert.Equal("En städzon kan inte inkludera sig själv.", error);
    }

    [Fact]
    public void Bundle_IncludingUnknownZone_IsInvalid()
    {
        var bundleId = Guid.NewGuid();
        var error = CleaningZoneService.ValidateBundle(bundleId, [Guid.NewGuid()], []);
        Assert.Equal("En av de inkluderade städzonerna finns inte.", error);
    }

    [Fact]
    public void Bundle_IncludingInactiveZone_IsInvalid()
    {
        var bundleId = Guid.NewGuid();
        var inactive = Zone(Guid.NewGuid(), active: false);

        var error = CleaningZoneService.ValidateBundle(bundleId, [inactive.Id], [inactive]);

        Assert.Equal("En bunt kan endast inkludera aktiva städzoner.", error);
    }

    [Fact]
    public void Bundle_IncludingAnotherBundle_IsInvalid()
    {
        var bundleId = Guid.NewGuid();
        var nestedBundle = Zone(Guid.NewGuid(), includes: Guid.NewGuid());

        var error = CleaningZoneService.ValidateBundle(bundleId, [nestedBundle.Id], [nestedBundle]);

        Assert.Equal("En bunt kan inte inkludera en annan bunt.", error);
    }

    [Fact]
    public void CleaningZone_CanHardDelete_DecisionFollowsReferenceCount()
    {
        Assert.True(CleaningZoneService.CanHardDelete(0));
        Assert.False(CleaningZoneService.CanHardDelete(1));
    }

    [Fact]
    public void NewCleaningZone_IsActiveByDefault()
    {
        Assert.True(new CleaningZone().IsActive);
    }
}
