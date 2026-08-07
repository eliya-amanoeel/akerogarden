namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// The cleaning buffer a booking holds after its stay (FR-106): the check-out date itself **and
/// the day after it**, both fully unavailable — irrespective of the stated check-out time and of
/// whether the group purchased cleaning. Two days rather than one because a single day would be
/// shared with a departing group who may not leave until late.
///
/// <para>Computed from the booking's dates, never stored. A stored <c>BlockedPeriod</c> would
/// duplicate what the booking already holds and would need maintaining on every date change,
/// cancellation or denial, where one missed update silently costs a bookable day (FR-106).</para>
///
/// <para>This type is the single definition of the rule: the public calendar (through
/// <see cref="OccupancyProvider"/>) and the admin calendar both read it here, so the two surfaces
/// cannot drift into showing different days. Derive everything from <see cref="Days"/> rather than
/// hardcoding the offset at a call site.</para>
/// </summary>
public static class TurnoverWindow
{
    /// <summary>Days held after a stay: the check-out date and the day following it.</summary>
    public const int Days = 2;

    /// <summary>Last date held by the turnover window of a stay ending on <paramref name="checkOut"/>.</summary>
    public static DateOnly LastDay(DateOnly checkOut) => checkOut.AddDays(Days - 1);

    /// <summary>
    /// True when <paramref name="date"/> falls in the turnover window of a stay ending on
    /// <paramref name="checkOut"/>.
    /// </summary>
    public static bool Covers(DateOnly checkOut, DateOnly date) =>
        date >= checkOut && date <= LastDay(checkOut);

    /// <summary>
    /// Earliest check-out date whose turnover window can still reach <paramref name="from"/>.
    /// Any query feeding a date window must reach back this far: a stay that ended *before* the
    /// window opens still holds days inside it, so loading only the stays overlapping
    /// <c>[from, to]</c> silently frees the window's first day.
    /// </summary>
    public static DateOnly EarliestCheckOutAffecting(DateOnly from) => from.AddDays(-(Days - 1));

    /// <summary>The dates held by a stay ending on <paramref name="checkOut"/>, in order.</summary>
    public static IEnumerable<DateOnly> Dates(DateOnly checkOut) =>
        Enumerable.Range(0, Days).Select(checkOut.AddDays);
}
