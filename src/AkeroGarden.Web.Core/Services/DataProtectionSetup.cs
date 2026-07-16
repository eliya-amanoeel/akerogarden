using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.DataProtection;

namespace AkeroGarden.Web.Core.Services;

/// <summary>How the Data Protection key ring is persisted, decided from <see cref="DataProtectionOptions"/>.</summary>
public enum DataProtectionMode
{
    /// <summary>No blob connection configured (local dev): default in-memory / filesystem key ring.</summary>
    Ephemeral,

    /// <summary>Key ring persisted to Blob storage; master key unencrypted-at-app-level (Blob is encrypted at rest).</summary>
    BlobOnly,

    /// <summary>Key ring persisted to Blob storage with the master key wrapped by a Key Vault key.</summary>
    BlobWithKeyVault,
}

/// <summary>
/// Wires ASP.NET Core Data Protection key persistence per <see cref="DataProtectionOptions"/> (Phase 7,
/// NFR-30). The gating decision is the pure <see cref="ResolveMode"/> so it can be unit-tested without Azure.
/// </summary>
public static class DataProtectionSetup
{
    /// <summary>Stable application name so the key ring is shared, not per-deployment-path.</summary>
    public const string ApplicationName = "AkeroGarden";

    /// <summary>
    /// Decides persistence mode from configuration alone — no I/O. Empty blob connection ⇒ Ephemeral
    /// (dev); a blob connection with no Key Vault key ⇒ BlobOnly; both present ⇒ BlobWithKeyVault.
    /// </summary>
    public static DataProtectionMode ResolveMode(DataProtectionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.KeysBlobConnectionString))
            return DataProtectionMode.Ephemeral;

        return string.IsNullOrWhiteSpace(options.KeyVaultKeyId)
            ? DataProtectionMode.BlobOnly
            : DataProtectionMode.BlobWithKeyVault;
    }

    /// <summary>
    /// Adds Data Protection and, when configured, persists the key ring to Blob storage (optionally
    /// wrapped by Key Vault). In <see cref="DataProtectionMode.Ephemeral"/> the default provider is used.
    /// </summary>
    public static IServiceCollection AddAkeroDataProtection(this IServiceCollection services, DataProtectionOptions options)
    {
        var builder = services.AddDataProtection().SetApplicationName(ApplicationName);
        var mode = ResolveMode(options);

        if (mode == DataProtectionMode.Ephemeral)
            return services;

        // The DP blob provider does not create the container; ensure it exists as a PRIVATE container
        // (no public access — these are secret keys, unlike the anonymously-read facility images).
        var container = new BlobContainerClient(options.KeysBlobConnectionString, options.KeysContainer);
        container.CreateIfNotExists(PublicAccessType.None);
        var keyBlob = container.GetBlobClient(options.KeysBlobName);
        builder.PersistKeysToAzureBlobStorage(keyBlob);

        if (mode == DataProtectionMode.BlobWithKeyVault)
        {
            // The app runs under a user-assigned managed identity, so DefaultAzureCredential must be
            // told which identity to use; locally this id is empty and this branch is never reached.
            var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
            {
                ManagedIdentityClientId = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
                    ? null
                    : options.ManagedIdentityClientId,
            });
            builder.ProtectKeysWithAzureKeyVault(new Uri(options.KeyVaultKeyId), credential);
        }

        return services;
    }
}
