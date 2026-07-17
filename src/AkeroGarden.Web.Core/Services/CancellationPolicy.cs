using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Services;

/// <summary>Whether the deposit is refunded or forfeited on cancellation (§5.6, FR-79).</summary>
public enum DepositOutcome
{
    FullRefund,
    Forfeited
}

/// <summary>
/// Pure resolution of the deposit clause for a cancellation (requirements.md §5.6, FR-79),
/// unit-testable (NFR-26). The system stores no monetary amounts (C8) — this decides only
/// which fixed policy wording the cancellation email carries.
/// </summary>
public static class CancellationPolicy
{
    /// <summary>Cutoff before check-in at which a guest-initiated cancellation loses the deposit.</summary>
    public const int ForfeitCutoffMonths = 2;

    /// <summary>
    /// Resolves the deposit outcome (FR-79):
    /// property-initiated cancellations are always a full refund; guest-initiated cancellations
    /// are a full refund only when made <b>more than</b> two months before check-in, and are
    /// forfeited when made two months or less before check-in.
    /// </summary>
    /// <param name="initiator">Who initiated the cancellation.</param>
    /// <param name="checkIn">The stay's check-in date.</param>
    /// <param name="cancelledOn">The date the cancellation is made.</param>
    public static DepositOutcome ResolveDepositOutcome(
        CancellationInitiator initiator,
        DateOnly checkIn,
        DateOnly cancelledOn)
    {
        if (initiator == CancellationInitiator.PropertyInitiated)
            return DepositOutcome.FullRefund;

        // Guest-initiated: full refund only if more than two months before check-in.
        var forfeitFrom = checkIn.AddMonths(-ForfeitCutoffMonths);
        return cancelledOn < forfeitFrom ? DepositOutcome.FullRefund : DepositOutcome.Forfeited;
    }
}
