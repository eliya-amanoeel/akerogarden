extern alias PublicApp;
extern alias AdminApp;

using System.Text.RegularExpressions;
using AkeroGarden.Integration.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace AkeroGarden.Integration;

/// <summary>
/// robots.txt and sitemap.xml as actually served (Phase 11).
///
/// <para>The load-bearing test is <see cref="Sitemap_lists_exactly_the_public_pages"/>. The sitemap
/// is generated from the app's own routes, which is what keeps it from going stale — but it also
/// means anything that registers a route can leak into it. That is not hypothetical: the first
/// implementation advertised <c>/js/calendar.js</c>, its <c>.gz</c> sibling and both fingerprinted
/// forms, because <c>MapStaticAssets</c> registers a route endpoint per asset variant. Asserting an
/// exact set rather than "contains the pages" is the whole point — a "contains" test passes just as
/// happily with fifty JavaScript files alongside.</para>
/// </summary>
public sealed class SeoIntegrationTests : IClassFixture<DbFixture>, IDisposable
{
    private readonly WebApplicationFactory<PublicApp::Program> _publicDevelopment;
    private readonly WebApplicationFactory<PublicApp::Program> _publicProduction;
    private readonly WebApplicationFactory<AdminApp::Program> _admin;

    private const string BaseUrl = "https://akerogarden.se";

    public SeoIntegrationTests(DbFixture db)
    {
        _publicDevelopment = CreatePublic(db, Environments.Development);
        // Indexing is decided by environment, so the production behaviour has to be booted as
        // Production to be tested at all — asserting it any other way would be asserting the
        // opposite of what production does.
        _publicProduction = CreatePublic(db, Environments.Production);

        _admin = new WebApplicationFactory<AdminApp::Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment(Environments.Development);
                b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            });
    }

    private static WebApplicationFactory<PublicApp::Program> CreatePublic(DbFixture db, string environment) =>
        new WebApplicationFactory<PublicApp::Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            b.UseSetting("BookingEmail:PublicBaseUrl", BaseUrl);
        });

    /// <summary>The nine public pages. Written out rather than derived, so that a page silently
    /// disappearing from the routes fails here instead of quietly leaving the sitemap.</summary>
    private static readonly string[] PublicPages =
    [
        "/", "/boka", "/bokningsvillkor", "/faciliteter", "/integritetspolicy",
        "/kalender", "/kontakt", "/mat-och-priser", "/om-oss",
    ];

    [Fact]
    public async Task Sitemap_lists_exactly_the_public_pages()
    {
        using var client = _publicProduction.CreateClient();
        var xml = await client.GetStringAsync("/sitemap.xml");

        var listed = Regex.Matches(xml, "<loc>(.*?)</loc>")
            .Select(m => m.Groups[1].Value.Replace(BaseUrl, ""))
            .Select(path => path.Length == 0 ? "/" : path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(PublicPages.OrderBy(p => p, StringComparer.Ordinal).ToArray(), listed);
    }

    [Fact]
    public async Task Sitemap_never_advertises_the_completion_form()
    {
        // A per-guest, token-gated URL. The route pattern is useless to a crawler and a real URL
        // would expose a booking.
        using var client = _publicProduction.CreateClient();
        var xml = await client.GetStringAsync("/sitemap.xml");

        Assert.DoesNotContain("slutfor", xml);
    }

    [Fact]
    public async Task Robots_invites_crawlers_in_production_only()
    {
        using var production = _publicProduction.CreateClient();
        var live = await production.GetStringAsync("/robots.txt");

        Assert.Contains($"Sitemap: {BaseUrl}/sitemap.xml", live);
        Assert.Contains("Disallow: /slutfor", live);
        Assert.DoesNotContain("Disallow: /\n", live.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task Robots_refuses_everything_outside_production()
    {
        // The environment IS the switch, so this asserts the switch — not a configuration flag that
        // happens to be off. Staging serves the same pages on a public hostname.
        using var development = _publicDevelopment.CreateClient();
        var staging = await development.GetStringAsync("/robots.txt");

        Assert.Contains("Disallow: /", staging);
        Assert.DoesNotContain("Sitemap:", staging);
    }

    [Fact]
    public async Task The_admin_host_refuses_crawling_and_indexing()
    {
        using var client = _admin.CreateClient();

        var robots = await client.GetStringAsync("/robots.txt");
        Assert.Contains("Disallow: /", robots);

        // robots.txt asks a well-behaved crawler not to fetch; X-Robots-Tag tells any crawler that
        // reaches a page anyway not to index it. The login page is reachable without auth, so both.
        var login = await client.GetAsync("/login");
        Assert.Equal("noindex, nofollow", Assert.Single(login.Headers.GetValues("X-Robots-Tag")));
    }

    /// <summary>
    /// A client whose base address is https. In Production the antiforgery cookie is
    /// <c>SecurePolicy = Always</c>, so rendering any page with a form over the factory's default
    /// http://localhost throws "the current request is not an SSL request" — a property of the test
    /// harness, not of the app, but it 500s /boka exactly like a real defect would.
    /// </summary>
    private static HttpClient HttpsClient<T>(WebApplicationFactory<T> factory) where T : class =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });

    [Fact]
    public async Task Every_public_page_carries_a_unique_description_and_canonical_url()
    {
        using var client = HttpsClient(_publicProduction);
        var descriptions = new Dictionary<string, string>();

        foreach (var path in PublicPages)
        {
            var response = await client.GetAsync(path);
            Assert.True(response.IsSuccessStatusCode,
                $"{path} returned {(int)response.StatusCode} — the metadata cannot be checked on a "
                + "page that does not render");
            var html = await response.Content.ReadAsStringAsync();

            var description = Regex.Match(html, "<meta name=\"description\" content=\"(.*?)\"").Groups[1].Value;
            Assert.False(string.IsNullOrWhiteSpace(description), $"{path} has no meta description");

            var canonical = Regex.Match(html, "<link rel=\"canonical\" href=\"(.*?)\"").Groups[1].Value;
            Assert.Equal(path == "/" ? $"{BaseUrl}/" : BaseUrl + path, canonical);

            // Open Graph decides what a shared link looks like in a message.
            Assert.Contains("property=\"og:title\"", html);
            Assert.Contains("property=\"og:url\"", html);
            Assert.Contains("content=\"sv_SE\"", html);

            descriptions[path] = description;
        }

        // Duplicated descriptions across pages are the classic copy-paste outcome, and search
        // engines treat them as a quality signal — so the uniqueness is asserted, not assumed.
        var duplicates = descriptions.GroupBy(pair => pair.Value)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(pair => pair.Key)))
            .ToArray();

        Assert.True(duplicates.Length == 0, $"pages share a description: {string.Join(" | ", duplicates)}");
    }

    public void Dispose()
    {
        _publicDevelopment.Dispose();
        _publicProduction.Dispose();
        _admin.Dispose();
    }
}
