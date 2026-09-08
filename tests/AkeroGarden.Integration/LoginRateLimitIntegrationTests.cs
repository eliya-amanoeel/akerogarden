extern alias AdminApp;

using System.Net;
using System.Text.RegularExpressions;
using System.Net.Http;
using AkeroGarden.Integration.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace AkeroGarden.Integration;

/// <summary>
/// Proves the admin login form actually consults the per-IP throttle (post-launch security review,
/// finding #8). <c>LoginAttemptLimiterTests</c> covers the limiter's own arithmetic; this covers the
/// part a unit test cannot — that <c>Login.razor</c> calls it, before verifying any password, and that
/// the refusal reaches the browser.
///
/// <para>The distinction matters because the E2E suite runs with the limit raised to 1000 so it does
/// not throttle itself (every admin test signs in from 127.0.0.1). Without this test, "login is rate
/// limited" would be a claim resting on a limiter that nothing proved was wired up.</para>
///
/// <para>The host is booted with a limit of 2 so the boundary is reached in three requests rather than
/// twenty-one.</para>
/// </summary>
public sealed class LoginRateLimitIntegrationTests : IClassFixture<DbFixture>, IDisposable
{
    private const int MaxAttempts = 2;

    private readonly WebApplicationFactory<AdminApp::Program> _admin;

    public LoginRateLimitIntegrationTests(DbFixture db)
    {
        _admin = new WebApplicationFactory<AdminApp::Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment(Environments.Development);
                b.UseSetting("ConnectionStrings:Postgres", db.ConnectionString);
                b.UseSetting("LoginRateLimit:MaxLoginAttempts", MaxAttempts.ToString());
                // A long window, so the test cannot pass by accident through the bucket rolling over
                // between requests.
                b.UseSetting("LoginRateLimit:LoginWindowSeconds", "3600");
            });
    }

    public void Dispose() => _admin.Dispose();

    /// <summary>
    /// Posts the real login form, carrying the antiforgery token and cookies from a preceding GET.
    /// Field names are scraped from the rendered form rather than hardcoded: Blazor derives them from
    /// the model binding path, and a guessed name would silently post nothing and make every attempt
    /// "fail" for the wrong reason — which would make this test pass while proving nothing.
    /// </summary>
    private static async Task<string> PostLoginAsync(HttpClient client, string email, string password)
    {
        var form = await client.GetStringAsync("/login");

        var token = Regex.Match(form, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "no antiforgery token in the rendered login form");

        var emailField = Regex.Match(form, @"<input[^>]*id=""email""[^>]*name=""([^""]+)""").Groups[1].Value;
        var passwordField = Regex.Match(form, @"<input[^>]*id=""password""[^>]*name=""([^""]+)""").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(emailField), "could not find the email field name");
        Assert.False(string.IsNullOrEmpty(passwordField), "could not find the password field name");

        var handler = Regex.Match(form, @"name=""_handler""[^>]*value=""([^""]+)""").Groups[1].Value;

        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            [emailField] = email,
            [passwordField] = password,
        };
        if (!string.IsNullOrEmpty(handler))
            fields["_handler"] = handler;

        var response = await client.PostAsync("/login", new FormUrlEncodedContent(fields));
        var body = await response.Content.ReadAsStringAsync();

        // Decode before returning: Blazor HTML-encodes non-ASCII, so the rendered markup carries
        // "l&#xF6;senord", not "lösenord". Asserting against the raw body silently fails every
        // Contains — and, worse, makes every DoesNotContain pass for the wrong reason.
        return WebUtility.HtmlDecode(body);
    }

    [Fact]
    public async Task Login_is_throttled_per_client_after_the_configured_number_of_attempts()
    {
        using var client = _admin.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        // Attempts within budget: wrong credentials, so the generic failure message — NOT the throttle.
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var body = await PostLoginAsync(client, "nobody@akerogarden.se", "FelLösenord!123");
            Assert.DoesNotContain("För många inloggningsförsök", body);
            Assert.Contains("Felaktig e-postadress eller lösenord", body);
        }

        // One past the budget: refused before any credential check.
        var throttled = await PostLoginAsync(client, "nobody@akerogarden.se", "FelLösenord!123");
        Assert.Contains("För många inloggningsförsök", throttled);
    }

    [Fact]
    public async Task Throttling_does_not_leak_whether_the_account_exists()
    {
        // The whole point of finding #8's other half: a wrong password and an unknown address must be
        // indistinguishable. Asserted here rather than only in the unit tests because it is the
        // rendered response that an attacker actually sees.
        using var client = _admin.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var unknown = await PostLoginAsync(client, "does-not-exist@akerogarden.se", "FelLösenord!123");

        Assert.Contains("Felaktig e-postadress eller lösenord", unknown);
        Assert.DoesNotContain("inaktiverat", unknown);
    }
}
