using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the pure completion-link access rule (§2.6, FR-53/54, C10): an Approved
/// booking with an unexpired token is the only usable state; already-submitted bookings report
/// AlreadyCompleted (so the guest sees a helpful message, not "invalid"); every other status is
/// NotActive. No database, no ambient clock (NFR-26).
/// </summary>
public class CompletionAccessPolicyTests
{
    private static readonly DateTime Now = new(2026, 7, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Approved_WithFutureExpiry_IsValid()
    {
        var access = CompletionAccessPolicy.Evaluate(BookingStatus.Approved, Now.AddHours(1), Now);
        Assert.Equal(CompletionAccess.Valid, access);
    }

    [Fact]
    public void Approved_WithPastExpiry_IsExpired()
    {
        var access = CompletionAccessPolicy.Evaluate(BookingStatus.Approved, Now.AddHours(-1), Now);
        Assert.Equal(CompletionAccess.Expired, access);
    }

    [Fact]
    public void Approved_AtExactExpiryInstant_IsExpired()
    {
        // Valid up to, and rejected at or after, the expiry instant (§2.6).
        var access = CompletionAccessPolicy.Evaluate(BookingStatus.Approved, Now, Now);
        Assert.Equal(CompletionAccess.Expired, access);
    }

    [Fact]
    public void Approved_WithNullExpiry_IsExpired()
    {
        var access = CompletionAccessPolicy.Evaluate(BookingStatus.Approved, null, Now);
        Assert.Equal(CompletionAccess.Expired, access);
    }

    [Theory]
    [InlineData(BookingStatus.DetailsComplete)]
    [InlineData(BookingStatus.Active)]
    [InlineData(BookingStatus.Completed)]
    public void AlreadySubmittedStatuses_AreAlreadyCompleted(BookingStatus status)
    {
        // Even with a still-valid expiry, a submitted booking cannot reopen the form (C10).
        var access = CompletionAccessPolicy.Evaluate(status, Now.AddDays(5), Now);
        Assert.Equal(CompletionAccess.AlreadyCompleted, access);
    }

    [Theory]
    [InlineData(BookingStatus.Pending)]
    [InlineData(BookingStatus.Denied)]
    [InlineData(BookingStatus.Closed)]
    [InlineData(BookingStatus.Cancelled)]
    public void OtherStatuses_AreNotActive(BookingStatus status)
    {
        var access = CompletionAccessPolicy.Evaluate(status, Now.AddDays(5), Now);
        Assert.Equal(CompletionAccess.NotActive, access);
    }
}
