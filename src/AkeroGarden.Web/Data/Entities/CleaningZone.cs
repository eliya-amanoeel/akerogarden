using System.ComponentModel.DataAnnotations;

namespace AkeroGarden.Web.Data.Entities;

/// <summary>
/// A cleaning package selectable on the inquiry form (requirements.md §8.11, FR-85–88).
/// Reference data — deactivate instead of deleting (FR-87); never hard-deleted while
/// referenced by a booking's cleaning selection (FR-88). Amounts are reference figures
/// only — the system computes no totals (C8).
/// A zone whose <see cref="IncludesZoneIds"/> is non-empty is a bundle (e.g. Hela lägret)
/// that covers the listed zones; selecting it later disables those zones on the inquiry form.
/// </summary>
public class CleaningZone
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Namn krävs.")]
    [StringLength(200, ErrorMessage = "Namnet får vara högst 200 tecken.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000, ErrorMessage = "Beskrivningen får vara högst 2000 tecken.")]
    public string? Description { get; set; }

    [Range(0, 1_000_000, ErrorMessage = "Beloppet måste vara mellan 0 och 1 000 000.")]
    public decimal AmountExVat { get; set; }

    // VAT stored as a fraction, e.g. 0.12 for 12 % (§8.11), mirroring PriceRule.
    [Range(0, 1, ErrorMessage = "Momssatsen anges som andel mellan 0 och 1 (t.ex. 0,12 för 12 %).")]
    public decimal VatRate { get; set; }

    /// <summary>
    /// Empty for a normal zone. For a bundle, the IDs of the zones it covers (self-reference,
    /// PostgreSQL uuid[]). Not a foreign key — bundle integrity is validated in the app layer.
    /// </summary>
    public List<Guid> IncludesZoneIds { get; set; } = [];

    public bool IsActive { get; set; } = true;

    [Range(0, int.MaxValue, ErrorMessage = "Visningsordningen kan inte vara negativ.")]
    public int DisplayOrder { get; set; }
}
