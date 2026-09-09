using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// <see cref="IImageStorage"/> backed by Azure Blob Storage (Azurite locally).
/// Like <see cref="SmtpEmailService"/>, this is an infrastructure adapter — its logic
/// is verified in dev / Phase 6 E2E rather than against a live container in CI.
/// </summary>
public class BlobImageStorage(IOptions<BlobOptions> options) : IImageStorage
{
    private readonly BlobOptions _options = options.Value;

    /// <summary>
    /// Managed identity in Azure, connection string only for local Azurite (post-launch security
    /// review, finding #9). The connection string carries the storage ACCOUNT KEY, which grants full
    /// access to the whole account — including the two Data Protection key-ring containers. Removing
    /// it from configuration entirely is the point; hiding it behind a Key Vault reference would have
    /// kept a credential that no longer needs to exist.
    /// </summary>
    private BlobContainerClient Container() =>
        _options.UsesManagedIdentity
            ? new BlobContainerClient(
                new Uri($"{_options.ServiceUri.TrimEnd('/')}/{_options.FacilityImagesContainer}"),
                new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = string.IsNullOrWhiteSpace(_options.ManagedIdentityClientId)
                        ? null
                        : _options.ManagedIdentityClientId,
                }))
            : new BlobContainerClient(_options.ConnectionString, _options.FacilityImagesContainer);

    public async Task<string> UploadFacilityImageAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var container = Container();

        // PRIVATE container. Images are served through the app's own /bilder/{name} endpoint, so
        // nothing fetches them anonymously any more — and the CSP forbids the browser doing so
        // regardless (see FacilityImagePath). Previously created with PublicAccessType.Blob, which is
        // what FR-81 assumed before NFR-39's img-src 'self' made it unworkable.
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        // Random blob name — never derived from guest data or the original filename (NFR-05). The
        // shape here is what FacilityImagePath.IsValidBlobName enforces on the way back in; change one
        // and the other must change with it.
        var extension = Path.GetExtension(fileName);
        var blobName = $"{Guid.NewGuid():N}{extension}".ToLowerInvariant();
        var blob = container.GetBlobClient(blobName);

        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);

        // The NAME, not the URL. The database stores this; the page renders /bilder/{name}.
        return blobName;
    }

    public async Task<StoredImage?> OpenFacilityImageAsync(
        string blobName,
        CancellationToken cancellationToken = default)
    {
        // Defence in depth: the endpoint already rejects anything that is not a name this app
        // generated, but this is the boundary that actually touches storage, so it checks too.
        if (!FacilityImagePath.IsValidBlobName(blobName))
            return null;

        var blob = Container().GetBlobClient(blobName);

        try
        {
            var response = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
            var details = response.Value.Details;
            return new StoredImage(
                response.Value.Content,
                string.IsNullOrEmpty(details.ContentType) ? "application/octet-stream" : details.ContentType,
                details.ETag.ToString());
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // A referenced image that no longer exists is a 404, not a 500 — a deleted blob must not
            // take the facilities page down.
            return null;
        }
    }
}
