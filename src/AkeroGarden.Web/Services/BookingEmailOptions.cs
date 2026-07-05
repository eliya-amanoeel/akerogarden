namespace AkeroGarden.Web.Services;

/// <summary>
/// Configuration for booking-related emails (§9): the links they contain and the footer
/// details every email must carry (Åkerögården name, contact email, privacy-policy link).
/// Bound from the "BookingEmail" configuration section.
/// </summary>
public class BookingEmailOptions
{
    /// <summary>Public site base URL, e.g. <c>https://akerogarden.se</c> — used to build the
    /// completion magic link and the privacy-policy link in the footer.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Admin panel base URL, e.g. <c>https://admin.akerogarden.se</c> — used to build
    /// the direct booking link in the admin notification email (FR-06).</summary>
    public string AdminBaseUrl { get; set; } = string.Empty;

    /// <summary>Address that receives admin notification emails (new inquiry, etc.).</summary>
    public string AdminNotificationEmail { get; set; } = string.Empty;

    /// <summary>Display name for the admin notification recipient.</summary>
    public string AdminNotificationName { get; set; } = "Åkerögården Admin";

    /// <summary>Contact email shown in every email footer (§9).</summary>
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>Property address stated in the 14-day reminder (FR-64). Configurable so the
    /// go-live address can be set without a code change.</summary>
    public string PropertyAddress { get; set; } = "Åkerögården, Norrtälje";
}
