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

/// <summary>How the key-ring blob is authenticated to.</summary>
public enum DataProtectionCredentialMode
{
    /// <summary>No blob persistence configured.</summary>
    None,

    /// <summary>Storage account key, via a connection string. Local dev (Azurite) only — see
    /// <see cref="DataProtectionOptions.KeysBlobServiceUri"/> for why Azure no longer uses it.</summary>
    ConnectionString,

    /// <summary>The app's user-assigned managed identity. The deployed configuration.</summary>
    ManagedIdentity,
}

/// <summary>
/// Wires ASP.NET Core Data Protection key persistence per <see cref="DataProtectionOptions"/> (Phase 7,
/// NFR-30). The gating decision is the pure <see cref="ResolveMode"/> so it can be unit-tested without Azure.
/// </summary>
public static class DataProtectionSetup
{
    /// <summary>
    /// Default stable application name so the key ring is shared, not per-deployment-path. Phase 10b
    /// splits the app into two hosts that each own an isolated key ring (public: antiforgery on the
    /// booking form; admin: the auth cookie — shared nothing); each passes its own application name to
    /// <see cref="AddAkeroDataProtection"/> so one host can never unprotect the other's payloads.
    /// </summary>
    public const string ApplicationName = "AkeroGarden";

    /// <summary>
    /// Decides persistence mode from configuration alone — no I/O. Empty blob connection ⇒ Ephemeral
    /// (dev); a blob connection with no Key Vault key ⇒ BlobOnly; both present ⇒ BlobWithKeyVault.
    /// </summary>
    public static DataProtectionMode ResolveMode(DataProtectionOptions options)
    {
        if (ResolveCredentialMode(options) == DataProtectionCredentialMode.None)
            return DataProtectionMode.Ephemeral;

        return string.IsNullOrWhiteSpace(options.KeyVaultKeyId)
            ? DataProtectionMode.BlobOnly
            : DataProtectionMode.BlobWithKeyVault;
    }

    /// <summary>
    /// How the key-ring blob is authenticated to — no I/O, so it is unit-testable. A blob service URI
    /// wins over a connection string: it is the deployed configuration (finding #9), and having it take
    /// precedence means a stale connection-string setting left behind in App Service cannot quietly
    /// keep the shared account key in play.
    /// </summary>
    public static DataProtectionCredentialMode ResolveCredentialMode(DataProtectionOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.KeysBlobServiceUri))
            return DataProtectionCredentialMode.ManagedIdentity;

        return string.IsNullOrWhiteSpace(options.KeysBlobConnectionString)
            ? DataProtectionCredentialMode.None
            : DataProtectionCredentialMode.ConnectionString;
    }

    /// <summary>
    /// Adds Data Protection and, when configured, persists the key ring to Blob storage (optionally
    /// wrapped by Key Vault). In <see cref="DataProtectionMode.Ephemeral"/> the default provider is used.
    /// </summary>
    public static IServiceCollection AddAkeroDataProtection(
        this IServiceCollection services, DataProtectionOptions options, string applicationName = ApplicationName)
    {
        var builder = services.AddDataProtection().SetApplicationName(applicationName);
        var mode = ResolveMode(options);

        if (mode == DataProtectionMode.Ephemeral)
            return services;

        // The app runs under a user-assigned managed identity, so DefaultAzureCredential must be told
        // which one to use. Built once and shared by the blob client and the Key Vault wrap below.
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = string.IsNullOrWhiteSpace(options.ManagedIdentityClientId)
                ? null
                : options.ManagedIdentityClientId,
        });

        // The DP blob provider does not create the container; ensure it exists as a PRIVATE container
        // (no public access — these are secret keys, unlike the anonymously-read facility images).
        var container = ResolveCredentialMode(options) switch
        {
            // Deployed configuration (finding #9): managed identity against the blob endpoint, so no
            // storage account key is handed to either host. Requires the app's identity to hold
            // "Storage Blob Data Contributor" on the account — Contributor at the control plane is NOT
            // enough, data-plane access is a separate grant.
            DataProtectionCredentialMode.ManagedIdentity => new BlobContainerClient(
                new Uri($"{options.KeysBlobServiceUri.TrimEnd('/')}/{options.KeysContainer}"), credential),

            // Local dev against Azurite, which has no AAD. Never used in Azure.
            _ => new BlobContainerClient(options.KeysBlobConnectionString, options.KeysContainer),
        };

        container.CreateIfNotExists(PublicAccessType.None);
        var keyBlob = container.GetBlobClient(options.KeysBlobName);
        builder.PersistKeysToAzureBlobStorage(keyBlob);

        if (mode == DataProtectionMode.BlobWithKeyVault)
            builder.ProtectKeysWithAzureKeyVault(new Uri(options.KeyVaultKeyId), credential);

        return services;
    }
}
