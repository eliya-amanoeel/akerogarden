namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Stores uploaded images in Blob storage and returns their public URL.
/// An interface because it is a genuine infrastructure seam (Azurite in dev,
/// Azure in prod) — mirrors <see cref="IEmailService"/>.
/// </summary>
public interface IImageStorage
{
    /// <summary>
    /// Uploads image content and returns the resulting blob URL. The caller is
    /// responsible for validating format/size first (see FacilityService.ValidateImage).
    /// </summary>
    Task<string> UploadFacilityImageAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);
}
