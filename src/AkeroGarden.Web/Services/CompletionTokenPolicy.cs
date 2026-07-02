using System.Security.Cryptography;

namespace AkeroGarden.Web.Services;

/// <summary>
/// Generation and expiry rules for the booking completion magic-link token (requirements.md
/// §2.6, FR-51/52/53, NFR-03). Pure and unit-testable (NFR-26) — no database, no clock of its
/// own; the caller supplies the issue instant.
/// </summary>
public static class CompletionTokenPolicy
{
    /// <summary>Token lifetime ceiling from issue (§2.6).</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(30);

    /// <summary>Lead time before check-in at which the token stops being valid (§2.6).</summary>
    public static readonly TimeSpan CheckInLeadTime = TimeSpan.FromDays(14);

    /// <summary>Minimum validity floor from issue, regardless of how close check-in is (§2.6).</summary>
    public static readonly TimeSpan MinLifetimeFloor = TimeSpan.FromHours(48);

    /// <summary>Standard check-in time (§2.8) — the instant "14 days before check-in" counts from.</summary>
    public static readonly TimeOnly StandardCheckInTime = new(14, 0);

    /// <summary>
    /// Generates a cryptographically random, URL-safe token with at least 128 bits of entropy
    /// (NFR-03). 32 bytes → 256 bits, base64url-encoded without padding.
    /// </summary>
    public static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// Computes the token expiry instant: the earlier of (issue + 30 days) and
    /// (14 days before check-in), but never sooner than (issue + 48 hours) — so a token issued
    /// when check-in is already close is still valid for at least 48 hours (§2.6, FR-53).
    /// </summary>
    /// <param name="issuedAtUtc">When the token is issued (UTC).</param>
    /// <param name="checkIn">The stay's check-in date; combined with the standard 14:00 check-in time.</param>
    public static DateTime ComputeExpiry(DateTime issuedAtUtc, DateOnly checkIn)
    {
        var checkInInstant = checkIn.ToDateTime(StandardCheckInTime, DateTimeKind.Utc);

        var thirtyDayCeiling = issuedAtUtc + MaxLifetime;
        var fourteenDaysBeforeCheckIn = checkInInstant - CheckInLeadTime;
        var floor = issuedAtUtc + MinLifetimeFloor;

        var earliest = thirtyDayCeiling < fourteenDaysBeforeCheckIn
            ? thirtyDayCeiling
            : fourteenDaysBeforeCheckIn;

        return earliest > floor ? earliest : floor;
    }

    /// <summary>
    /// True while the token is valid: not expired at <paramref name="nowUtc"/> (valid up to,
    /// and rejected at or after, the computed expiry instant — §2.6).
    /// </summary>
    public static bool IsValidAt(DateTime expiryUtc, DateTime nowUtc) => nowUtc < expiryUtc;
}
