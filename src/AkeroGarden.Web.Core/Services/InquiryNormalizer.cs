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
    /// Day guests only apply for <see cref="MealPlan.FullBoardAll"/> (§4.2) — a
    /// <see cref="MealPlan.SelfCateringAll"/> booking has no catered meals, so there is nothing for
    /// a day guest to attend.
    ///
    /// <para><b>The one place this rule lives.</b> It was previously restated in
    /// <see cref="BookingService"/>, here, and in the completion page, so a change to the meal plan
    /// enum had to be found in three places to stay correct. A null plan (a general inquiry) has no
    /// day guests either.</para>
    /// </summary>
    public static bool DayGuestsApplicable(MealPlan? plan) => plan == MealPlan.FullBoardAll;

    /// <summary>
    /// Discards a submitted day-guest count when day guests do not apply to the plan (§4.2). See
    /// <see cref="DayGuestsApplicable"/>.
    /// </summary>
    public static int? NormalizeDayGuestCount(MealPlan plan, int? dayGuestCount) =>
        DayGuestsApplicable(plan) ? dayGuestCount : null;

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
