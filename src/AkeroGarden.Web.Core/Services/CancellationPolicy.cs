using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Services;

/// <summary>What the cancellation email says about money (FR-79).</summary>
public enum CancellationNotice
{
    /// <summary>The booking was still waiting for a deposit that never arrived. It was never
    /// confirmed, so nothing is owed (the client's terms §2).</summary>
    NothingOwed,

    /// <summary>The group cancelled a confirmed booking. Any fee follows the terms in force when it
    /// was confirmed (§32); a paid deposit is not refunded (§7).</summary>
    GuestCancelled,

    /// <summary>Åkerögården cancelled. Everything paid is refunded (§27).</summary>
    PropertyCancelled
}

/// <summary>
/// Pure resolution of the cancellation email's money clause (requirements.md §5.6, FR-77/79),
/// unit-testable (NFR-26). The email names no amount and **never restates the cancellation
/// scale** — the email points to the booking terms instead, so a change to the terms needs no code
/// change and the two can never contradict each other again (known-conflicts §3, resolved 2026-10).
///
/// <para>Until the deposit work this resolved a 20 % / two-month refund rule against the
/// cancellation date. That rule contradicted the client's adopted terms and was removed; there is
/// no timing input any more.</para>
/// </summary>
public static class CancellationPolicy
{
    /// <summary>
    /// Resolves the clause. <paramref name="depositPaid"/> is the admin's answer at cancellation —
    /// it is asked whenever a deposit was requested, because paying, not the admin's confirmation,
    /// makes the deposit non-refundable (client's §7) and money that has arrived but not yet been
    /// marked is invisible to the system.
    /// </summary>
    public static CancellationNotice Resolve(
        BookingStatus cancelledFrom, CancellationInitiator initiator, bool depositPaid)
    {
        if (cancelledFrom == BookingStatus.AwaitingDeposit && !depositPaid)
            return CancellationNotice.NothingOwed;

        return initiator == CancellationInitiator.PropertyInitiated
            ? CancellationNotice.PropertyCancelled
            : CancellationNotice.GuestCancelled;
    }
}
