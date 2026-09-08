using AkeroGarden.Web.Core.Services;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AkeroGarden.Unit;

/// <summary>
/// Per-IP throttling of the admin host's unauthenticated forms (post-launch security review, finding
/// #8). Identity's lockout is per <em>account</em>, so it does nothing against password spraying across
/// many accounts and lets an attacker lock a named admin out at will; this bounds attempts per source.
///
/// <para>The clock is an input, never ambient — every test here drives a <see cref="FakeTimeProvider"/>
/// rather than sleeping, so the window boundary is asserted exactly instead of approximately.</para>
/// </summary>
public class LoginAttemptLimiterTests
{
    private static (LoginAttemptLimiter Limiter, FakeTimeProvider Clock) Build(
        int maxLogin = 3, int loginWindow = 900, int maxReset = 2, int resetWindow = 3600)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = Options.Create(new LoginRateLimitOptions
        {
            MaxLoginAttempts = maxLogin,
            LoginWindowSeconds = loginWindow,
            MaxPasswordResetRequests = maxReset,
            PasswordResetWindowSeconds = resetWindow,
        });
        return (new LoginAttemptLimiter(options, clock), clock);
    }

    [Fact]
    public void Login_allows_up_to_the_limit_then_blocks()
    {
        var (limiter, _) = Build(maxLogin: 3);

        Assert.True(limiter.TryLogin("198.51.100.1"));
        Assert.True(limiter.TryLogin("198.51.100.1"));
        Assert.True(limiter.TryLogin("198.51.100.1"));
        Assert.False(limiter.TryLogin("198.51.100.1"));
        Assert.False(limiter.TryLogin("198.51.100.1"));
    }

    [Fact]
    public void Login_limit_is_per_client()
    {
        var (limiter, _) = Build(maxLogin: 1);

        Assert.True(limiter.TryLogin("198.51.100.1"));
        Assert.False(limiter.TryLogin("198.51.100.1")); // this client is exhausted
        Assert.True(limiter.TryLogin("198.51.100.2"));  // a different client is unaffected
    }

    [Fact]
    public void Login_window_reopens_after_it_elapses()
    {
        var (limiter, clock) = Build(maxLogin: 1, loginWindow: 900);

        Assert.True(limiter.TryLogin("198.51.100.1"));
        Assert.False(limiter.TryLogin("198.51.100.1"));

        // One second short of the window — still blocked. Asserting the boundary from both sides is
        // the point: an off-by-one here either locks admins out for twice as long as intended or
        // halves the protection.
        clock.Advance(TimeSpan.FromSeconds(899));
        Assert.False(limiter.TryLogin("198.51.100.1"));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limiter.TryLogin("198.51.100.1"));
    }

    [Fact]
    public void PasswordReset_has_its_own_stricter_budget()
    {
        var (limiter, _) = Build(maxLogin: 3, maxReset: 2);

        Assert.True(limiter.TryPasswordReset("198.51.100.1"));
        Assert.True(limiter.TryPasswordReset("198.51.100.1"));
        Assert.False(limiter.TryPasswordReset("198.51.100.1"));
    }

    [Fact]
    public void PasswordReset_and_login_budgets_do_not_share_a_bucket()
    {
        // Each reset request sends an email, so it is throttled far harder than a sign-in attempt.
        // If the two shared a counter, exhausting one would lock the admin out of the other — an
        // attacker could block password recovery by hammering the login form.
        var (limiter, _) = Build(maxLogin: 1, maxReset: 1);

        Assert.True(limiter.TryLogin("198.51.100.1"));
        Assert.False(limiter.TryLogin("198.51.100.1"));

        Assert.True(limiter.TryPasswordReset("198.51.100.1"));
        Assert.False(limiter.TryPasswordReset("198.51.100.1"));
    }

    [Fact]
    public void Unknown_client_is_still_throttled()
    {
        // RemoteIpAddress can be null; the caller passes "unknown". That bucket must not be a bypass.
        var (limiter, _) = Build(maxLogin: 2);

        Assert.True(limiter.TryLogin("unknown"));
        Assert.True(limiter.TryLogin("unknown"));
        Assert.False(limiter.TryLogin("unknown"));
    }
}
