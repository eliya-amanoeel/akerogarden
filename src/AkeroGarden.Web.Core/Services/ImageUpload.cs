namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// The rules every image upload follows, whatever it is an image of — a facility, a lodging building
/// or a room (FR-27, FR-13, FR-17). One set of rules, so the three upload fields cannot drift apart.
/// </summary>
public static class ImageUpload
{
    /// <summary>5 MB upload ceiling.</summary>
    public const long MaxBytes = 5 * 1024 * 1024;

    /// <summary>The <c>accept</c> list for the admin file picker — the same three types <see cref="Validate"/> allows.</summary>
    public const string Accept = "image/jpeg,image/png,image/webp";

    private static readonly string[] AllowedContentTypes = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>
    /// Pure upload validation (unit-testable, NFR-26): accepts JPEG/PNG/WebP up to 5 MB.
    /// Returns a Swedish error message when invalid, or null when valid.
    /// </summary>
    public static string? Validate(string? contentType, long sizeBytes)
    {
        if (string.IsNullOrEmpty(contentType) ||
            !AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            return "Bilden måste vara i formatet JPEG, PNG eller WebP.";

        if (sizeBytes <= 0)
            return "Bilden är tom.";

        if (sizeBytes > MaxBytes)
            return "Bilden får vara högst 5 MB.";

        return null;
    }

    /// <summary>
    /// Removes an image the record no longer points at. Call it AFTER the save, deliberately: the
    /// database is the record of what the facility, building or room HAS, and a storage failure must
    /// not roll back a completed edit. It also keeps the slow call outside the window where unsaved
    /// rows are pending on the circuit-shared context — the same ordering that produced the #2 crash.
    ///
    /// <para>Replacing or clearing an image used to leave the old blob in storage forever (raised by
    /// the maintainer on 2026-09-09) — every edit orphaned a file that stayed fetchable by anyone
    /// holding its name.</para>
    /// </summary>
    public static async Task DeleteSupersededAsync(
        this IImageStorage images, string? previous, string? current, ILogger logger)
    {
        if (!ImagePath.IsValidName(previous) || previous == current)
            return;

        try
        {
            await images.DeleteImageAsync(previous!);
        }
        catch (Exception ex)
        {
            // An orphaned blob is a tidiness problem; a failed edit is a real one.
            logger.LogWarning(ex, "Could not delete superseded image {ImageName}", previous);
        }
    }
}
