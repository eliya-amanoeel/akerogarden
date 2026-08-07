namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// The whole group's meal choice for one sitting (requirements.md §8.6) — one row per
/// (booking, date, lunch|dinner), drawn from the active admin-managed meal list. Which sittings
/// are collected follows the resolved meal occasions of the booking (§4.1), computed by
/// <see cref="AkeroGarden.Web.Core.Services.MealOccasionResolver"/>.
///
/// <para><b>Per booking, not per person (FR-105).</b> This was <c>MealPreference</c>, hanging off
/// <see cref="Participant"/> with one row per person per meal. Nobody chooses individually any
/// more: the group picks one option per sitting and everybody — including day guests — eats it.
/// The name changed with the parent because "preference" stopped being true. Individual needs are
/// met through <see cref="Participant.Allergies"/> and the booking's dietary-preference counts
/// instead (§4.6).</para>
///
/// <para><b>Snapshot pattern (NFR-15).</b> <see cref="MealOptionId"/> is the live link (and blocks
/// hard-deleting a referenced meal option, FR-26); <see cref="MealOptionSnapshot"/> preserves the
/// option name agreed at submission so history survives later edits to the MealOption record.</para>
/// </summary>
public class BookingMealSelection
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }

    public Booking? Booking { get; set; }

    /// <summary>The specific day this selection applies to (§8.6).</summary>
    public DateOnly Date { get; set; }

    public MealType MealType { get; set; }

    /// <summary>Live FK → <see cref="MealOption"/> (§8.6). Powers queries and blocks hard-deleting
    /// the option while referenced (FR-26).</summary>
    public Guid MealOptionId { get; set; }

    public MealOption? MealOption { get; set; }

    /// <summary>Meal option name captured at submission for historical accuracy (NFR-15).</summary>
    public string MealOptionSnapshot { get; set; } = string.Empty;
}
