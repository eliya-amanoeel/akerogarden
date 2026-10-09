using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;

namespace AkeroGarden.Unit;

/// <summary>
/// Unit coverage for the cancellation email's money clause (§5.6, FR-77/79, NFR-26). There is no
/// timing input any more: the email points to the booking terms instead of restating a scale, so the
/// only questions are who cancelled and whether a deposit had been paid.
/// </summary>
public class CancellationPolicyTests
{
    [Theory]
    [InlineData(CancellationInitiator.GuestInitiated)]
    [InlineData(CancellationInitiator.PropertyInitiated)]
    public void AwaitingDeposit_NotPaid_IsNothingOwed_WhoeverCancels(CancellationInitiator initiator)
    {
        // Never confirmed (terms §2), so whoever cancels, the guest owes nothing.
        Assert.Equal(CancellationNotice.NothingOwed,
            CancellationPolicy.Resolve(BookingStatus.AwaitingDeposit, initiator, depositPaid: false));
    }

    [Fact]
    public void AwaitingDeposit_Paid_ButNotYetConfirmed_IsAGuestCancellation()
    {
        // Paying is the binding moment (D15): money that arrived before the admin confirmed still
        // makes the deposit non-refundable, so this must not read "nothing owed".
        Assert.Equal(CancellationNotice.GuestCancelled,
            CancellationPolicy.Resolve(BookingStatus.AwaitingDeposit, CancellationInitiator.GuestInitiated, depositPaid: true));
    }

    [Theory]
    [InlineData(BookingStatus.AwaitingDeposit, true)]
    [InlineData(BookingStatus.Approved, true)]
    [InlineData(BookingStatus.Approved, false)]
    [InlineData(BookingStatus.DetailsComplete, false)]
    [InlineData(BookingStatus.Active, true)]
    public void PropertyInitiated_IsAlwaysARefund_OnceAnythingCouldHaveBeenPaid(BookingStatus from, bool depositPaid)
    {
        Assert.Equal(CancellationNotice.PropertyCancelled,
            CancellationPolicy.Resolve(from, CancellationInitiator.PropertyInitiated, depositPaid));
    }

    [Theory]
    [InlineData(BookingStatus.Approved, true)]
    [InlineData(BookingStatus.Approved, false)]
    [InlineData(BookingStatus.DetailsComplete, false)]
    [InlineData(BookingStatus.Active, true)]
    public void GuestInitiated_OnAConfirmedBooking_IsAGuestCancellation(BookingStatus from, bool depositPaid)
    {
        Assert.Equal(CancellationNotice.GuestCancelled,
            CancellationPolicy.Resolve(from, CancellationInitiator.GuestInitiated, depositPaid));
    }
}
