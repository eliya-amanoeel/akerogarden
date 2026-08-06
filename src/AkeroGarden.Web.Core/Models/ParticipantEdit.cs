using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Models;

/// <summary>
/// An admin's edits to one participant, applied post-submission in the panel (FR-48) without any
/// status change or guest reopen. Meals reference existing <see cref="MealPreference"/> rows by id
/// — the admin changes each day's option, not the set of days.
/// </summary>
public class ParticipantEdit
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public AgeCategory AgeCategory { get; set; }

    public bool AttendsBreakfast { get; set; }

    public List<DietaryRequirement> Dietary { get; set; } = [];

    public string? DietaryNotes { get; set; }

    /// <summary>Existing meal-preference rows with their (possibly changed) option choice.</summary>
    public List<MealEdit> Meals { get; set; } = [];
}

/// <summary>One existing meal preference's chosen option (admin correction, FR-48).</summary>
public class MealEdit
{
    public Guid Id { get; set; }

    public Guid MealOptionId { get; set; }
}
