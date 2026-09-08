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

    public async Task<string> UploadFacilityImageAsync(
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        // Managed identity in Azure, connection string only for local Azurite (post-launch security
        // review, finding #9). The connection string carries the storage ACCOUNT KEY, which grants
        // full access to the whole account — including the two Data Protection key-ring containers.
        // Removing it from configuration entirely is the point; hiding it behind a Key Vault
        // reference would have kept a credential that no longer needs to exist.
        var container = _options.UsesManagedIdentity
            ? new BlobContainerClient(
                new Uri($"{_options.ServiceUri.TrimEnd('/')}/{_options.FacilityImagesContainer}"),
                new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ManagedIdentityClientId = string.IsNullOrWhiteSpace(_options.ManagedIdentityClientId)
                        ? null
                        : _options.ManagedIdentityClientId,
                }))
            : new BlobContainerClient(_options.ConnectionString, _options.FacilityImagesContainer);
        // Facility images are non-personal public assets shown on the public site (FR-81).
        await container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);

        // Random blob name — never derived from guest data or the original filename (NFR-05).
        var extension = Path.GetExtension(fileName);
        var blobName = $"{Guid.NewGuid():N}{extension}";
        var blob = container.GetBlobClient(blobName);

        await blob.UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            cancellationToken);

        return blob.Uri.ToString();
    }
}
