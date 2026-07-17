using System.ComponentModel.DataAnnotations;

namespace AkeroGarden.Web.Core.Models;

/// <summary>
/// The public general-inquiry form (FR-04): contact details and a required free-text message.
/// Bound by the <c>/boka</c> page and passed to <see cref="Services.BookingService"/>.
/// </summary>
public class GeneralInquiryInput : IValidatableObject
{
    [Required(ErrorMessage = "Namn krävs.")]
    [StringLength(200, ErrorMessage = "Namnet får vara högst 200 tecken.")]
    public string ContactName { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-postadress krävs.")]
    [EmailAddress(ErrorMessage = "Ange en giltig e-postadress.")]
    [StringLength(320, ErrorMessage = "E-postadressen får vara högst 320 tecken.")]
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>
    /// Confirm-email safeguard (Phase 7) — see <see cref="StayRequestInput.ConfirmEmail"/>. Must
    /// match <see cref="ContactEmail"/> (checked in <see cref="Validate"/>, case-insensitive/trimmed).
    /// </summary>
    [Required(ErrorMessage = "Bekräfta din e-postadress.")]
    [StringLength(320, ErrorMessage = "E-postadressen får vara högst 320 tecken.")]
    public string ConfirmEmail { get; set; } = string.Empty;

    [OptionalPhone(ErrorMessage = "Ange ett giltigt telefonnummer.")]
    [StringLength(50, ErrorMessage = "Telefonnumret får vara högst 50 tecken.")]
    public string? ContactPhone { get; set; }

    [Required(ErrorMessage = "Meddelande krävs.")]
    [StringLength(5000, ErrorMessage = "Meddelandet får vara högst 5000 tecken.")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Explicit privacy-policy acknowledgement, required at submission (NFR-21).</summary>
    public bool AcceptPrivacyPolicy { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!AcceptPrivacyPolicy)
        {
            yield return new ValidationResult(
                "Du måste godkänna integritetspolicyn för att skicka förfrågan.",
                [nameof(AcceptPrivacyPolicy)]);
        }

        // Only when a confirmation was actually entered — a blank one is reported by [Required].
        if (!string.IsNullOrWhiteSpace(ConfirmEmail) && !EmailConfirmation.Matches(ContactEmail, ConfirmEmail))
        {
            yield return new ValidationResult(
                EmailConfirmation.MismatchMessage,
                [nameof(ConfirmEmail)]);
        }
    }
}
