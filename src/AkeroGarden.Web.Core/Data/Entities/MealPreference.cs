namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// One meal selection for a participant on a specific day (requirements.md §8.6) — a lunch or a
/// dinner choice drawn from the active admin-managed meal list. Which days and meals are collected
/// follows the meals-by-day rule (§4.1), computed by
/// <see cref="AkeroGarden.Web.Core.Services.MealsByDayCalculator"/>.
///
/// <para><b>Snapshot pattern (NFR-15).</b> <see cref="MealOptionId"/> is the live link (and blocks
/// hard-deleting a referenced meal option, FR-26); <see cref="MealOptionSnapshot"/> preserves the
/// option name agreed at submission so history survives later edits to the MealOption record.</para>
/// </summary>
public class MealPreference
{
    public Guid Id { get; set; }

    public Guid ParticipantId { get; set; }

    public Participant? Participant { get; set; }

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
