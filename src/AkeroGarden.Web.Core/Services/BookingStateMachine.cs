using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// The one-way booking status transitions of requirements.md §2.5. Pure and unit-testable
/// (NFR-26). This is the single source of truth for which transitions are legal;
/// <see cref="BookingService"/> routes every status change through it so illegal transitions
/// are blocked in code, not merely hidden in the UI. Any transition not in
/// <see cref="AllowedTransitions"/> is rejected — including all transitions out of the terminal
/// states (Denied, Completed, Closed, Cancelled).
/// </summary>
public static class BookingStateMachine
{
    /// <summary>The complete set of allowed <c>(from → to)</c> transitions (§2.5).</summary>
    public static readonly IReadOnlySet<(BookingStatus From, BookingStatus To)> AllowedTransitions =
        new HashSet<(BookingStatus, BookingStatus)>
        {
            (BookingStatus.Pending, BookingStatus.Approved),         // Admin — stay requests only
            (BookingStatus.Pending, BookingStatus.Denied),           // Admin — stay requests only
            (BookingStatus.Pending, BookingStatus.Closed),           // Admin — general inquiries only
            (BookingStatus.Approved, BookingStatus.DetailsComplete), // System — on guest completion
            (BookingStatus.Approved, BookingStatus.Cancelled),       // Admin
            (BookingStatus.DetailsComplete, BookingStatus.Active),   // Admin — marks arrived
            (BookingStatus.DetailsComplete, BookingStatus.Cancelled),// Admin
            (BookingStatus.Active, BookingStatus.Completed),         // Admin — marks finished
            (BookingStatus.Active, BookingStatus.Cancelled),         // Admin
        };

    /// <summary>True if <paramref name="to"/> is a legal next status from <paramref name="from"/>.</summary>
    public static bool CanTransition(BookingStatus from, BookingStatus to) =>
        AllowedTransitions.Contains((from, to));

    /// <summary>All statuses legally reachable in one step from <paramref name="from"/>.</summary>
    public static IReadOnlyList<BookingStatus> AllowedTargets(BookingStatus from) =>
        AllowedTransitions
            .Where(t => t.From == from)
            .Select(t => t.To)
            .ToList();

    /// <summary>
    /// Throws <see cref="InvalidBookingTransitionException"/> if the transition is not allowed;
    /// otherwise returns. Call this before mutating <see cref="Booking.Status"/>.
    /// </summary>
    public static void EnsureCanTransition(BookingStatus from, BookingStatus to)
    {
        if (!CanTransition(from, to))
            throw new InvalidBookingTransitionException(from, to);
    }
}

/// <summary>
/// Thrown when code attempts a booking status transition not permitted by §2.5. Surfacing this
/// as an exception (rather than a silent no-op) ensures an illegal transition fails loudly in
/// tests and logs rather than corrupting a booking's lifecycle.
/// </summary>
public class InvalidBookingTransitionException(BookingStatus from, BookingStatus to)
    : InvalidOperationException($"Ogiltig statusövergång: {from} → {to}.")
{
    public BookingStatus From { get; } = from;
    public BookingStatus To { get; } = to;
}
