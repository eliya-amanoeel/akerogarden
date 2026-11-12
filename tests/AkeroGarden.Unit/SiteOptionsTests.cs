using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Every absolute link the system builds goes through <see cref="SiteOptions"/>. A base URL set with
/// or without a trailing slash must give the same link — a doubled or missing slash is a broken
/// completion link in a guest's inbox.
/// </summary>
public class SiteOptionsTests
{
    [Theory]
    [InlineData("https://akerogarden.se")]
    [InlineData("https://akerogarden.se/")]
    public void PublicUrl_joins_with_exactly_one_slash(string baseUrl)
    {
        var site = new SiteOptions { PublicBaseUrl = baseUrl };

        Assert.Equal("https://akerogarden.se/slutfor/abc", site.PublicUrl("/slutfor/abc"));
        Assert.Equal("https://akerogarden.se/slutfor/abc", site.PublicUrl("slutfor/abc"));
    }

    [Fact]
    public void PublicUrl_of_the_root_is_the_site_with_a_trailing_slash()
    {
        var site = new SiteOptions { PublicBaseUrl = "https://akerogarden.se" };

        Assert.Equal("https://akerogarden.se/", site.PublicUrl("/"));
    }

    [Fact]
    public void AdminUrl_uses_the_admin_base()
    {
        var site = new SiteOptions
        {
            PublicBaseUrl = "https://akerogarden.se",
            AdminBaseUrl = "https://admin.akerogarden.se/",
        };

        Assert.Equal("https://admin.akerogarden.se/bokningar/1", site.AdminUrl("/bokningar/1"));
    }
}
