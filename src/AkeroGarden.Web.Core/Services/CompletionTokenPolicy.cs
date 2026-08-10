using System.Security.Cryptography;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Generation and expiry rules for the booking completion magic-link token (requirements.md
/// §2.6, FR-51/52/53, NFR-03). Pure and unit-testable (NFR-26) — no database, no clock of its
/// own; the caller supplies the issue instant.
/// </summary>
public static class CompletionTokenPolicy
{
    /// <summary>Token lifetime ceiling from issue (§2.6).</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(30);

    /// <summary>Days before check-in at which the token stops being valid — the operational deadline,
    /// since the kitchen and cleaning need the details by then (§2.6). Also the point at or after
    /// which an issue counts as <i>late</i> and falls back to <see cref="LateIssueLeadTime"/>.</summary>
    public const int CheckInLeadDays = 14;

    /// <summary>
    /// <see cref="CheckInLeadDays"/> as an offset, for computing the cut-off instant.
    /// </summary>
    public static readonly TimeSpan CheckInLeadTime = TimeSpan.FromDays(CheckInLeadDays);

    /// <summary>Minimum validity floor from issue, regardless of how close check-in is (§2.6).</summary>
    public static readonly TimeSpan MinLifetimeFloor = TimeSpan.FromHours(48);

    /// <summary>
    /// How far before check-in the link is issued (§2.6, FR-108). Approving further ahead than this
    /// issues no token at all; a daily job issues one when check-in comes within this window.
    ///
    /// <para><b>40 is load-bearing, not a round number.</b> <see cref="MaxLifetime"/> only outlives
    /// <see cref="CheckInLeadTime"/> while the link is issued within <b>44</b> days of check-in
    /// (issue + 30 ≥ check-in − 14). Past that the 30-day ceiling binds first and the link dies while
    /// the guest is still waiting — the exact gap deferred issuance exists to close. 40 sits four days
    /// inside that boundary so the daily job has room to fire without the rule resting on its edge.
    /// <b>Do not raise this toward 44, and do not lower <see cref="MaxLifetime"/> without lowering
    /// this in step.</b></para>
    ///
    /// <para>Counted in whole days rather than as an instant offset: check-in is a date, and an
    /// instant comparison against the standard 14:00 check-in time makes the answer depend on the
    /// hour the job happens to run — the 03:00 sweep would quietly issue at 39 days, not 40. FR-75
    /// already counts the distance to check-in this way.</para>
    /// </summary>
    public const int IssueLeadDays = 40;

    /// <summary>
    /// How long before check-in a <b>late-issued</b> link stops working (§2.6, FR-53). When the link
    /// is issued <see cref="CheckInLeadDays"/> days or fewer before check-in, the normal cut-off is
    /// today or already past and cannot serve as the deadline, so the link runs to this point
    /// instead. A guest approved ten days out then gets a week to collect everyone's names and
    /// allergies rather than the 48 hours the bare floor gave.
    ///
    /// <para>This covers the completion nudge as well as a late approval, which is the reason the
    /// comparison is on whole days. The nudge fires at 03:00 on the 14-days-before date; against the
    /// 14:00 check-in instant that reads as eleven hours <i>early</i>, so it took the normal branch,
    /// computed an expiry the same afternoon and fell through to the 48-hour floor. The link a guest
    /// is chased with then lasted two days, while a guest approved one day later got eleven — and
    /// which of the two you got depended on the hour the job happened to run.</para>
    /// </summary>
    public static readonly TimeSpan LateIssueLeadTime = TimeSpan.FromDays(3);

    /// <summary>Standard check-in time (§2.8) — the instant "14 days before check-in" counts from.
    /// Deliberately the <b>standard</b> time and not the booking's own stated one (FR-100): link
    /// expiry is an administrative deadline, and letting a guest's 08:00 arrival quietly shorten
    /// their window would be a surprising side effect of an unrelated field. Aliased rather than
    /// redeclared so there is one definition of "14:00" in the system.</summary>
    public static readonly TimeOnly StandardCheckInTime = MealOccasionResolver.StandardCheckInTime;

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
    /// True when a completion link should be issued: check-in is <see cref="IssueLeadDays"/> days
    /// away or sooner (§2.6, FR-51/108). False means the booking is too far out to hand the guest a
    /// form they cannot yet fill in — approval issues no token and the daily issuance job picks it
    /// up later.
    /// </summary>
    /// <param name="today">The day being judged.</param>
    /// <param name="checkIn">The stay's check-in date.</param>
    public static bool ShouldIssueOn(DateOnly today, DateOnly checkIn) =>
        checkIn.DayNumber - today.DayNumber <= IssueLeadDays;

    /// <summary>
    /// Computes the token expiry instant (§2.6, FR-53):
    /// <list type="bullet">
    ///   <item>issued more than <see cref="CheckInLeadDays"/> days before check-in — the earlier of
    ///   (issue + 30 days) and (14 days before check-in);</item>
    ///   <item>issued <see cref="CheckInLeadDays"/> days or fewer before check-in — 3 days before
    ///   check-in, since the cut-off it would otherwise use is today or already past. This is the
    ///   late approval <i>and</i> the completion nudge;</item>
    /// </list>
    /// and in neither case sooner than (issue + 48 hours), so a link issued on the eve of a stay is
    /// still usable.
    ///
    /// <para>The branch is decided on <b>whole days</b>, not by comparing instants against the 14:00
    /// standard check-in time — see <see cref="LateIssueLeadTime"/> for what that cost.</para>
    /// </summary>
    /// <param name="issuedAtUtc">When the token is issued (UTC).</param>
    /// <param name="checkIn">The stay's check-in date; combined with the standard 14:00 check-in time.</param>
    public static DateTime ComputeExpiry(DateTime issuedAtUtc, DateOnly checkIn)
    {
        var checkInInstant = checkIn.ToDateTime(StandardCheckInTime, DateTimeKind.Utc);
        var floor = issuedAtUtc + MinLifetimeFloor;
        var daysToCheckIn = checkIn.DayNumber - DateOnly.FromDateTime(issuedAtUtc).DayNumber;

        DateTime target;
        if (daysToCheckIn <= CheckInLeadDays)
        {
            target = checkInInstant - LateIssueLeadTime;
        }
        else
        {
            var thirtyDayCeiling = issuedAtUtc + MaxLifetime;
            var fourteenDaysBeforeCheckIn = checkInInstant - CheckInLeadTime;
            target = thirtyDayCeiling < fourteenDaysBeforeCheckIn
                ? thirtyDayCeiling
                : fourteenDaysBeforeCheckIn;
        }

        return target > floor ? target : floor;
    }

    /// <summary>
    /// True while the token is valid: not expired at <paramref name="nowUtc"/> (valid up to,
    /// and rejected at or after, the computed expiry instant — §2.6).
    /// </summary>
    public static bool IsValidAt(DateTime expiryUtc, DateTime nowUtc) => nowUtc < expiryUtc;
}
