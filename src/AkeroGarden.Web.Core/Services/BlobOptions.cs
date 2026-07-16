namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Azure Blob Storage configuration, bound from the "Blob" configuration section.
/// The connection string is an encrypted app setting / Key Vault secret in non-dev
/// environments (NFR-30); locally it points at Azurite.
/// </summary>
public class BlobOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Container holding facility images (FR-27).</summary>
    public string FacilityImagesContainer { get; set; } = "facility-images";
}
