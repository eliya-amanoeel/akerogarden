using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Pure, server-authoritative normalisation of inquiry-form input (NFR-26). The public form
/// hides day guests when the meal plan is <see cref="MealPlan.SelfCateringAll"/> (§4.2), but that
/// is progressive-enhancement UI only — this rule re-applies here on the server regardless of
/// what the client submitted, so a crafted or JS-disabled request cannot bypass it.
/// </summary>
public static class InquiryNormalizer
{
    /// <summary>
    /// Day guests only apply for <see cref="MealPlan.FullBoardAll"/> or <see cref="MealPlan.Mixed"/>
    /// (§4.2). For <see cref="MealPlan.SelfCateringAll"/> there is no catered meal, so any submitted
    /// day-guest count is discarded (null).
    /// </summary>
    public static int? NormalizeDayGuestCount(MealPlan plan, int? dayGuestCount) =>
        plan == MealPlan.SelfCateringAll ? null : dayGuestCount;

    /// <summary>
    /// Keeps only the submitted facility IDs that correspond to a currently active facility —
    /// discarding unknown or deactivated IDs (NFR-14). Null input (an unchecked checkbox group
    /// binds to null on form post) is treated as an empty selection.
    /// </summary>
    public static List<Guid> NormalizeFacilitySelection(
        IEnumerable<Guid>? selectedIds,
        IReadOnlyCollection<Facility> activeFacilities)
    {
        if (selectedIds is null) return [];
        var activeIds = activeFacilities.Select(f => f.Id).ToHashSet();
        return selectedIds.Distinct().Where(activeIds.Contains).ToList();
    }
}
