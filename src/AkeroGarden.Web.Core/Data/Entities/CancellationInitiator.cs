namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// Who initiated a cancellation (requirements.md §8.1, FR-77). Controls the deposit clause
/// in the cancellation email (FR-79): guest-initiated cancellations follow the timing rule
/// (full refund if &gt;2 months before check-in, forfeited if ≤2 months), while
/// property-initiated cancellations are always a full refund regardless of timing.
/// Persisted as a string for legibility and stability against reordering.
/// </summary>
public enum CancellationInitiator
{
    GuestInitiated,
    PropertyInitiated
}
