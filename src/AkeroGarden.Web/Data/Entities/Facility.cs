using System.ComponentModel.DataAnnotations;

namespace AkeroGarden.Web.Data.Entities;

/// <summary>
/// A bookable facility shown on the public facilities page (FR-81) and selectable on the
/// inquiry form (requirements.md §8.8, FR-27–30). Reference data — deactivate instead of
/// deleting (FR-29); never hard-deleted while referenced by a booking (FR-30).
/// </summary>
public class Facility
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Namn krävs.")]
    [StringLength(200, ErrorMessage = "Namnet får vara högst 200 tecken.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "Beskrivningen får vara högst 2000 tecken.")]
    public string? Description { get; set; }

    /// <summary>Azure Blob Storage URL of the facility image; null until one is uploaded.</summary>
    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }
}
