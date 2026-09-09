using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace AkeroGarden.Web.Core.Services;

/// <summary>Limits for the admin host's unauthenticated forms (post-launch security review, finding
/// #8). Bound from the <c>LoginRateLimit</c> configuration section.</summary>
public class LoginRateLimitOptions
{
    /// <summary>Sign-in attempts allowed per client IP per <see cref="LoginWindowSeconds"/>.
    /// Deliberately looser than Identity's per-account lockout (5): this exists to stop password
    /// spraying across many accounts, not to re-implement lockout, and admins may share an office
    /// IP.</summary>
    public int MaxLoginAttempts { get; set; } = 20;

    /// <summary>Sign-in window in seconds.</summary>
    public int LoginWindowSeconds { get; set; } = 900;

    /// <summary>Password-reset requests allowed per client IP per
    /// <see cref="PasswordResetWindowSeconds"/>. Much stricter than sign-in because each request
    /// sends an email: unthrottled, this is an unauthenticated mail-flooding primitive against a
    /// known admin address, and it shares the One.com SMTP account with guest booking mail — so
    /// abusing it degrades the public booking funnel too.</summary>
    public int MaxPasswordResetRequests { get; set; } = 5;

    /// <summary>Password-reset window in seconds.</summary>
    public int PasswordResetWindowSeconds { get; set; } = 3600;

    /// <summary>
    /// How many client entries may accumulate before expired ones are pruned. Only real attempts
    /// against the two unauthenticated admin forms create entries, so on this site the dictionary
    /// stays tiny — but nothing else ever removes an entry, and a long-running process must not grow
    /// without bound.
    ///
    /// <para>Configurable purely so the pruning path can be driven by a test. A default of 5000 means
    /// it effectively never runs in production, and an unexercised branch that also never executes is
    /// a liability rather than a safeguard.</para>
    /// </summary>
    public int MaxTrackedClients { get; set; } = 5000;
}

/// <summary>
/// Throttles the admin host's two unauthenticated forms by client IP.
///
/// <para><strong>Why this is in-memory rather than the Redis limiter the public host uses.</strong>
/// The shape is deliberately the same — a limiter keyed by client id, fixed window, one call per
/// attempt — but <see cref="RedisInquiryRateLimiter"/> is paired with a documented decision to
/// <em>fail open</em>: a Redis outage must never block a legitimate booking. That is right for a public
/// inquiry form and wrong here, because it would mean a Redis outage silently removing brute-force
/// protection from the one form standing in front of guest data. Sharing the component would have made
/// the mechanism consistent at the cost of making the failure mode inconsistent with the risk.</para>
///
/// <para>In-memory counting is exact while the admin plan runs a single worker, which it does
/// (<c>numberOfWorkers: 1</c>). If admin is ever scaled out, each instance would count separately and
/// the effective limit multiplies by the instance count — at which point this should move to the
/// shared store, not be silently tolerated.</para>
///
/// <para>Synchronous on purpose: there is no I/O here, and wrapping it in <see cref="Task"/> would be
/// async-over-sync for the sake of matching a signature.</para>
/// </summary>
public sealed class LoginAttemptLimiter(IOptions<LoginRateLimitOptions> options, TimeProvider clock)
{
    private readonly LoginRateLimitOptions _opts = options.Value;
    private readonly ConcurrentDictionary<string, Counter> _counters = new();

    private sealed class Counter
    {
        public DateTimeOffset WindowStart;
        public int Count;
    }

    /// <summary>True if this client may attempt a sign-in now.</summary>
    public bool TryLogin(string clientId) =>
        TryAcquire($"login:{clientId}", _opts.MaxLoginAttempts,
            TimeSpan.FromSeconds(_opts.LoginWindowSeconds));

    /// <summary>True if this client may request a password-reset email now.</summary>
    public bool TryPasswordReset(string clientId) =>
        TryAcquire($"reset:{clientId}", _opts.MaxPasswordResetRequests,
            TimeSpan.FromSeconds(_opts.PasswordResetWindowSeconds));

    private bool TryAcquire(string key, int max, TimeSpan window)
    {
        var now = clock.GetUtcNow();
        var allowed = true;

        _counters.AddOrUpdate(
            key,
            _ => new Counter { WindowStart = now, Count = 1 },
            (_, existing) =>
            {
                lock (existing)
                {
                    // Fixed window, matching the Redis limiter's semantics so the two behave alike.
                    if (now - existing.WindowStart >= window)
                    {
                        existing.WindowStart = now;
                        existing.Count = 1;
                    }
                    else
                    {
                        existing.Count++;
                        allowed = existing.Count <= max;
                    }
                }

                return existing;
            });

        // Entries are only created by real attempts against an unauthenticated admin form, so this
        // dictionary stays tiny — but it must not grow without bound over a long-running process, and
        // nothing else ever removes an entry.
        if (_counters.Count > _opts.MaxTrackedClients)
            PruneExpired(now);

        return allowed;
    }

    private void PruneExpired(DateTimeOffset now)
    {
        var longestWindow = TimeSpan.FromSeconds(
            Math.Max(_opts.LoginWindowSeconds, _opts.PasswordResetWindowSeconds));

        foreach (var (key, counter) in _counters)
        {
            if (now - counter.WindowStart >= longestWindow)
                _counters.TryRemove(key, out _);
        }
    }
}
