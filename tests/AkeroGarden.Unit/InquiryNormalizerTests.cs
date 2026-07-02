using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the server-authoritative inquiry normalisation (NFR-26): day guests are
/// dropped for self-catering (§4.2) and bundle-covered cleaning zones are dropped (§5.5),
/// regardless of what the client submits.
/// </summary>
public class InquiryNormalizerTests
{
    [Theory]
    [InlineData(MealPlan.FullBoardAll, 5, 5)]
    [InlineData(MealPlan.Mixed, 3, 3)]
    [InlineData(MealPlan.FullBoardAll, null, null)]
    public void NormalizeDayGuestCount_KeepsCount_WhenMealsApply(MealPlan plan, int? input, int? expected)
    {
        Assert.Equal(expected, InquiryNormalizer.NormalizeDayGuestCount(plan, input));
    }

    [Fact]
    public void NormalizeDayGuestCount_DropsCount_ForSelfCatering()
    {
        Assert.Null(InquiryNormalizer.NormalizeDayGuestCount(MealPlan.SelfCateringAll, 7));
    }

    [Fact]
    public void NormalizeFacilitySelection_KeepsOnlyActiveKnownIds_Deduped()
    {
        var f1 = new Facility { Id = Guid.NewGuid(), Name = "Kapellet", IsActive = true };
        var f2 = new Facility { Id = Guid.NewGuid(), Name = "Spelrum", IsActive = true };
        var active = new[] { f1, f2 };

        var result = InquiryNormalizer.NormalizeFacilitySelection(
            [f1.Id, f1.Id, Guid.NewGuid()], active);

        Assert.Equal([f1.Id], result);
    }

    [Fact]
    public void NormalizeSelections_TreatNullAsEmpty()
    {
        // An unchecked checkbox group binds to null on form post — must not throw.
        Assert.Empty(InquiryNormalizer.NormalizeFacilitySelection(null, []));
        Assert.Empty(InquiryNormalizer.NormalizeCleaningSelection(null, []));
    }

    [Fact]
    public void NormalizeCleaningSelection_NoBundle_KeepsValidZones()
    {
        var a = Zone("A");
        var b = Zone("B");
        var result = InquiryNormalizer.NormalizeCleaningSelection([a.Id, b.Id], [a, b]);
        Assert.Equal(new[] { a.Id, b.Id }.OrderBy(x => x), result.OrderBy(x => x));
    }

    [Fact]
    public void NormalizeCleaningSelection_BundleDropsCoveredZones_KeepsBundle()
    {
        var a = Zone("A");
        var b = Zone("B");
        var c = Zone("C");
        var bundle = Zone("Hela lägret", a.Id, b.Id, c.Id);

        var result = InquiryNormalizer.NormalizeCleaningSelection(
            [bundle.Id, a.Id, b.Id], [a, b, c, bundle]);

        Assert.Equal([bundle.Id], result);
    }

    [Fact]
    public void NormalizeCleaningSelection_FiltersUnknownIds()
    {
        var a = Zone("A");
        var result = InquiryNormalizer.NormalizeCleaningSelection([a.Id, Guid.NewGuid()], [a]);
        Assert.Equal([a.Id], result);
    }

    private static CleaningZone Zone(string name, params Guid[] includes) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        IsActive = true,
        IncludesZoneIds = includes.ToList()
    };
}
