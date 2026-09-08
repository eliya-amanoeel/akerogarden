extern alias PublicApp;
extern alias AdminApp;

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AkeroGarden.Integration.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace AkeroGarden.Integration;

/// <summary>
/// The security headers as actually served, on both hosts (Phase 11).
///
/// <para>The load-bearing test here is <see cref="Csp_script_src_hash_matches_the_rendered_import_map"/>.
/// Blazor's <c>&lt;ImportMap /&gt;</c> emits the one inline script either host produces, and its
/// content carries the static-asset fingerprints — so the hash in <c>script-src</c> has to be
/// computed at runtime from the framework's own <c>ImportMapDefinition</c>. That computation
/// reproduces what the framework will render, which is exactly the kind of coupling that breaks
/// silently on a framework upgrade: the header stays well-formed, the page stops loading its
/// modules. This test hashes the import map the server *actually rendered* and asserts the header
/// contains that value, so drift fails CI instead of production.</para>
///
/// <para>It has already earned its place once: the first implementation resolved
/// <c>ImportMapDefinition</c> from the root service provider, which returns nothing — the asset
/// collection lives in endpoint metadata, not DI — and shipped a <c>script-src 'self'</c> that would
/// have blocked the import map on every page.</para>
/// </summary>
public sealed class SecurityHeadersIntegrationTests : IClassFixture<DbFixture>, IDisposable
{
    private readonly WebApplicationFactory<PublicApp::Program> _public;
    private readonly WebApplicationFactory<AdminApp::Program> _admin;

    public SecurityHeadersIntegrationTests(DbFixture db)
    {
        _public = new WebApplicationFactory<PublicApp::Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment(Environments.Development);
                b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            });

        _admin = new WebApplicationFactory<AdminApp::Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment(Environments.Development);
                b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            });
    }

    private static readonly string[] ExpectedHeaders =
    [
        "Content-Security-Policy",
        "X-Content-Type-Options",
        "X-Frame-Options",
        "Referrer-Policy",
        "Permissions-Policy",
    ];

    [Fact]
    public async Task Public_host_sends_every_security_header()
    {
        using var client = _public.CreateClient();
        var response = await client.GetAsync("/integritetspolicy");

        foreach (var header in ExpectedHeaders)
            Assert.True(response.Headers.Contains(header), $"missing {header}");
    }

    [Fact]
    public async Task Admin_host_sends_every_security_header()
    {
        using var client = _admin.CreateClient();
        var response = await client.GetAsync("/login");

        foreach (var header in ExpectedHeaders)
            Assert.True(response.Headers.Contains(header), $"missing {header}");
    }

    [Fact]
    public async Task Headers_are_present_on_a_404_too()
    {
        // The middleware is registered before routing precisely so error and status-code responses
        // carry the policy as well — a 404 body is still a rendered page on this app.
        using var client = _public.CreateClient();
        var response = await client.GetAsync("/den-har-sidan-finns-inte");

        Assert.True(response.Headers.Contains("Content-Security-Policy"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Csp_script_src_hash_matches_the_rendered_import_map(bool admin)
    {
        using var client = admin ? _admin.CreateClient() : _public.CreateClient();
        var response = await client.GetAsync(admin ? "/login" : "/integritetspolicy");
        var html = await response.Content.ReadAsStringAsync();

        var importMap = Regex.Match(html, "<script type=\"importmap\">(.*?)</script>", RegexOptions.Singleline);
        Assert.True(importMap.Success, "the page rendered no import map — if <ImportMap /> was removed "
            + "on purpose, this test and the script-src hash should go with it");

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(importMap.Groups[1].Value));
        var expected = $"'sha256-{Convert.ToBase64String(digest)}'";

        Assert.Contains(response.Headers.GetValues("Content-Security-Policy"),
            csp => csp.Contains(expected));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Csp_never_admits_inline_script(bool admin)
    {
        using var client = admin ? _admin.CreateClient() : _public.CreateClient();
        var response = await client.GetAsync(admin ? "/login" : "/integritetspolicy");

        foreach (var csp in response.Headers.GetValues("Content-Security-Policy"))
        {
            var scriptSrc = Regex.Match(csp, "script-src [^;]*").Value;
            Assert.DoesNotContain("'unsafe-inline'", scriptSrc);
            Assert.DoesNotContain("'unsafe-eval'", scriptSrc);
        }
    }

    [Fact]
    public async Task Csp_forbids_third_party_resources_by_default()
    {
        // This is NFR-35's enforcement mechanism. Before it, the requirement ("no resource may load
        // from a third-party host while a page renders") was upheld by convention plus one E2E test
        // on one page — nothing stopped the next edit from adding a font CDN or an embedded map.
        using var client = _public.CreateClient();
        var response = await client.GetAsync("/kontakt");
        var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));

        Assert.Contains("default-src 'self'", csp);
        Assert.DoesNotContain("https://", csp);   // no host has been allow-listed
        Assert.DoesNotContain("*", csp.Replace("ws://localhost:*", "").Replace("wss://localhost:*", ""));
    }

    [Fact]
    public async Task Admin_frame_ancestors_agree_across_both_csp_headers()
    {
        // Blazor Server emits its own Content-Security-Policy header carrying only frame-ancestors,
        // so an interactive admin page answers with two. Browsers apply both and take the
        // intersection, so the framework default ('self') never actually weakened ours ('none') —
        // but two headers disagreeing about the same directive is a trap for whoever reads them
        // next, so CircuitOptions is configured to match. This asserts they still agree.
        using var client = _admin.CreateClient();
        var response = await client.GetAsync("/login");

        var policies = response.Headers.GetValues("Content-Security-Policy").ToList();
        Assert.All(policies, csp =>
        {
            if (csp.Contains("frame-ancestors"))
                Assert.Contains("frame-ancestors 'none'", csp);
        });
    }

    public void Dispose()
    {
        _public.Dispose();
        _admin.Dispose();
    }
}
