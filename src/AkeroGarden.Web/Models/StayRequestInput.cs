using System.ComponentModel.DataAnnotations;
using AkeroGarden.Web.Data.Entities;

namespace AkeroGarden.Web.Models;

/// <summary>
/// The public stay-request inquiry form (FR-03). Bound by the <c>/boka</c> page and passed to
/// <see cref="Services.BookingService"/>. Cross-field rules (dates, org details, privacy
/// acknowledgement) are validated here; the day-guest and cleaning-bundle rules are re-applied
/// server-side by <see cref="Services.InquiryNormalizer"/> regardless of client input.
/// </summary>
public class StayRequestInput : IValidatableObject
{
    [Required(ErrorMessage = "Namn krävs.")]
    [StringLength(200, ErrorMessage = "Namnet får vara högst 200 tecken.")]
    public string ContactName { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-postadress krävs.")]
    [EmailAddress(ErrorMessage = "Ange en giltig e-postadress.")]
    [StringLength(320, ErrorMessage = "E-postadressen får vara högst 320 tecken.")]
    public string ContactEmail { get; set; } = string.Empty;

    [OptionalPhone(ErrorMessage = "Ange ett giltigt telefonnummer.")]
    [StringLength(50, ErrorMessage = "Telefonnumret får vara högst 50 tecken.")]
    public string? ContactPhone { get; set; }

    [Required(ErrorMessage = "Incheckningsdatum krävs.")]
    public DateOnly? CheckIn { get; set; }

    [Required(ErrorMessage = "Utcheckningsdatum krävs.")]
    public DateOnly? CheckOut { get; set; }

    [Required(ErrorMessage = "Antal gäster krävs.")]
    [Range(1, 1000, ErrorMessage = "Ange minst en gäst.")]
    public int? OvernightGuestCount { get; set; }

    [Required(ErrorMessage = "Välj en måltidsplan.")]
    public MealPlan? MealPlan { get; set; }

    [Range(0, 10000, ErrorMessage = "Antal dagsgäster kan inte vara negativt.")]
    public int? DayGuestCount { get; set; }

    // Nullable: an unchecked checkbox group binds to null on form post — treated as empty by
    // InquiryNormalizer and the page markup.
    public List<Guid>? SelectedFacilityIds { get; set; } = [];

    public List<Guid>? SelectedCleaningZoneIds { get; set; } = [];

    public bool IsOrganisation { get; set; }

    [StringLength(200, ErrorMessage = "Organisationsnamnet får vara högst 200 tecken.")]
    public string? OrgName { get; set; }

    [StringLength(50, ErrorMessage = "Organisationsnumret får vara högst 50 tecken.")]
    public string? OrgNumber { get; set; }

    [StringLength(500, ErrorMessage = "Fakturaadressen får vara högst 500 tecken.")]
    public string? InvoiceAddress { get; set; }

    [StringLength(5000, ErrorMessage = "Meddelandet får vara högst 5000 tecken.")]
    public string? Message { get; set; }

    /// <summary>Explicit privacy-policy acknowledgement, required at submission (NFR-21).</summary>
    public bool AcceptPrivacyPolicy { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CheckIn is not null && CheckOut is not null && CheckOut <= CheckIn)
        {
            yield return new ValidationResult(
                "Utcheckningsdatumet måste vara efter incheckningsdatumet.",
                [nameof(CheckOut)]);
        }

        if (!AcceptPrivacyPolicy)
        {
            yield return new ValidationResult(
                "Du måste godkänna integritetspolicyn för att skicka förfrågan.",
                [nameof(AcceptPrivacyPolicy)]);
        }

        // OrgName is Conditional in the data model (§8): if the guest declares the booking is for an
        // organisation, the organisation must be named. OrgNumber and InvoiceAddress stay optional.
        if (IsOrganisation && string.IsNullOrWhiteSpace(OrgName))
        {
            yield return new ValidationResult(
                "Organisationsnamn krävs när bokningen görs för en organisation.",
                [nameof(OrgName)]);
        }
    }
}
