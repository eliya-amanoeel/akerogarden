using System.Globalization;
using AkeroGarden.Web.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace AkeroGarden.Unit;

/// <summary>
/// The public inquiry form's bot check (NFR-04). Each refusal reason is driven on its own, and the
/// two boundaries — the minimum fill time and the maximum age — are asserted exactly, which is why the
/// clock is a <see cref="FakeTimeProvider"/> advanced by the test rather than the wall clock.
/// </summary>
public class InquiryFormGuardTests
{
    private static (InquiryFormGuard Guard, FakeTimeProvider Clock) Build(int minSeconds = 3, int maxHours = 24)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.Zero));
        var options = Options.Create(new InquiryFormGuardOptions
        {
            MinimumFillSeconds = minSeconds,
            MaximumAgeHours = maxHours,
        });
        return (new InquiryFormGuard(new EphemeralDataProtectionProvider(), options, clock), clock);
    }

    [Fact]
    public void A_form_sent_after_the_minimum_fill_time_is_accepted()
    {
        var (guard, clock) = Build();
        var stamp = guard.Issue();
        clock.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(InquiryFormGuardVerdict.Accepted, guard.Check(new() { Stamp = stamp }));
    }

    [Fact]
    public void A_form_sent_sooner_than_the_minimum_fill_time_is_refused()
    {
        var (guard, clock) = Build();
        var stamp = guard.Issue();
        clock.Advance(TimeSpan.FromSeconds(3) - TimeSpan.FromTicks(1));

        Assert.Equal(InquiryFormGuardVerdict.TooFast, guard.Check(new() { Stamp = stamp }));
    }

    [Fact]
    public void A_stamp_older_than_the_maximum_age_is_refused()
    {
        var (guard, clock) = Build();
        var stamp = guard.Issue();

        clock.Advance(TimeSpan.FromHours(24));
        Assert.Equal(InquiryFormGuardVerdict.Accepted, guard.Check(new() { Stamp = stamp }));

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(InquiryFormGuardVerdict.Expired, guard.Check(new() { Stamp = stamp }));
    }

    [Fact]
    public void A_filled_trap_field_is_refused_even_with_a_good_stamp()
    {
        var (guard, clock) = Build();
        var stamp = guard.Issue();
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(InquiryFormGuardVerdict.TrapFilled, guard.Check(new() { Stamp = stamp, Extra = "ulgdnuezut" }));
    }

    [Fact]
    public void A_missing_stamp_is_refused()
    {
        var (guard, _) = Build();

        Assert.Equal(InquiryFormGuardVerdict.StampMissing, guard.Check(null));
        Assert.Equal(InquiryFormGuardVerdict.StampMissing, guard.Check(new()));
        Assert.Equal(InquiryFormGuardVerdict.StampMissing, guard.Check(new() { Stamp = "" }));
    }

    [Fact]
    public void A_forged_or_altered_stamp_is_refused()
    {
        var (guard, clock) = Build();
        var stamp = guard.Issue();
        clock.Advance(TimeSpan.FromMinutes(2));

        // A plain timestamp, which is what a forger would post, and the real stamp with one character
        // changed. A middle character, not the last: the last one of base64 can carry padding bits, so
        // changing it may decode to the same bytes and prove nothing.
        Assert.Equal(InquiryFormGuardVerdict.StampInvalid,
            guard.Check(new() { Stamp = clock.GetUtcNow().AddMinutes(-5).UtcTicks.ToString(CultureInfo.InvariantCulture) }));
        var middle = stamp.Length / 2;
        var altered = stamp[..middle] + (stamp[middle] == 'A' ? 'B' : 'A') + stamp[(middle + 1)..];
        Assert.Equal(InquiryFormGuardVerdict.StampInvalid, guard.Check(new() { Stamp = altered }));
    }

    [Fact]
    public void A_stamp_from_another_key_ring_is_refused()
    {
        // Each host has its own Data Protection application name; a stamp only verifies where it was issued.
        var (issuer, clock) = Build();
        var stamp = issuer.Issue();
        clock.Advance(TimeSpan.FromMinutes(2));
        var (other, _) = Build();

        Assert.Equal(InquiryFormGuardVerdict.StampInvalid, other.Check(new() { Stamp = stamp }));
    }

    [Fact]
    public void A_redrawn_form_keeps_its_stamp_so_the_guest_does_not_wait_again()
    {
        var (guard, clock) = Build();
        var stamp = guard.Issue();
        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(stamp, guard.StampFor(stamp));
    }

    [Fact]
    public void A_redrawn_form_with_an_expired_or_bad_stamp_gets_a_fresh_one()
    {
        var (guard, clock) = Build();
        var stamp = guard.Issue();
        clock.Advance(TimeSpan.FromHours(25));

        var renewed = guard.StampFor(stamp);
        Assert.NotEqual(stamp, renewed);
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(InquiryFormGuardVerdict.Accepted, guard.Check(new() { Stamp = renewed }));

        Assert.NotEqual("forged", guard.StampFor("forged"));
        Assert.NotEqual("", guard.StampFor(null));
    }
}
