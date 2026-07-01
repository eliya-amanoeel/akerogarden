using System.ComponentModel.DataAnnotations;

namespace AkeroGarden.Web.Data.Entities;

/// <summary>
/// A price rule shown read-only on the public prices page (FR-82) and on booking
/// records (FR-34) — requirements.md §8.9, FR-31–34. Standalone reference data with
/// no foreign keys: nothing references it, so there is no hard-delete guard.
/// Amounts are reference figures only — the system computes no totals (C8).
/// </summary>
public class PriceRule : IValidatableObject
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "Etikett krävs.")]
    [StringLength(200, ErrorMessage = "Etiketten får vara högst 200 tecken.")]
    public string Label { get; set; } = string.Empty;

    [Range(0, 1_000_000, ErrorMessage = "Beloppet måste vara mellan 0 och 1 000 000.")]
    public decimal AmountExVat { get; set; }

    // VAT stored as a fraction, e.g. 0.12 for 12 % (§8.9). Differs by category (§5).
    [Range(0, 1, ErrorMessage = "Momssatsen anges som andel mellan 0 och 1 (t.ex. 0,12 för 12 %).")]
    public decimal VatRate { get; set; }

    [Required(ErrorMessage = "Enhet krävs.")]
    [StringLength(100, ErrorMessage = "Enheten får vara högst 100 tecken.")]
    public string Unit { get; set; } = string.Empty;

    public DateOnly? SeasonStart { get; set; }

    public DateOnly? SeasonEnd { get; set; }

    public bool IsActive { get; set; } = true;

    [StringLength(2000, ErrorMessage = "Anteckningen får vara högst 2000 tecken.")]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SeasonStart is not null && SeasonEnd is not null && SeasonEnd < SeasonStart)
        {
            yield return new ValidationResult(
                "Säsongens slutdatum kan inte vara före startdatumet.",
                [nameof(SeasonEnd)]);
        }
    }
}
