using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Computes a booking's <c>DeleteAfter</c> date per the GDPR retention rules of requirements.md
/// §2.7 / FR-91. Pure and unit-testable (NFR-26). The value is recomputed whenever a booking
/// enters a retention-relevant state; the scheduled deletion job and the 30-day pre-deletion
/// warning (both Phase 5) operate off the stored result.
///
/// <list type="bullet">
///   <item><c>Pending</c> / <c>AwaitingDeposit</c> — submission date + 12 months</item>
///   <item><c>Closed</c> — submission date + 12 months</item>
///   <item><c>Denied</c> — denial date + 12 months</item>
///   <item><c>Cancelled</c> — cancellation date + 12 months</item>
///   <item><c>Completed</c> — check-out date + 2 years</item>
///   <item><c>Approved</c> / <c>DetailsComplete</c> / <c>Active</c> — not deletable (null)</item>
/// </list>
/// </summary>
public static class RetentionCalculator
{
    /// <summary>
    /// Computes the <c>DeleteAfter</c> date for a booking entering <paramref name="status"/>.
    /// </summary>
    /// <param name="status">The status being entered.</param>
    /// <param name="submissionDate">The booking's submission (created) date — drives Pending/Closed.</param>
    /// <param name="checkOut">The stay's check-out date — required for Completed; ignored otherwise.</param>
    /// <param name="transitionDate">
    /// The date the transition happens — drives Denied/Cancelled (the denial/cancellation date).
    /// </param>
    /// <returns>The date after which the record may be deleted, or null for live in-progress states.</returns>
    public static DateOnly? ComputeDeleteAfter(
        BookingStatus status,
        DateOnly submissionDate,
        DateOnly? checkOut,
        DateOnly transitionDate) =>
        status switch
        {
            BookingStatus.Pending => submissionDate.AddMonths(12),
            // A booking waiting for a deposit nobody pays must not keep guest data indefinitely (FR-91).
            BookingStatus.AwaitingDeposit => submissionDate.AddMonths(12),
            BookingStatus.Closed => submissionDate.AddMonths(12),
            BookingStatus.Denied => transitionDate.AddMonths(12),
            BookingStatus.Cancelled => transitionDate.AddMonths(12),
            BookingStatus.Completed => checkOut is null
                ? throw new ArgumentNullException(nameof(checkOut),
                    "En avslutad bokning måste ha ett utcheckningsdatum för att beräkna gallringsdatum.")
                : checkOut.Value.AddYears(2),
            // Live in-progress states are not subject to automatic deletion (FR-91).
            _ => null
        };
}
