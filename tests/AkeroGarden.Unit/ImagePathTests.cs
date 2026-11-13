using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Images — of facilities, lodging buildings and rooms — are served by the app rather than fetched
/// from Blob storage by the browser (post-launch security review — the CSP/image finding). The
/// <c>img-src 'self'</c> policy (NFR-39) blocks a Blob URL outright.
///
/// <para>These cover the two jobs of the helper: producing the path a page renders, and deciding
/// which names may reach storage. The second is a security boundary — the endpoint takes the name
/// straight off a public URL.</para>
/// </summary>
public class ImagePathTests
{
    private const string Name = "0c9be3c902284fa898b6eb78264953c9.jpg";

    // ── Rendering ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_stored_image_name_becomes_an_app_hosted_path()
    {
        Assert.Equal($"/bilder/{Name}", ImagePath.ToDisplayPath(Name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_image_renders_no_path(string? stored)
    {
        Assert.Null(ImagePath.ToDisplayPath(stored));
    }

    [Fact]
    public void A_stored_value_that_is_not_a_name_this_app_generated_renders_nothing()
    {
        // Better a missing image than a request to something unexpected. A full URL is no longer a
        // stored shape at all — the ImagesForLodging migration reduced the last legacy ones to names.
        Assert.Null(ImagePath.ToDisplayPath("https://evil.example/images/x.jpg"));
        Assert.Null(ImagePath.ToDisplayPath("not-a-blob-name"));
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
        Assert.True(ImagePath.IsValidName(name));
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
        Assert.False(ImagePath.IsValidName(name));
    }

    [Fact]
    public void The_dataprotection_key_ring_is_not_reachable_through_this_name()
    {
        // The key rings live in sibling containers on the same storage account. The endpoint uses the
        // app's managed identity, which can read them — so the name check is the only thing standing
        // between a public URL and the key that protects the admin auth cookie.
        Assert.False(ImagePath.IsValidName("../dataprotection-keys-admin/keys.xml"));
        Assert.Null(ImagePath.ToDisplayPath("../dataprotection-keys-admin/keys.xml"));
    }
}
