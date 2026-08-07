namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// Booking-level meal plan declared on the inquiry form (requirements.md §4.1, §8.1).
/// A booking is all-or-nothing on catering — there is no mixed plan and no per-participant
/// catering flag, because that value could only ever be a copy of this one, free to disagree
/// with it. Note (§4.2): day guests are only applicable for <see cref="FullBoardAll"/> — a
/// <see cref="SelfCateringAll"/> booking has no catered meals, so the inquiry form must not
/// show the day-guest field for it.
/// Persisted as a string for legibility and stability against reordering.
/// </summary>
public enum MealPlan
{
    /// <summary>All guests full-board — meals included for everyone.</summary>
    FullBoardAll,

    /// <summary>All guests self-catering — no meals provided.</summary>
    SelfCateringAll
}
