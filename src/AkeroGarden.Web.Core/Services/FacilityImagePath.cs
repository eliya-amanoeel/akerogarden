using System.Text.RegularExpressions;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Turns a stored facility-image value into the app-hosted path a page should render, and validates
/// blob names arriving from a public URL (post-launch security review — the CSP/image finding).
///
/// <para><strong>Why images are served by the app at all.</strong> Facility images used to render
/// straight from their absolute Blob URLs (FR-81). The <c>img-src 'self' data:</c> policy shipped for
/// NFR-39 does not list the storage account, so the browser blocked every one of them — two
/// requirements that contradict each other, shipped a day apart. Routing images through
/// <c>/bilder/{name}</c> satisfies both: the CSP stays tight, and the visitor's browser never contacts
/// the storage domain, which is also what NFR-35 is really about.</para>
///
/// <para>Pure and unit-testable — no storage, no HTTP.</para>
/// </summary>
public static class FacilityImagePath
{
    /// <summary>The route prefix images are served from. Swedish, like every other public route.</summary>
    public const string Prefix = "/bilder";

    /// <summary>
    /// A blob name as produced by <c>BlobImageStorage</c>: a 32-character GUID "N" format plus one of
    /// the allowed image extensions. Deliberately an exact allow-list rather than a check for "../":
    /// the endpoint takes this straight off a public URL, and anything that is not a name this app
    /// itself generated has no business reaching storage.
    /// </summary>
    private static readonly Regex BlobName = new(
        @"^[0-9a-f]{32}\.(jpg|jpeg|png|webp)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>True when <paramref name="blobName"/> is a name this app could have generated.</summary>
    public static bool IsValidBlobName(string? blobName) =>
        !string.IsNullOrEmpty(blobName) && BlobName.IsMatch(blobName);

    /// <summary>
    /// The path to render for a stored value, or null when there is no image.
    ///
    /// <para>Accepts both shapes on purpose. New uploads store a bare blob name; rows written before
    /// this change hold an absolute Blob URL, and the last path segment of one is exactly the blob
    /// name. Tolerating both avoids a data migration for what was, at the time of the change, a single
    /// row across every environment — production had none at all.</para>
    /// </summary>
    public static string? ToDisplayPath(string? stored)
    {
        var name = ToBlobName(stored);
        return name is null ? null : $"{Prefix}/{name}";
    }

    /// <summary>
    /// Extracts the blob name from a stored value, whether it is already a bare name or a legacy
    /// absolute URL. Returns null when the value is empty or is not a name this app generated.
    /// </summary>
    public static string? ToBlobName(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return null;

        var value = stored.Trim();

        // Legacy absolute URL — take the final path segment. Query/fragment are stripped first so a
        // stored SAS-style suffix cannot smuggle anything into the name.
        if (value.Contains("://", StringComparison.Ordinal))
        {
            var withoutQuery = value.Split('?', '#')[0];
            value = withoutQuery[(withoutQuery.LastIndexOf('/') + 1)..];
        }

        return IsValidBlobName(value) ? value : null;
    }
}
