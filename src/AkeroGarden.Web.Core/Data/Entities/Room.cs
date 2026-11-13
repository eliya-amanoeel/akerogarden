using System.ComponentModel.DataAnnotations;

namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// A room within a building (requirements.md §8.3). Capacity data — never an assignment target — and
/// shown publicly on /faciliteter with its image, name and bed setup (FR-81).
/// Reference data — never hard-deleted while referenced by a booking (FR-20); deactivate instead (FR-19).
/// </summary>
public class Room
{
    public Guid Id { get; set; }

    public Guid BuildingId { get; set; }

    public Building? Building { get; set; }

    [Required(ErrorMessage = "Namn krävs.")]
    [StringLength(200, ErrorMessage = "Namnet får vara högst 200 tecken.")]
    public string Name { get; set; } = string.Empty;

    [Range(1, 100, ErrorMessage = "Antal bäddar måste vara mellan 1 och 100.")]
    public int BedCount { get; set; }

    [StringLength(500, ErrorMessage = "Bäddkonfigurationen får vara högst 500 tecken.")]
    public string? BedConfiguration { get; set; }

    /// <summary>Name of the uploaded image (<see cref="Services.ImagePath"/>) — the stored blob's name,
    /// never a URL; the page renders it from <c>/bilder/{name}</c>. Null until one is uploaded.</summary>
    [StringLength(64)]
    public string? ImageName { get; set; }

    public bool IsActive { get; set; } = true;

    [Range(0, int.MaxValue, ErrorMessage = "Visningsordningen kan inte vara negativ.")]
    public int DisplayOrder { get; set; }
}
