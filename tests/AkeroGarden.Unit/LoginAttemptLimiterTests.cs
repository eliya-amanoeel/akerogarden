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
        int maxLogin = 3, int loginWindow = 900, int maxReset = 2, int resetWindow = 3600,
        int maxTracked = 5000)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var options = Options.Create(new LoginRateLimitOptions
        {
            MaxLoginAttempts = maxLogin,
            LoginWindowSeconds = loginWindow,
            MaxPasswordResetRequests = maxReset,
            PasswordResetWindowSeconds = resetWindow,
            MaxTrackedClients = maxTracked,
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

    [Fact]
    public void Expired_entries_are_pruned_once_the_tracking_cap_is_passed()
    {
        // Nothing else ever removes an entry, so without this the dictionary grows for the lifetime of
        // the process. The cap is 5000 in production, which means this path would otherwise never run
        // and never be exercised — an unverified safeguard is not a safeguard.
        var (limiter, clock) = Build(maxLogin: 1, loginWindow: 60, maxTracked: 2);

        limiter.TryLogin("198.51.100.1");
        limiter.TryLogin("198.51.100.2");

        // Both entries are now older than the window.
        clock.Advance(TimeSpan.FromSeconds(61));

        // Two more clients push the count past the cap and trigger the sweep.
        limiter.TryLogin("198.51.100.3");
        limiter.TryLogin("198.51.100.4");

        // The originals were pruned, so each gets a fresh budget rather than being remembered as
        // exhausted. Asserting on behaviour rather than on an internal count: the dictionary is
        // private, and what actually matters is that a returning client is not wrongly blocked.
        Assert.True(limiter.TryLogin("198.51.100.1"));
        Assert.True(limiter.TryLogin("198.51.100.2"));
    }

    [Fact]
    public void Pruning_does_not_forgive_a_client_still_inside_its_window()
    {
        // The dangerous failure: a sweep that drops live counters would hand an attacker a reset.
        var (limiter, clock) = Build(maxLogin: 1, loginWindow: 900, maxTracked: 2);

        Assert.True(limiter.TryLogin("198.51.100.9"));
        Assert.False(limiter.TryLogin("198.51.100.9"));

        // Well past the cap, but only a moment later — 198.51.100.9 is still inside its window.
        clock.Advance(TimeSpan.FromSeconds(5));
        limiter.TryLogin("198.51.100.10");
        limiter.TryLogin("198.51.100.11");
        limiter.TryLogin("198.51.100.12");

        Assert.False(limiter.TryLogin("198.51.100.9"));
    }
}
