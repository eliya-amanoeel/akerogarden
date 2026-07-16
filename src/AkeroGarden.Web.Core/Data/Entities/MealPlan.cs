namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// Booking-level meal plan declared on the inquiry form (requirements.md §4.1, §8.1).
/// Determines the default <c>IsFullBoard</c> value for every participant on the completion
/// form. Note (§4.2): day guests are only applicable for <see cref="FullBoardAll"/> or
/// <see cref="Mixed"/> — a <see cref="SelfCateringAll"/> booking has no catered meals, so
/// the inquiry form must not show the day-guest field for it.
/// Persisted as a string for legibility and stability against reordering.
/// </summary>
public enum MealPlan
{
    /// <summary>All guests full-board — meals included for everyone.</summary>
    FullBoardAll,

    /// <summary>All guests self-catering — no meals provided.</summary>
    SelfCateringAll,

    /// <summary>Mix of full-board and self-catering; set per participant on the completion form.</summary>
    Mixed
}
