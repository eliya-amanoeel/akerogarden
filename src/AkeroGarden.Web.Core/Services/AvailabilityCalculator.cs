namespace AkeroGarden.Web.Core.Services;

/// <summary>The two public-calendar states for a single date (FR-09).</summary>
public enum AvailabilityState
{
    Free,

    /// <summary>Held by a booking, its turnover window, or a whole-property block.</summary>
    Unavailable
}

/// <summary>
/// Why a date is unavailable (FR-10).
///
/// <para><b>Group size is deliberately absent.</b> The property is let to one group at a time —
/// two unrelated groups never share it — so an occupied night is unavailable whether the group
/// numbers three or seventy-nine. The calendar used to compare guests against 79 beds and show a
/// third, "partially booked" state for anything between; that state advertised dates no second
/// group could ever have, and the inquiries it invited could only be denied. Do not reintroduce a
/// capacity comparison here without first changing that letting policy.</para>
///
/// <para>Modelled as a set rather than a single flag so that adding a future reason is additive:
/// <see cref="AvailabilityCalculator.Evaluate"/> asks whether *any* reason applies and never has
/// to be reopened. The set is also what lets the inquiry form say *why* a requested range is
/// unavailable rather than merely that it is (<see cref="AvailabilityService"/>).</para>
/// </summary>
[Flags]
public enum UnavailableReason
{
    /// <summary>Nothing makes the date unavailable — it is free.</summary>
    None = 0,

    /// <summary>A <c>WholeProperty</c> block covers the date (FR-21).</summary>
    WholePropertyBlock = 1,

    /// <summary>The date falls in an occupying booking's cleaning/turnover window (FR-106).</summary>
    Turnover = 2,

    /// <summary>
    /// An occupying booking (<c>Approved</c> / <c>DetailsComplete</c> / <c>Active</c>) holds this
    /// night — check-in inclusive, check-out exclusive. <c>Pending</c> inquiries never set this:
    /// availability is subject to admin confirmation (FR-10).
    /// </summary>
    Occupied = 4
}

/// <summary>
/// Pure computation of a date's public availability state from its reasons (FR-10). No database,
/// no rendering — the single named home of the rule, so the calendar, the inquiry form and the
/// approval path cannot each carry their own version of it.
/// </summary>
public static class AvailabilityCalculator
{
    /// <summary>Derives the public state for a single date per FR-10's rule.</summary>
    public static AvailabilityState Evaluate(UnavailableReason reasons) =>
        reasons == UnavailableReason.None
            ? AvailabilityState.Free
            : AvailabilityState.Unavailable;
}
