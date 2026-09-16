extern alias AdminApp;

using System.Net;
using System.Text.RegularExpressions;
using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AkeroGarden.Integration;

/// <summary>
/// The half of FR-74 the browser test cannot reach: an admin whose account is deactivated is refused
/// on their next REQUEST, using the auth cookie they already hold.
///
/// <para>Two mechanisms end such a session, and they are independent. The E2E suite covers the open
/// Blazor tab (<c>SessionWatch</c> + the revalidating auth-state provider). This covers the cookie:
/// Identity's security-stamp validator re-checking the signed-in principal against the database on
/// <c>SecurityStampValidatorOptions.ValidationInterval</c>. Most people meet this path rather than the
/// other one — they navigate, they don't sit on an idle tab.</para>
/// </summary>
public sealed class AdminSessionRevalidationIntegrationTests : IClassFixture<DbFixture>, IDisposable
{
    private readonly DbFixture _db;
    private readonly WebApplicationFactory<AdminApp::Program> _admin;

    public AdminSessionRevalidationIntegrationTests(DbFixture db)
    {
        _db = db;
        _admin = new WebApplicationFactory<AdminApp::Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment(Environments.Development);
                b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
                // One second, so the re-check is reached without the test sleeping for a minute. The
                // production value is asserted separately below — shortening it here would otherwise
                // be indistinguishable from shipping a one-second interval.
                b.UseSetting("AdminSession:RevalidationSeconds", "1");
                // The admin host attributes audit rows to the signed-in admin by reading the Blazor
                // circuit's auth state; deactivating from a plain DI scope (as this test does, rather
                // than clicking the button) has no circuit and throws. Who the audit row names is not
                // what this test proves — that the cookie stops working is — so the system actor
                // stands in, exactly as the background jobs use it.
                b.ConfigureTestServices(s => s.AddScoped<IAuditActorAccessor, SystemAuditActorAccessor>());
            });
    }

    public void Dispose() => _admin.Dispose();

    [Fact]
    public async Task A_deactivated_admin_is_refused_on_the_next_request_with_the_cookie_they_already_hold()
    {
        var email = $"{Guid.NewGuid():N}@akerogarden.se";
        const string password = "Integration-Revalidate-1!";

        using var scope = _admin.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var invites = scope.ServiceProvider.GetRequiredService<AdminInviteService>();
        await DbFixture.EnsureAdminRolesAsync(scope.ServiceProvider);

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = "Ingrid",
            LastName = "Integration",
            PhoneNumber = "070-000 00 00",
            IsActive = true,
            SetupCompletedAt = DateTime.UtcNow,
            EmailConfirmed = true,
        };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        await users.AddToRoleAsync(user, AdminRoles.Administrator);

        using var client = _admin.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await SignInAsync(client, email, password);

        // Signed in: the dashboard renders rather than redirecting. Without this the assertion below
        // could pass on a session that never worked.
        var before = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        // Deactivated through the real service — which is what rotates the security stamp.
        await invites.SetAccountActiveAsync(user, false);

        // The cookie is unchanged and still unexpired; only the account behind it changed. Poll rather
        // than sleep once: the validator's interval is wall-clock, and a single fixed wait would make
        // this test's failure mode "too slow" rather than "not refused".
        var deadline = DateTime.UtcNow.AddSeconds(20);
        HttpResponseMessage response;
        do
        {
            await Task.Delay(500);
            response = await client.GetAsync("/");
        }
        while (response.StatusCode == HttpStatusCode.OK && DateTime.UtcNow < deadline);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.OriginalString ?? "");
    }

    [Fact]
    public void The_shipped_revalidation_interval_is_one_minute()
    {
        // Not a timing test — it cannot be. It is a guard against the value drifting: a test interval
        // left in a config file, or a default edited to something that quietly widens the window in
        // which a deactivated admin still has access (FR-74). Booted WITHOUT the override above, so
        // this reads what production reads.
        using var app = new WebApplicationFactory<AdminApp::Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment(Environments.Development);
                b.UseSetting("ConnectionStrings:Postgres", _db.ConnectionString);
            });

        using var scope = app.Services.CreateScope();
        var session = scope.ServiceProvider.GetRequiredService<IOptions<AdminApp::AkeroGarden.Web.Admin.Services.AdminSessionOptions>>();
        var stamp = scope.ServiceProvider.GetRequiredService<IOptions<SecurityStampValidatorOptions>>();

        Assert.Equal(60, session.Value.RevalidationSeconds);
        // The cookie half must move with it; the framework default is 30 minutes.
        Assert.Equal(TimeSpan.FromSeconds(60), stamp.Value.ValidationInterval);
    }

    /// <summary>Posts the real login form, carrying its antiforgery token and cookies (same approach as
    /// LoginRateLimitIntegrationTests — field names are scraped, not guessed).</summary>
    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var form = await client.GetStringAsync("/login");
        var token = Regex.Match(form, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""").Groups[1].Value;
        var emailField = Regex.Match(form, @"<input[^>]*id=""email""[^>]*name=""([^""]+)""").Groups[1].Value;
        var passwordField = Regex.Match(form, @"<input[^>]*id=""password""[^>]*name=""([^""]+)""").Groups[1].Value;
        var handler = Regex.Match(form, @"name=""_handler""[^>]*value=""([^""]+)""").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "no antiforgery token in the rendered login form");

        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            [emailField] = email,
            [passwordField] = password,
        };
        if (!string.IsNullOrEmpty(handler))
            fields["_handler"] = handler;

        var response = await client.PostAsync("/login", new FormUrlEncodedContent(fields));
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.OK,
            $"login POST returned {(int)response.StatusCode}");
    }
}
