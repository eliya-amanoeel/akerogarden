using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Why a completion link (`/slutfor/{token}`) is or is not usable right now. Drives which Swedish
/// message the completion page shows (Phase 4 slice 2, §2.6, FR-53/54).
/// </summary>
public enum CompletionAccess
{
    /// <summary>Token matches an <c>Approved</c> booking whose link has not expired — show the form.</summary>
    Valid,

    /// <summary>No booking matches the token — never issued, or superseded by a regenerated link (FR-49).</summary>
    NotFound,

    /// <summary>The booking is <c>Approved</c> but the link's expiry instant has passed (§2.6).</summary>
    Expired,

    /// <summary>The guest has already submitted (<c>DetailsComplete</c>/<c>Active</c>/<c>Completed</c>) —
    /// no reopen (C10); corrections go through the admin (FR-48).</summary>
    AlreadyCompleted,

    /// <summary>The booking is in a state where the link is not usable (e.g. <c>Cancelled</c>).</summary>
    NotActive
}

/// <summary>
/// Pure, unit-testable (NFR-26) evaluation of completion-link access from a booking's status and
/// token expiry. Single-use is enforced by the <b>status gate</b>: only an <c>Approved</c> booking
/// accepts the completion form, so once submission advances the booking to <c>DetailsComplete</c>
/// the same link is inert (FR-62, C10) without having to erase the token string — which also lets
/// the page tell a guest who reopens the email "you have already submitted" rather than a bare
/// "invalid link". The <see cref="CompletionAccess.NotFound"/> case (no matching token) is
/// determined by the database lookup, not here.
/// </summary>
public static class CompletionAccessPolicy
{
    /// <summary>
    /// Evaluates access for a booking that <em>was</em> found by its token.
    /// </summary>
    /// <param name="status">The booking's current status.</param>
    /// <param name="expiryUtc">The token's expiry instant (null is treated as expired).</param>
    /// <param name="nowUtc">The current instant (caller-supplied — no ambient clock).</param>
    public static CompletionAccess Evaluate(BookingStatus status, DateTime? expiryUtc, DateTime nowUtc) =>
        status switch
        {
            BookingStatus.DetailsComplete or BookingStatus.Active or BookingStatus.Completed
                => CompletionAccess.AlreadyCompleted,
            BookingStatus.Approved when expiryUtc is { } e && CompletionTokenPolicy.IsValidAt(e, nowUtc)
                => CompletionAccess.Valid,
            BookingStatus.Approved
                => CompletionAccess.Expired,
            _ // Pending, AwaitingDeposit, Denied, Closed, Cancelled — the link is not usable
                => CompletionAccess.NotActive,
        };
}
