namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Azure Blob Storage configuration for the images of facilities, lodging buildings and rooms
/// (FR-27, FR-13, FR-17), bound from the "Blob"
/// configuration section.
///
/// <para>In Azure the app authenticates with its <b>managed identity</b> via
/// <see cref="ServiceUri"/> — see the post-launch security review, finding #9. Locally
/// <see cref="ConnectionString"/> points at Azurite, which has no AAD.</para>
/// </summary>
public class BlobOptions
{
    /// <summary>
    /// Storage connection string. Local dev (Azurite) only in the deployed configuration — it carries
    /// the storage <b>account key</b>, which grants full access to the whole account, so Azure uses
    /// <see cref="ServiceUri"/> instead.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Blob service endpoint, e.g. <c>https://jmkagprodblob.blob.core.windows.net</c>. When set, the
    /// app reaches the container with its managed identity and no account key is configured anywhere.
    /// Takes precedence over <see cref="ConnectionString"/>, so a stale connection-string setting
    /// cannot quietly reintroduce the shared key.
    /// </summary>
    public string ServiceUri { get; set; } = string.Empty;

    /// <summary>
    /// Client id of the user-assigned managed identity to authenticate with. The app runs under a
    /// user-assigned identity, so <see cref="Azure.Identity.DefaultAzureCredential"/> must be told
    /// which one. Ignored when <see cref="ServiceUri"/> is empty.
    /// </summary>
    public string ManagedIdentityClientId { get; set; } = string.Empty;

    /// <summary>Container holding every uploaded image — facilities, lodging buildings and rooms alike.</summary>
    public string ImagesContainer { get; set; } = "images";

    /// <summary>True when the managed-identity path is configured. Pure, so it is unit-testable.</summary>
    public bool UsesManagedIdentity => !string.IsNullOrWhiteSpace(ServiceUri);
}
