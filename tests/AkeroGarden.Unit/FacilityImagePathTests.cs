using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Facility images are served by the app rather than fetched from Blob storage by the browser
/// (post-launch security review — the CSP/image finding). FR-81 said the public site renders images
/// from their anonymous Blob URLs; the <c>img-src 'self'</c> policy shipped for NFR-39 blocks exactly
/// that. Both requirements were live and contradicted each other for a day, invisibly, because
/// production had no facility images to break.
///
/// <para>These cover the two jobs of the helper: producing the path a page renders, and deciding
/// which names may reach storage. The second is a security boundary — the endpoint takes the name
/// straight off a public URL.</para>
/// </summary>
public class FacilityImagePathTests
{
    private const string Name = "0c9be3c902284fa898b6eb78264953c9.jpg";

    // ── Rendering ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_stored_blob_name_becomes_an_app_hosted_path()
    {
        Assert.Equal($"/bilder/{Name}", FacilityImagePath.ToDisplayPath(Name));
    }

    [Fact]
    public void A_legacy_absolute_blob_url_still_renders()
    {
        // Rows written before this change hold a full Blob URL. Both shapes are accepted so no data
        // migration was needed — there was exactly one such row across all environments, and none in
        // production.
        Assert.Equal(
            $"/bilder/{Name}",
            FacilityImagePath.ToDisplayPath($"https://jmkagprodblob.blob.core.windows.net/facility-images/{Name}"));
    }

    [Fact]
    public void A_legacy_url_with_a_query_string_is_stripped_before_the_name_is_taken()
    {
        // A stored SAS-style suffix must not smuggle anything into the name.
        Assert.Equal(
            $"/bilder/{Name}",
            FacilityImagePath.ToDisplayPath(
                $"https://jmkagprodblob.blob.core.windows.net/facility-images/{Name}?sv=2024-01-01&sig=abc"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_image_renders_no_path(string? stored)
    {
        Assert.Null(FacilityImagePath.ToDisplayPath(stored));
    }

    [Fact]
    public void A_stored_value_that_is_not_a_name_this_app_generated_renders_nothing()
    {
        // Better a missing image than a request to something unexpected.
        Assert.Null(FacilityImagePath.ToDisplayPath("https://evil.example/facility-images/x.jpg"));
        Assert.Null(FacilityImagePath.ToDisplayPath("not-a-blob-name"));
    }

    // ── The security boundary ───────────────────────────────────────────────────

    [Theory]
    [InlineData("0c9be3c902284fa898b6eb78264953c9.jpg")]
    [InlineData("0c9be3c902284fa898b6eb78264953c9.jpeg")]
    [InlineData("0c9be3c902284fa898b6eb78264953c9.png")]
    [InlineData("0c9be3c902284fa898b6eb78264953c9.webp")]
    [InlineData("0C9BE3C902284FA898B6EB78264953C9.JPG")]
    public void Names_this_app_generates_are_accepted(string name)
    {
        Assert.True(FacilityImagePath.IsValidBlobName(name));
    }

    [Theory]
    // Path traversal, the reason this is an allow-list and not a blocklist.
    [InlineData("../dataprotection-keys-admin/keys.xml")]
    [InlineData("..%2Fdataprotection-keys-admin%2Fkeys.xml")]
    [InlineData("/etc/passwd")]
    [InlineData("keys.xml")]
    // Right shape, wrong extension — an uploaded .html served from our own origin would be stored XSS.
    [InlineData("0c9be3c902284fa898b6eb78264953c9.html")]
    [InlineData("0c9be3c902284fa898b6eb78264953c9.svg")]
    [InlineData("0c9be3c902284fa898b6eb78264953c9")]
    // Wrong length or characters.
    [InlineData("0c9be3c9.jpg")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz.jpg")]
    [InlineData("0c9be3c902284fa898b6eb78264953c9.jpg.exe")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_refused(string? name)
    {
        Assert.False(FacilityImagePath.IsValidBlobName(name));
    }

    [Fact]
    public void The_dataprotection_key_ring_is_not_reachable_through_this_name()
    {
        // The key rings live in sibling containers on the same storage account. The endpoint uses the
        // app's managed identity, which can read them — so the name check is the only thing standing
        // between a public URL and the key that protects the admin auth cookie.
        Assert.False(FacilityImagePath.IsValidBlobName("../dataprotection-keys-admin/keys.xml"));
        Assert.Null(FacilityImagePath.ToBlobName("../dataprotection-keys-admin/keys.xml"));
    }
}
