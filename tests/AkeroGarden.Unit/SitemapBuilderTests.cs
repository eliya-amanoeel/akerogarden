using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// The sitemap/robots generation (Phase 11). The interesting cases are all exclusions: what must
/// *not* appear. A sitemap that lists too much produces no error anywhere — a crawler simply
/// follows it — so nothing but a test will ever say so.
/// </summary>
public class SitemapBuilderTests
{
    [Fact]
    public void Static_asset_routes_are_not_pages()
    {
        // Regression: the first implementation shipped a sitemap advertising /js/calendar.js, its
        // .gz sibling and both fingerprinted forms, because MapStaticAssets registers a route
        // endpoint per asset *variant* and the filter only excluded .txt and .xml.
        var paths = SitemapBuilder.SelectIndexablePaths([
            "/", "/kontakt",
            "js/calendar.js", "js/calendar.js.gz", "js/calendar.3rjhqft48r.js",
            "robots.txt", "sitemap.xml",
        ]);

        Assert.Equal(["/", "/kontakt"], paths);
    }

    [Fact]
    public void Parameterised_routes_are_not_pages()
    {
        // /slutfor/{token} is a per-guest URL. The pattern is meaningless to a crawler and a real
        // one would expose a booking.
        var paths = SitemapBuilder.SelectIndexablePaths(["/", "slutfor/{token}", "bokningar/{id:guid}"]);

        Assert.Equal(["/"], paths);
    }

    [Theory]
    [InlineData("/slutfor")]
    [InlineData("/Error")]
    [InlineData("/not-found")]
    public void Never_indexed_routes_are_excluded_whatever_their_casing(string route)
    {
        var paths = SitemapBuilder.SelectIndexablePaths(["/", route]);
        Assert.Equal(["/"], paths);
    }

    [Fact]
    public void Output_is_ordered_and_deduplicated_so_it_is_stable_between_deployments()
    {
        // A sitemap that reshuffles on every deploy tells a crawler the site changed when it did not.
        var paths = SitemapBuilder.SelectIndexablePaths(["/om-oss", "/boka", "/om-oss", "/", "/boka"]);

        Assert.Equal(["/", "/boka", "/om-oss"], paths);
    }

    [Fact]
    public void Sitemap_renders_absolute_urls_under_the_base_url()
    {
        var xml = SitemapBuilder.BuildSitemap("https://akerogarden.se/", ["/", "/kontakt"]);

        Assert.Contains("<loc>https://akerogarden.se/</loc>", xml);
        Assert.Contains("<loc>https://akerogarden.se/kontakt</loc>", xml);
        Assert.Contains("http://www.sitemaps.org/schemas/sitemap/0.9", xml);
        // No fabricated <lastmod> — nothing here tracks per-page content dates.
        Assert.DoesNotContain("lastmod", xml);
    }

    [Fact]
    public void Robots_outside_production_refuses_everything()
    {
        // staging.akerogarden.se is publicly reachable and serves the same pages. Without this it
        // competes with the real site as duplicate content.
        var robots = SitemapBuilder.BuildRobots("https://staging.akerogarden.se", allowIndexing: false);

        Assert.Contains("User-agent: *", robots);
        Assert.Contains("Disallow: /", robots);
        Assert.DoesNotContain("Sitemap:", robots);
    }

    [Fact]
    public void Robots_in_production_invites_crawlers_but_not_to_the_completion_form()
    {
        var robots = SitemapBuilder.BuildRobots("https://akerogarden.se", allowIndexing: true);

        Assert.Contains("Sitemap: https://akerogarden.se/sitemap.xml", robots);
        Assert.Contains("Disallow: /slutfor", robots);
        // The blanket refusal must NOT be present — that would silently deindex the live site.
        Assert.DoesNotContain("Disallow: /\n", robots.Replace("\r\n", "\n"));
    }
}
