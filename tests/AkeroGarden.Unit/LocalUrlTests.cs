using AkeroGarden.Web.Core.Security;

namespace AkeroGarden.Unit;

/// <summary>
/// The redirect guard behind <c>?ReturnUrl=</c> on the admin login page (post-launch security review,
/// finding #21). The parameter was previously ignored entirely, which was safe but lost the admin's
/// destination; honouring it is only safe while this holds the line.
///
/// <para>The rejection cases matter more than the acceptance ones: every one of them is a real
/// open-redirect payload, and a login page that forwards to an attacker's host after a genuine
/// sign-in is a credible phishing primitive.</para>
/// </summary>
public class LocalUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/bokningar")]
    [InlineData("/bokningar/3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [InlineData("/listor?filter=kommande")]
    [InlineData("/konton#nytt")]
    public void Site_rooted_paths_are_allowed(string url)
    {
        Assert.True(LocalUrl.IsSafe(url));
    }

    [Theory]
    // Absolute URLs — the obvious case.
    [InlineData("https://evil.example/")]
    [InlineData("http://evil.example/bokningar")]
    // Protocol-relative: a browser reads "//host" as "https://host". The most commonly missed case,
    // and the reason this is an allow-list rather than a "does it start with http" check.
    [InlineData("//evil.example")]
    [InlineData("//evil.example/bokningar")]
    // Backslash variant — some browsers normalise "/\" to "//".
    [InlineData("/\\evil.example")]
    // Not rooted: resolves relative to the current directory, and "evil.example/x" can be read as a
    // host by some parsers.
    [InlineData("evil.example")]
    [InlineData("bokningar")]
    // Scheme smuggling.
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void Anything_that_could_leave_the_site_is_refused(string url)
    {
        Assert.False(LocalUrl.IsSafe(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_values_are_refused(string? url)
    {
        // No ReturnUrl simply means "use the role landing page"; it is not an error, but it is
        // certainly not a safe redirect target.
        Assert.False(LocalUrl.IsSafe(url));
    }

    [Theory]
    [InlineData("/bokningar\nLocation: https://evil.example")]
    [InlineData("/bokningar\r\nSet-Cookie: x=y")]
    [InlineData("/bokningar\tx")]
    public void Control_characters_are_refused(string url)
    {
        // A newline in a redirect target is a response-splitting attempt; a well-formed local path
        // never contains one. Note this checks control characters only — a plain space is not one,
        // and is not a security problem, so it is deliberately not rejected here.
        Assert.False(LocalUrl.IsSafe(url));
    }
}
