using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Which credential the facility-image store uses (post-launch security review, finding #9).
///
/// <para>This one property decides between the app's managed identity and a storage <b>account key</b>
/// that grants full access to the whole account — including both Data Protection key-ring containers.
/// It is the smallest possible piece of logic guarding the largest credential in the system, which is
/// exactly why it is worth asserting rather than assuming.</para>
/// </summary>
public class BlobOptionsTests
{
    [Fact]
    public void No_service_uri_means_connection_string()
    {
        // Local dev against Azurite, which has no AAD.
        var options = new BlobOptions { ConnectionString = "UseDevelopmentStorage=true" };
        Assert.False(options.UsesManagedIdentity);
    }

    [Fact]
    public void A_service_uri_means_managed_identity()
    {
        var options = new BlobOptions { ServiceUri = "https://jmkagprodblob.blob.core.windows.net" };
        Assert.True(options.UsesManagedIdentity);
    }

    [Fact]
    public void A_service_uri_wins_over_a_connection_string()
    {
        // The precedence is the finding, not a detail: if a stale Blob__ConnectionString is ever left
        // behind in App Service configuration it must NOT quietly put the shared account key back in
        // play. Same rule as DataProtectionSetup.ResolveCredentialMode.
        var options = new BlobOptions
        {
            ServiceUri = "https://jmkagprodblob.blob.core.windows.net",
            ConnectionString = "DefaultEndpointsProtocol=https;AccountName=x;AccountKey=y;EndpointSuffix=core.windows.net",
        };
        Assert.True(options.UsesManagedIdentity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_service_uri_does_not_count_as_configured(string serviceUri)
    {
        // Whitespace is how an unset Bicep parameter or a cleared app setting arrives. Treating it as
        // "managed identity configured" would build a BlobContainerClient against a malformed URI and
        // fail at upload time rather than falling back.
        var options = new BlobOptions
        {
            ServiceUri = serviceUri,
            ConnectionString = "UseDevelopmentStorage=true",
        };
        Assert.False(options.UsesManagedIdentity);
    }

    [Fact]
    public void Facility_images_container_has_a_default()
    {
        // The container name is not supplied by Bicep; the default is what production actually uses,
        // and it must match the container declared in main.bicep.
        Assert.Equal("facility-images", new BlobOptions().FacilityImagesContainer);
    }
}
