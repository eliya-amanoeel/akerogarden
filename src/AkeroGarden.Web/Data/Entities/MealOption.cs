using System.ComponentModel.DataAnnotations;

namespace AkeroGarden.Web.Data.Entities;

/// <summary>
/// A selectable lunch or dinner option (requirements.md §8.7, FR-22–26).
/// Reference data — deactivate instead of deleting (FR-24); never hard-deleted
/// while referenced by a meal preference (FR-26).
/// </summary>
public class MealOption
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Namn krävs.")]
    [StringLength(200, ErrorMessage = "Namnet får vara högst 200 tecken.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "Beskrivningen får vara högst 2000 tecken.")]
    public string? Description { get; set; }

    public MealType MealType { get; set; }

    public bool IsVegetarian { get; set; }

    public bool IsActive { get; set; } = true;

    [Range(0, int.MaxValue, ErrorMessage = "Visningsordningen kan inte vara negativ.")]
    public int DisplayOrder { get; set; }
}
