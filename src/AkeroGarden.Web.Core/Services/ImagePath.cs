using System.Text.RegularExpressions;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Turns a stored image name into the app-hosted path a page renders, and validates names arriving
/// from a public URL (post-launch security review — the CSP/image finding).
///
/// <para><strong>Why images are served by the app at all.</strong> The <c>img-src 'self' data:</c>
/// policy (NFR-39) does not list the storage account, so an image rendered from its Blob URL is
/// blocked. Routing images through <c>/bilder/{name}</c> keeps the CSP tight, and the visitor's
/// browser never contacts the storage domain, which is also what NFR-35 is really about.</para>
///
/// <para>Pure and unit-testable — no storage, no HTTP.</para>
/// </summary>
public static class ImagePath
{
    /// <summary>The route prefix images are served from. Swedish, like every other public route.</summary>
    public const string Prefix = "/bilder";

    /// <summary>
    /// An image name as produced by <c>BlobImageStorage</c>: a 32-character GUID "N" format plus one of
    /// the allowed image extensions. Deliberately an exact allow-list rather than a check for "../":
    /// the endpoint takes this straight off a public URL, and anything that is not a name this app
    /// itself generated has no business reaching storage.
    /// </summary>
    private static readonly Regex Name = new(
        @"^[0-9a-f]{32}\.(jpg|jpeg|png|webp)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>True when <paramref name="name"/> is a name this app could have generated.</summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && Name.IsMatch(name);

    /// <summary>The path to render for a stored image name, or null when there is no (valid) image.</summary>
    public static string? ToDisplayPath(string? name) =>
        IsValidName(name) ? $"{Prefix}/{name}" : null;
}
