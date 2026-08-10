using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the completion-token expiry and generation rules of §2.6 / FR-53 and
/// NFR-03 (NFR-26).
/// </summary>
public class CompletionTokenPolicyTests
{
    private static readonly DateTime Issued = new(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FarFutureCheckIn_ExpiresAt30DayCeiling()
    {
        // Check-in far off → the 30-day ceiling is the earliest, well before "14 days before check-in".
        var checkIn = new DateOnly(2026, 12, 1);
        var expiry = CompletionTokenPolicy.ComputeExpiry(Issued, checkIn);
        Assert.Equal(Issued.AddDays(30), expiry);
    }

    [Fact]
    public void NearCheckIn_ExpiresAt14DaysBeforeCheckIn()
    {
        // Check-in 20 days out → 14 days before check-in (at 14:00) is earlier than the 30-day
        // ceiling and later than the 48h floor.
        var checkIn = new DateOnly(2026, 7, 21);
        var expiry = CompletionTokenPolicy.ComputeExpiry(Issued, checkIn);
        Assert.Equal(new DateTime(2026, 7, 7, 14, 0, 0, DateTimeKind.Utc), expiry);
    }

    [Fact]
    public void LateIssue_RunsToThreeDaysBeforeCheckIn()
    {
        // Check-in 10 days out → "14 days before check-in" is already past, so the link runs to
        // 3 days before check-in (FR-53) rather than dying 48 hours after issue. This is the whole
        // point of the late-issue window: a week to collect names and allergies, not two days.
        var checkIn = new DateOnly(2026, 7, 11);
        var expiry = CompletionTokenPolicy.ComputeExpiry(Issued, checkIn);

        Assert.Equal(new DateTime(2026, 7, 8, 14, 0, 0, DateTimeKind.Utc), expiry);
        Assert.True(expiry > Issued.AddHours(48), "the late-issue window must beat the bare 48h floor");
    }

    [Theory]
    [InlineData(2)]    // the nightly job's real 03:00 Stockholm sweep, in UTC
    [InlineData(13)]   // just before the 14:00 check-in instant
    [InlineData(15)]   // just after it
    public void IssuedOnTheCutOffDate_RunsToThreeDaysBeforeCheckIn_WhateverTheHour(int hour)
    {
        // The completion nudge fires at 03:00 on the 14-days-before date. Comparing that instant
        // against the 14:00 check-in time read as eleven hours *early*, so it took the normal branch,
        // expired the same afternoon and fell to the 48-hour floor — a two-day link for the guest
        // being chased, where a guest approved a day later got eleven. Whole days, so the hour the
        // job runs cannot decide it (FR-53).
        var checkIn = new DateOnly(2026, 7, 21);
        var issued = checkIn.AddDays(-CompletionTokenPolicy.CheckInLeadDays)
            .ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc);

        var expiry = CompletionTokenPolicy.ComputeExpiry(issued, checkIn);

        Assert.Equal(new DateTime(2026, 7, 18, 14, 0, 0, DateTimeKind.Utc), expiry);
        Assert.True(expiry - issued > TimeSpan.FromDays(10), "the nudge link must be usable, not a 48h stub");
    }

    [Fact]
    public void IssuedTheDayBeforeTheCutOff_StillUsesTheNormalCutOff()
    {
        // The other side of the boundary: 15 days out is not late, so the operational 14-day deadline
        // still governs and the 48-hour floor carries what is left of it.
        var checkIn = new DateOnly(2026, 7, 21);
        var issued = checkIn.AddDays(-15).ToDateTime(new TimeOnly(2, 0), DateTimeKind.Utc);

        var expiry = CompletionTokenPolicy.ComputeExpiry(issued, checkIn);

        Assert.Equal(issued.AddHours(48), expiry);
    }

    [Fact]
    public void VeryCloseCheckIn_IsFlooredTo48Hours()
    {
        // Check-in only 4 days out → even "3 days before check-in" is less than 48 hours away,
        // so the floor takes over and keeps the token usable (FR-53).
        var checkIn = new DateOnly(2026, 7, 5);
        var expiry = CompletionTokenPolicy.ComputeExpiry(Issued, checkIn);
        Assert.Equal(Issued.AddHours(48), expiry);
    }

    [Fact]
    public void CheckInInThePast_IsFlooredTo48Hours()
    {
        var checkIn = new DateOnly(2026, 6, 1);
        var expiry = CompletionTokenPolicy.ComputeExpiry(Issued, checkIn);
        Assert.Equal(Issued.AddHours(48), expiry);
    }

    [Fact]
    public void ShouldIssueOn_TrueAtExactly40Days_FalseBeyond()
    {
        var today = DateOnly.FromDateTime(Issued);

        Assert.True(CompletionTokenPolicy.ShouldIssueOn(today, today.AddDays(40)));   // the edge itself
        Assert.True(CompletionTokenPolicy.ShouldIssueOn(today, today.AddDays(7)));
        Assert.True(CompletionTokenPolicy.ShouldIssueOn(today, today));               // check-in today
        Assert.False(CompletionTokenPolicy.ShouldIssueOn(today, today.AddDays(41)));
        Assert.False(CompletionTokenPolicy.ShouldIssueOn(today, today.AddDays(180)));
    }

    [Theory]
    [InlineData(0)]   // 00:00 — the job's real 03:00 Stockholm sweep lands near here
    [InlineData(12)]
    [InlineData(23)]
    public void ShouldIssueOn_DoesNotDependOnTheHourOfDay(int hour)
    {
        // Counting instants against the 14:00 standard check-in time would make a 03:00 sweep issue
        // at 39 days and a 15:00 one at 40. The rule is whole days, so the hour cannot matter.
        var now = new DateTime(2026, 7, 1, hour, 0, 0, DateTimeKind.Utc);
        var today = DateOnly.FromDateTime(now);

        Assert.True(CompletionTokenPolicy.ShouldIssueOn(today, today.AddDays(CompletionTokenPolicy.IssueLeadDays)));
        Assert.False(CompletionTokenPolicy.ShouldIssueOn(today, today.AddDays(CompletionTokenPolicy.IssueLeadDays + 1)));
    }

    [Fact]
    public void LinkIssuedAtTheLeadTime_SurvivesToTheNudge()
    {
        // The reason the lead time is 40 and not 44: a link issued the moment the job first sees the
        // booking must still be alive at the 14-day nudge that replaces it. If this fails, the
        // dead-link gap deferred issuance exists to close has been reopened (§2.6).
        var checkIn = DateOnly.FromDateTime(Issued).AddDays(CompletionTokenPolicy.IssueLeadDays);
        var expiry = CompletionTokenPolicy.ComputeExpiry(Issued, checkIn);
        var nudgeInstant = checkIn.ToDateTime(CompletionTokenPolicy.StandardCheckInTime, DateTimeKind.Utc)
                           - CompletionTokenPolicy.CheckInLeadTime;

        Assert.True(CompletionTokenPolicy.IsValidAt(expiry, nudgeInstant.AddSeconds(-1)),
            $"a link issued {CompletionTokenPolicy.IssueLeadDays} days out expired at {expiry}, before the nudge at {nudgeInstant}");
    }

    [Fact]
    public void GenerateToken_IsUrlSafe_And256Bits()
    {
        var token = CompletionTokenPolicy.GenerateToken();

        Assert.False(string.IsNullOrEmpty(token));
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);

        // Decodes back to 32 bytes (256 bits — comfortably above the 128-bit minimum, NFR-03).
        var padded = token.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        Assert.Equal(32, Convert.FromBase64String(padded).Length);
    }

    [Fact]
    public void GenerateToken_ProducesDistinctValues()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => CompletionTokenPolicy.GenerateToken()).ToHashSet();
        Assert.Equal(100, tokens.Count);
    }

    [Fact]
    public void IsValidAt_TrueBeforeExpiry_FalseAtOrAfter()
    {
        var expiry = new DateTime(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(CompletionTokenPolicy.IsValidAt(expiry, expiry.AddSeconds(-1)));
        Assert.False(CompletionTokenPolicy.IsValidAt(expiry, expiry));            // rejected at the instant
        Assert.False(CompletionTokenPolicy.IsValidAt(expiry, expiry.AddSeconds(1)));
    }
}
