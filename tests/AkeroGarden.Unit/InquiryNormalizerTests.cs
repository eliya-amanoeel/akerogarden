using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the server-authoritative inquiry normalisation (NFR-26): day guests are
/// dropped for self-catering (§4.2), regardless of what the client submits.
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
    public void NormalizeFacilitySelection_TreatsNullAsEmpty()
    {
        // An unchecked checkbox group binds to null on form post — must not throw.
        Assert.Empty(InquiryNormalizer.NormalizeFacilitySelection(null, []));
    }
}
