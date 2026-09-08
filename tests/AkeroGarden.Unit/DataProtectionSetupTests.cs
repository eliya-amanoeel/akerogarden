using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// The Data Protection key-persistence mode is decided from configuration alone (Phase 7 — NFR-30),
/// so it must be verifiable without Azure. Local dev (no blob connection) stays on the ephemeral
/// filesystem key ring; staging/prod persist to Blob, and wrap with Key Vault when a key is set.
/// </summary>
public class DataProtectionSetupTests
{
    [Fact]
    public void ResolveMode_NoBlobConnection_IsEphemeral()
    {
        var options = new DataProtectionOptions(); // defaults: empty conn string
        Assert.Equal(DataProtectionMode.Ephemeral, DataProtectionSetup.ResolveMode(options));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveMode_BlankBlobConnection_IsEphemeral(string blobConnection)
    {
        var options = new DataProtectionOptions
        {
            KeysBlobConnectionString = blobConnection,
            KeyVaultKeyId = "https://vault.vault.azure.net/keys/dp/abc", // ignored when no blob conn
        };
        Assert.Equal(DataProtectionMode.Ephemeral, DataProtectionSetup.ResolveMode(options));
    }

    [Fact]
    public void ResolveMode_BlobConnectionOnly_IsBlobOnly()
    {
        var options = new DataProtectionOptions
        {
            KeysBlobConnectionString = "UseDevelopmentStorage=true",
        };
        Assert.Equal(DataProtectionMode.BlobOnly, DataProtectionSetup.ResolveMode(options));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveMode_BlobConnectionWithBlankKeyVaultKey_IsBlobOnly(string keyVaultKeyId)
    {
        var options = new DataProtectionOptions
        {
            KeysBlobConnectionString = "UseDevelopmentStorage=true",
            KeyVaultKeyId = keyVaultKeyId,
        };
        Assert.Equal(DataProtectionMode.BlobOnly, DataProtectionSetup.ResolveMode(options));
    }

    [Fact]
    public void ResolveMode_BlobConnectionAndKeyVaultKey_IsBlobWithKeyVault()
    {
        var options = new DataProtectionOptions
        {
            KeysBlobConnectionString = "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=y;EndpointSuffix=core.windows.net",
            KeyVaultKeyId = "https://jmk-ag-prod-kv.vault.azure.net/keys/dataprotection-key",
        };
        Assert.Equal(DataProtectionMode.BlobWithKeyVault, DataProtectionSetup.ResolveMode(options));
    }

    // ── Credential mode (post-launch security review, finding #9) ───────────────
    // Both hosts used to receive the same storage ACCOUNT KEY, so the public site could read the admin
    // site's key ring — the "shared nothing" isolation held at the DataProtection application-name
    // layer and not at the credential layer. Azure now authenticates with each app's managed identity.

    [Fact]
    public void CredentialMode_NothingConfigured_IsNone()
    {
        Assert.Equal(
            DataProtectionCredentialMode.None,
            DataProtectionSetup.ResolveCredentialMode(new DataProtectionOptions()));
    }

    [Fact]
    public void CredentialMode_ConnectionStringOnly_IsConnectionString()
    {
        // Local dev against Azurite, which has no AAD.
        var options = new DataProtectionOptions { KeysBlobConnectionString = "UseDevelopmentStorage=true" };
        Assert.Equal(
            DataProtectionCredentialMode.ConnectionString,
            DataProtectionSetup.ResolveCredentialMode(options));
    }

    [Fact]
    public void CredentialMode_BlobServiceUri_IsManagedIdentity()
    {
        var options = new DataProtectionOptions
        {
            KeysBlobServiceUri = "https://jmkagprodblob.blob.core.windows.net",
        };
        Assert.Equal(
            DataProtectionCredentialMode.ManagedIdentity,
            DataProtectionSetup.ResolveCredentialMode(options));
    }

    [Fact]
    public void CredentialMode_BlobServiceUri_WinsOverAConnectionString()
    {
        // The precedence is the point of the finding, not a detail. If a stale
        // DataProtection__KeysBlobConnectionString is ever left behind in App Service configuration,
        // it must NOT quietly put the shared account key back in play.
        var options = new DataProtectionOptions
        {
            KeysBlobServiceUri = "https://jmkagprodblob.blob.core.windows.net",
            KeysBlobConnectionString = "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=y;EndpointSuffix=core.windows.net",
        };
        Assert.Equal(
            DataProtectionCredentialMode.ManagedIdentity,
            DataProtectionSetup.ResolveCredentialMode(options));
    }

    [Fact]
    public void ResolveMode_BlobServiceUriAndKeyVaultKey_IsBlobWithKeyVault()
    {
        // A managed-identity blob URI switches persistence on exactly as a connection string did —
        // otherwise moving to managed identity would silently drop back to the ephemeral key ring and
        // invalidate every auth cookie on each restart.
        var options = new DataProtectionOptions
        {
            KeysBlobServiceUri = "https://jmkagprodblob.blob.core.windows.net",
            KeyVaultKeyId = "https://jmk-ag-prod-kv.vault.azure.net/keys/dataprotection-key",
        };
        Assert.Equal(DataProtectionMode.BlobWithKeyVault, DataProtectionSetup.ResolveMode(options));
    }
}
