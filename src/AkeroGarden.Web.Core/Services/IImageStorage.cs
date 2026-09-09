namespace AkeroGarden.Web.Core.Services;

/// <summary>One stored image, opened for streaming through the app's own image endpoint.</summary>
/// <param name="Content">The blob's content stream. The caller disposes it.</param>
/// <param name="ContentType">The stored content type, e.g. <c>image/jpeg</c>.</param>
/// <param name="ETag">The blob's ETag, forwarded so browsers can revalidate cheaply.</param>
public sealed record StoredImage(Stream Content, string ContentType, string? ETag);

/// <summary>
/// Stores uploaded facility images in Blob storage and reads them back.
/// An interface because it is a genuine infrastructure seam (Azurite in dev,
/// Azure in prod) — mirrors <see cref="IEmailService"/>.
///
/// <para><strong>Blob names, not URLs (post-launch security review — the CSP/image finding).</strong>
/// Uploads return the bare blob name and the app serves images from its own
/// <c>/bilder/{name}</c> endpoint. Previously this returned an absolute Blob URL which was rendered
/// directly, and the <c>img-src 'self'</c> Content-Security-Policy shipped for NFR-39 blocked every
/// one of them: FR-81 ("the public site renders images from their anonymous Blob URLs") and the CSP
/// contradicted each other. Serving through the app satisfies both, and means the visitor's browser
/// never contacts the storage domain at all.</para>
/// </summary>
public interface IImageStorage
{
    /// <summary>
    /// Uploads image content and returns the resulting <b>blob name</b> (not a URL). The caller is
    /// responsible for validating format/size first (see <c>FacilityService.ValidateImage</c>).
    /// </summary>
    Task<string> UploadFacilityImageAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a stored facility image for streaming, or null when no such blob exists.
    /// <paramref name="blobName"/> comes from a public URL, so implementations must treat it as
    /// untrusted — see <see cref="FacilityImagePath.IsValidBlobName"/>, which the endpoint applies
    /// before calling this.
    /// </summary>
    Task<StoredImage?> OpenFacilityImageAsync(
        string blobName,
        CancellationToken cancellationToken = default);
}
