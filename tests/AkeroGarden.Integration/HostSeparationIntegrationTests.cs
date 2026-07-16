extern alias PublicApp;
extern alias AdminApp;

using System.Net;
using AkeroGarden.Integration.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace AkeroGarden.Integration;

/// <summary>
/// FR-70 (Phase 10b) — proves the public and admin surfaces are two genuinely separate applications,
/// not one app gated at the edge. The load-bearing claim: an admin route on the public host is a real
/// 404 (the code isn't in the process), never an <c>[Authorize]</c> 302 redirect — and vice versa for
/// public routes on the admin host. Asserted at the HTTP level by booting each host in-memory.
///
/// The public host boots with no database or Redis (it migrates nothing and the routing 404s short-
/// circuit before any data access; the rate-limiter connects lazily with AbortOnConnectFail=false).
/// The admin host migrates + seeds on startup, so it is pointed at the ephemeral Postgres from
/// <see cref="DbFixture"/>.
/// </summary>
public sealed class HostSeparationIntegrationTests : IClassFixture<DbFixture>, IDisposable
{
    private readonly WebApplicationFactory<PublicApp::Program> _public;
    private readonly WebApplicationFactory<AdminApp::Program> _admin;

    public HostSeparationIntegrationTests(DbFixture db)
    {
        _public = new WebApplicationFactory<PublicApp::Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment(Environments.Development);
                // The public host never migrates and the asserted routes don't touch data, but wiring a
                // valid connection string keeps DbContext registration unconditional (no null-string edge).
                b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            });

        _admin = new WebApplicationFactory<AdminApp::Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment(Environments.Development);
                // The admin host migrates + seeds on startup — give it the ephemeral schema.
                b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
            });
    }

    private static HttpClient NoRedirectClient<T>(WebApplicationFactory<T> factory) where T : class =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // ── Public host: serves only public routes ────────────────────────────────────────────────

    [Fact]
    public async Task Public_host_serves_a_public_route()
    {
        // Positive control — the public app is actually up (a hardcoded page, no DB dependency).
        using var client = NoRedirectClient(_public);
        var resp = await client.GetAsync("/integritetspolicy");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Theory]
    [InlineData("/admin")]            // admin dashboard — absent from the public assembly
    [InlineData("/admin/bokningar")]  // admin booking list
    [InlineData("/admin/kalender")]   // admin calendar
    [InlineData("/login")]            // Identity — admin-only
    public async Task Public_host_returns_404_for_admin_routes(string path)
    {
        // The core FR-70 claim: a real 404 (route not registered in the process), NOT a 302 redirect
        // to /login. A redirect would mean the admin stack is loaded and merely edge-gated.
        using var client = NoRedirectClient(_public);
        var resp = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Found, resp.StatusCode);
    }

    // ── Admin host: serves only admin routes ──────────────────────────────────────────────────

    [Fact]
    public async Task Admin_host_serves_the_login_page()
    {
        // Positive control — the admin app is up and its Identity surface renders.
        using var client = NoRedirectClient(_admin);
        var resp = await client.GetAsync("/login");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_host_redirects_unauthenticated_admin_route_to_login()
    {
        // The admin route IS present here (unlike on the public host) — an unauthenticated request is
        // auth-gated to /login, proving the difference from the public host's 404 for the same path.
        using var client = NoRedirectClient(_admin);
        var resp = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.Found, resp.StatusCode);
        Assert.Contains("/login", resp.Headers.Location?.OriginalString ?? "");
    }

    // Note: "/" is deliberately NOT here — the admin host maps GET "/" to a redirect to /admin
    // (it has no public home), so it is a 302, not a 404. The routes below are genuine public pages
    // that must be absent from the admin assembly.
    [Theory]
    [InlineData("/boka")]              // inquiry form
    [InlineData("/integritetspolicy")] // privacy policy
    [InlineData("/kalender")]          // public availability calendar
    [InlineData("/om-oss")]            // about
    public async Task Admin_host_returns_404_for_public_routes(string path)
    {
        using var client = NoRedirectClient(_admin);
        var resp = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    public void Dispose()
    {
        _public.Dispose();
        _admin.Dispose();
    }
}
