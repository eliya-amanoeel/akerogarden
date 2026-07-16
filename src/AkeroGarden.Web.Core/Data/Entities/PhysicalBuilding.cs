using System.ComponentModel.DataAnnotations;

namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// A physical building on the property (requirements.md §8.14) — Huvudbyggnad, Lilla/Stora
/// bostadslängan, Konferensbyggnad, Smedjan, Tallstugan. The level cleaning is priced at
/// (§5.5) and the future level at which a whole building can be blocked in one action.
/// Reference data — never hard-deleted while referenced (FR-16 pattern); deactivate instead.
/// Some physical buildings hold lodging <see cref="Building"/> units, others do not
/// (Huvudbyggnad = matsal/shared; Konferensbyggnad = conference, linked via <see cref="Facility"/>).
/// </summary>
public class PhysicalBuilding
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Namn krävs.")]
    [StringLength(200, ErrorMessage = "Namnet får vara högst 200 tecken.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "Beskrivningen får vara högst 2000 tecken.")]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    [Range(0, int.MaxValue, ErrorMessage = "Visningsordningen kan inte vara negativ.")]
    public int DisplayOrder { get; set; }
}
