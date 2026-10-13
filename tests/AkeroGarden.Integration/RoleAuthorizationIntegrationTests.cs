extern alias AdminApp;

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AkeroGarden.Integration;

/// <summary>
/// FR-104 — which admin pages each role may open, asserted at the HTTP level against the real admin
/// host with the real authorization middleware and the real <c>[Authorize(Roles = …)]</c> metadata
/// Blazor copies onto each page endpoint.
///
/// <para><b>What is substituted and what is not.</b> The signed-in principal comes from a test
/// authentication scheme rather than a real login, because issuing a genuine Identity cookie over
/// <see cref="HttpClient"/> means scraping an antiforgery token out of the login form — brittle, and
/// it would test the login page rather than the authorisation matrix. Everything that decides the
/// outcome is real: the endpoints, their role requirements, and the policy evaluation. Denial here
/// therefore surfaces as <c>403</c>; the *redirect* a denied browser user actually sees is a
/// property of the cookie handler, pinned separately by
/// <see cref="AccessDenied_redirect_target_is_the_no_permission_page"/> and walked end-to-end by the
/// Samordnare E2E journey.</para>
///
/// <para>The matrix is exhaustive on purpose. A new admin page that forgets its role attribute is
/// exactly the failure this slice must prevent, and it is invisible in every other kind of test —
/// so every routable page is listed below, in both directions.</para>
/// </summary>
public sealed class RoleAuthorizationIntegrationTests : IClassFixture<DbFixture>, IDisposable
{
    private const string TestScheme = "RoleTest";
    private const string RoleHeader = "X-Test-Role";

    private readonly WebApplicationFactory<AdminApp::Program> _admin;

    public RoleAuthorizationIntegrationTests(DbFixture db)
    {
        _admin = new WebApplicationFactory<AdminApp::Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
                builder.ConfigureTestServices(services =>
                {
                    services.AddAuthentication()
                        .AddScheme<AuthenticationSchemeOptions, RoleHeaderAuthHandler>(TestScheme, _ => { });

                    // AddIdentity sets DefaultAuthenticateScheme and DefaultChallengeScheme to the
                    // Identity cookie *explicitly*, so naming a default on AddAuthentication is not
                    // enough — authentication would still run on the cookie handler and this handler
                    // would never execute. Override all four; the anonymous control test below fails
                    // loudly (302 instead of 401) if this ever stops taking effect.
                    services.Configure<AuthenticationOptions>(o =>
                    {
                        o.DefaultScheme = TestScheme;
                        o.DefaultAuthenticateScheme = TestScheme;
                        o.DefaultChallengeScheme = TestScheme;
                        o.DefaultForbidScheme = TestScheme;
                    });
                });
            });
    }

    /// <summary>Signs the request in as whatever role its <c>X-Test-Role</c> header names; anonymous
    /// without the header. Registered last, so it is the default scheme for this factory only.</summary>
    private sealed class RoleHeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeader, out var role) || string.IsNullOrEmpty(role))
                return Task.FromResult(AuthenticateResult.NoResult());

            // The role claim type must be the one [Authorize(Roles = …)] reads, or every role check
            // silently fails open into "no roles" and the whole matrix would go green for nobody.
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "test@akerogarden.se"), new Claim(ClaimTypes.Role, role!)],
                TestScheme, ClaimTypes.Name, ClaimTypes.Role);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }

    private async Task<HttpStatusCode> GetAsync(string path, string? role)
    {
        using var client = _admin.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (role is not null)
            request.Headers.Add(RoleHeader, role);

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>Every routable admin page except the ones that are deliberately open to both roles.</summary>
    public static TheoryData<string> AdministratorOnlyPages() =>
    [
        "/",                                                  // dashboard
        "/bokningar",
        "/bokningar/ny",
        $"/bokningar/{Guid.NewGuid()}",                       // booking detail — the page D1 exists to block
        "/kalender",
        "/blockeringar",
        "/byggnader",
        "/matalternativ",
        "/faciliteter",
        "/priser",
        "/stadszoner",
        "/konton",
        "/jobbhistorik",                                      // FR-114 — missing from this list until 2026-10-13
        "/handbok",                                           // FR-117 — the full handbook
    ];

    public static TheoryData<string> SharedPages() =>
    [
        "/listor",
        $"/listor/{Guid.NewGuid()}",
        "/handbok/samordnare",                                // FR-117 — Del B, the coordinator's part
    ];

    [Theory]
    [MemberData(nameof(AdministratorOnlyPages))]
    public async Task Administrator_may_open_every_admin_page(string path) =>
        Assert.Equal(HttpStatusCode.OK, await GetAsync(path, AdminRoles.Administrator));

    [Theory]
    [MemberData(nameof(AdministratorOnlyPages))]
    public async Task Coordinator_is_denied_every_administrator_page(string path) =>
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync(path, AdminRoles.Coordinator));

    [Theory]
    [MemberData(nameof(SharedPages))]
    public async Task Both_roles_may_open_the_shared_pages(string path)
    {
        Assert.Equal(HttpStatusCode.OK, await GetAsync(path, AdminRoles.Coordinator));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(path, AdminRoles.Administrator));
    }

    [Theory]
    [MemberData(nameof(SharedPages))]
    [MemberData(nameof(AdministratorOnlyPages))]
    public async Task Anonymous_reaches_no_admin_page(string path)
    {
        // Positive control for the harness itself: without the header the handler returns NoResult,
        // so any 200 below would mean the pages are simply not authorised at all and the role
        // assertions above prove nothing.
        var status = await GetAsync(path, role: null);
        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Theory]
    [InlineData("deltagarlista.pdf")]
    [InlineData("deltagarlista.xlsx")]
    [InlineData("matlista.pdf")]
    [InlineData("matlista.xlsx")]
    public async Task Coordinator_is_allowed_through_to_the_download_endpoints(string file)
    {
        // The booking id is a fresh Guid, so the handler answers 404 — and 404 rather than 403 is the
        // whole point: the role passed authorisation and reached the handler. That the handler then
        // renders a real list is covered by the list-generation tests and the E2E download.
        var status = await GetAsync($"/bokningar/{Guid.NewGuid()}/{file}", AdminRoles.Coordinator);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task Unknown_role_reaches_nothing()
    {
        // Role membership is matched by name. A typo in AdminRoles, or a role added straight into
        // the database, must not inherit access to anything.
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync("/", "Superadmin"));
        Assert.Equal(HttpStatusCode.Forbidden, await GetAsync("/listor", "Superadmin"));
    }

    [Fact]
    public void AccessDenied_redirect_target_is_the_no_permission_page()
    {
        // A denied *browser* request is redirected by the cookie handler rather than 403'd. Pinning
        // the configured path here because it used to be "/login", which showed an already-signed-in
        // user the login form — and nothing else in the suite would notice if it regressed.
        var options = _admin.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.Equal("/ingen-behorighet", options.AccessDeniedPath);
        Assert.Equal("/login", options.LoginPath);
    }

    public void Dispose() => _admin.Dispose();
}
