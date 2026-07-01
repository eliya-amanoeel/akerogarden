using AkeroGarden.Web.Data.Entities;

namespace AkeroGarden.Web.Services;

/// <summary>
/// Pure, server-authoritative normalisation of inquiry-form input (NFR-26). The public form
/// hides day guests when the meal plan is <see cref="MealPlan.SelfCateringAll"/> (§4.2) and
/// disables the cleaning zones a selected bundle already covers (§5.5), but that is
/// progressive-enhancement UI only — these rules re-apply here on the server regardless of what
/// the client submitted, so a crafted or JS-disabled request cannot bypass them.
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

    /// <summary>
    /// Resolves the effective cleaning-zone selection (§5.5): drops unknown/inactive IDs, then —
    /// because a bundle such as Hela lägret is mutually exclusive with the zones it covers —
    /// removes any individually selected zone that a selected bundle already covers. The bundle
    /// itself is kept.
    /// </summary>
    public static List<Guid> NormalizeCleaningSelection(
        IEnumerable<Guid>? selectedIds,
        IReadOnlyCollection<CleaningZone> activeZones)
    {
        if (selectedIds is null) return [];
        var byId = activeZones.ToDictionary(z => z.Id);
        var selected = selectedIds.Distinct().Where(byId.ContainsKey).ToList();

        // Every zone covered by any selected bundle.
        var covered = selected
            .SelectMany(id => byId[id].IncludesZoneIds)
            .ToHashSet();

        // Keep bundles and standalone zones; drop zones a selected bundle already covers.
        return selected.Where(id => !covered.Contains(id)).ToList();
    }
}
