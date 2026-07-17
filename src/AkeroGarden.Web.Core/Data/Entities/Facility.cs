using System.ComponentModel.DataAnnotations;

namespace AkeroGarden.Web.Core.Data.Entities;

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

    // Reference figure only — snapshotted onto BookingFacilitySelection at selection time
    // (NFR-34). No totals are computed from it (C8). Mirrors CleaningZone.
    [Range(0, 1_000_000, ErrorMessage = "Beloppet måste vara mellan 0 och 1 000 000.")]
    public decimal AmountExVat { get; set; }

    // VAT stored as a fraction, e.g. 0.25 for 25 % (§8.8), mirroring PriceRule/CleaningZone.
    [Range(0, 1, ErrorMessage = "Momssatsen anges som andel mellan 0 och 1 (t.ex. 0,25 för 25 %).")]
    public decimal VatRate { get; set; }

    /// <summary>Nullable FK → PhysicalBuilding (§8.14) — set only where booking the facility puts
    /// a physical building's cleaning in play (Konferensrum → Konferensbyggnad). Null for
    /// facilities with no associated cleaning building (Kapellet, pitches, Spelrum).</summary>
    public Guid? PhysicalBuildingId { get; set; }

    public PhysicalBuilding? PhysicalBuilding { get; set; }

    public bool IsActive { get; set; } = true;

    [Range(0, int.MaxValue, ErrorMessage = "Visningsordningen kan inte vara negativ.")]
    public int DisplayOrder { get; set; }
}
