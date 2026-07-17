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
}
