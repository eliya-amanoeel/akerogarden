using System.ComponentModel.DataAnnotations;

namespace AkeroGarden.Web.Core.Data.Entities;

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

    /// <summary>The physical building (§8.14) this lodging unit sits in — several lodging units
    /// can share one physical building (e.g. Blidö/Ljusterö/Singö/Tjockö → Lilla bostadslängan).
    /// Drives cleaning-zone resolution (room → building → physical building → zone, FR-13).</summary>
    public Guid PhysicalBuildingId { get; set; }

    public PhysicalBuilding? PhysicalBuilding { get; set; }

    /// <summary>What the house's guests share — allrum, showers, toilets. The one text the public
    /// facilities page shows for a house (FR-81). There is no separate free-text description: it
    /// only ever restated the room count, which the page now derives from the rooms themselves.</summary>
    [StringLength(2000, ErrorMessage = "Beskrivningen av gemensamma utrymmen får vara högst 2000 tecken.")]
    public string? SharedFacilities { get; set; }

    /// <summary>Name of the uploaded image (<see cref="Services.ImagePath"/>) — the stored blob's name,
    /// never a URL; the page renders it from <c>/bilder/{name}</c>. Null until one is uploaded.</summary>
    [StringLength(64)]
    public string? ImageName { get; set; }

    public bool IsAccessible { get; set; }

    public bool IsActive { get; set; } = true;

    [Range(0, int.MaxValue, ErrorMessage = "Visningsordningen kan inte vara negativ.")]
    public int DisplayOrder { get; set; }

    public ICollection<Room> Rooms { get; set; } = new List<Room>();
}
