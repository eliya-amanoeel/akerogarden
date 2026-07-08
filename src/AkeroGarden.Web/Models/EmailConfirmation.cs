namespace AkeroGarden.Web.Models;

/// <summary>
/// Shared confirm-email comparison for the public inquiry forms (Phase 7). Centralised so the
/// stay-request and general-inquiry forms — and the progressive client-side validation that mirrors
/// this rule — all compare the same way (case-insensitive, trimmed).
/// </summary>
public static class EmailConfirmation
{
    public const string MismatchMessage = "E-postadresserna stämmer inte överens.";

    /// <summary>
    /// True when the confirmation matches the primary address, ignoring case and surrounding
    /// whitespace. When either side is blank the <c>[Required]</c>/<c>[EmailAddress]</c> attributes
    /// report first, so this only guards the mismatch of two otherwise-present values.
    /// </summary>
    public static bool Matches(string? email, string? confirmEmail)
        => string.Equals(
            (email ?? string.Empty).Trim(),
            (confirmEmail ?? string.Empty).Trim(),
            StringComparison.OrdinalIgnoreCase);
}
