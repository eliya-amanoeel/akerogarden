namespace AkeroGarden.Web.Services;

/// <summary>
/// Configuration for ASP.NET Core Data Protection key persistence, bound from the
/// "DataProtection" configuration section.
///
/// On App Service a recycle / redeploy / scale-out regenerates the in-memory key ring,
/// which invalidates every antiforgery token and auth cookie (a guest mid-way through
/// <c>/boka</c> then hits "A valid antiforgery token was not provided" on submit). Persisting
/// the key ring to durable, shared Blob storage — and wrapping the master key with Key Vault —
/// makes keys survive restarts and be shared across instances (Phase 7 hardening, NFR-30).
///
/// The wiring is <b>config-gated</b> (see <see cref="DataProtectionSetup.ResolveMode"/>): when no
/// blob connection string is configured (local dev) the app falls back to the default filesystem
/// key ring, so Docker dev needs no Azure. The settings are supplied only in staging/prod via Bicep.
/// </summary>
public class DataProtectionOptions
{
    /// <summary>
    /// Connection string for the storage account holding the key ring. Deliberately a
    /// <b>separate</b> setting from <c>Blob:ConnectionString</c> so the gating is explicit:
    /// its absence (local dev) means "no persistence", never an accidental write to Azurite.
    /// </summary>
    public string KeysBlobConnectionString { get; set; } = string.Empty;

    /// <summary>Private container holding the key ring. Must NOT allow public access (unlike facility images).</summary>
    public string KeysContainer { get; set; } = "dataprotection-keys";

    /// <summary>Blob name for the key ring XML.</summary>
    public string KeysBlobName { get; set; } = "keys.xml";

    /// <summary>
    /// Full URI of the Key Vault key used to wrap (encrypt) the master key. When empty, keys are
    /// persisted to Blob unencrypted-at-app-level (Blob is still encrypted at rest by Azure).
    /// </summary>
    public string KeyVaultKeyId { get; set; } = string.Empty;

    /// <summary>
    /// Client id of the user-assigned managed identity used to authenticate to Key Vault from app
    /// code (the app runs under a user-assigned identity, so <see cref="Azure.Identity.DefaultAzureCredential"/>
    /// must be told which one to use). Ignored when <see cref="KeyVaultKeyId"/> is empty.
    /// </summary>
    public string ManagedIdentityClientId { get; set; } = string.Empty;
}
