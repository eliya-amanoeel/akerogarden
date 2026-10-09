namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// The lifecycle state of a booking (requirements.md §2.4). Transitions between states
/// are one-way and constrained — the allowed set is enforced by
/// <see cref="AkeroGarden.Web.Core.Services.BookingStateMachine"/> (§2.5), not just hidden in the UI.
/// Persisted as a string for legibility and stability against reordering.
/// </summary>
public enum BookingStatus
{
    /// <summary>Inquiry submitted, awaiting admin review (both booking types).</summary>
    Pending,

    /// <summary>Stay request whose deposit the admin has asked for (FR-115). Holds its dates while
    /// the group pays; the admin confirms it to <see cref="Approved"/> once the money has arrived.
    /// Placed after <see cref="Pending"/> so lists ordered by declaration read in lifecycle order —
    /// safe to insert because the status is persisted as a string, not an ordinal.</summary>
    AwaitingDeposit,

    /// <summary>Stay request confirmed — directly, or once its deposit arrived; awaiting guest completion.</summary>
    Approved,

    /// <summary>Stay request denied; retained 12 months. Terminal.</summary>
    Denied,

    /// <summary>Guest submitted participant and meal data.</summary>
    DetailsComplete,

    /// <summary>Group currently on-site.</summary>
    Active,

    /// <summary>Stay finished; in retention period. Terminal.</summary>
    Completed,

    /// <summary>Cancelled by admin from AwaitingDeposit/Approved/DetailsComplete/Active. Terminal.</summary>
    Cancelled,

    /// <summary>General inquiry read and responded to; no approve/deny applies. Terminal.</summary>
    Closed
}
