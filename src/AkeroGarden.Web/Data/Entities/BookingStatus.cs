namespace AkeroGarden.Web.Data.Entities;

/// <summary>
/// The lifecycle state of a booking (requirements.md §2.4). Transitions between states
/// are one-way and constrained — the allowed set is enforced by
/// <see cref="AkeroGarden.Web.Services.BookingStateMachine"/> (§2.5), not just hidden in the UI.
/// Persisted as a string for legibility and stability against reordering.
/// </summary>
public enum BookingStatus
{
    /// <summary>Inquiry submitted, awaiting admin review (both booking types).</summary>
    Pending,

    /// <summary>Stay request approved; awaiting guest completion.</summary>
    Approved,

    /// <summary>Stay request denied; retained 12 months. Terminal.</summary>
    Denied,

    /// <summary>Guest submitted participant and meal data.</summary>
    DetailsComplete,

    /// <summary>Group currently on-site.</summary>
    Active,

    /// <summary>Stay finished; in retention period. Terminal.</summary>
    Completed,

    /// <summary>Cancelled by admin from Approved/DetailsComplete/Active. Terminal.</summary>
    Cancelled,

    /// <summary>General inquiry read and responded to; no approve/deny applies. Terminal.</summary>
    Closed
}
