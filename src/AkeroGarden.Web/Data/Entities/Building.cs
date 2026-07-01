using System.ComponentModel.DataAnnotations;

namespace AkeroGarden.Web.Data.Entities;

/// <summary>
/// An accommodation building (requirements.md §8.2). Buildings contain rooms.
/// Reference data — never hard-deleted while referenced (FR-16); deactivate instead (FR-15).
/// </summary>
public class Building
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Namn krävs.")]
    [StringLength(200, ErrorMessage = "Namnet får vara högst 200 tecken.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "Beskrivningen får vara högst 2000 tecken.")]
    public string? Description { get; set; }

    [StringLength(2000, ErrorMessage = "Beskrivningen av gemensamma utrymmen får vara högst 2000 tecken.")]
    public string? SharedFacilities { get; set; }

    public bool IsAccessible { get; set; }

    public bool IsActive { get; set; } = true;

    [Range(0, int.MaxValue, ErrorMessage = "Visningsordningen kan inte vara negativ.")]
    public int DisplayOrder { get; set; }

    public ICollection<Room> Rooms { get; set; } = new List<Room>();
}
