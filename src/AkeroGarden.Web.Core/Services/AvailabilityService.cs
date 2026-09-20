using System.Globalization;
using System.Text;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// The verdict on a requested stay: whether the property is free for it, and if not, exactly which
/// dates are not (FR-10, FR-106).
/// </summary>
/// <param name="IsAvailable">True when every date the stay needs is free.</param>
/// <param name="ConflictingStayDates">
/// Unavailable dates among the nights the group would sleep — <c>[check-in, check-out)</c>.
/// </param>
/// <param name="ConflictingTurnoverDates">
/// Unavailable dates among the two days the stay's own turnover window would hold after it
/// (<see cref="TurnoverWindow"/>). Separated from the nights because the two need different
/// explanations: one says another group is here, the other says another group arrives too soon
/// after you leave for the property to be cleaned in between.
/// </param>
public readonly record struct StayAvailability(
    bool IsAvailable,
    IReadOnlyList<DateOnly> ConflictingStayDates,
    IReadOnlyList<DateOnly> ConflictingTurnoverDates)
{
    /// <summary>A stay with nothing in its way.</summary>
    public static StayAvailability Available { get; } = new(true, [], []);
}

/// <summary>
/// Answers "can this stay happen?" for a whole date range — the single implementation of that
/// question (FR-10).
///
/// <para><b>Why this exists as a service rather than a check at each call site.</b> Three surfaces
/// need the answer: the public calendar shows it per date, the inquiry form refuses a request that
/// cannot be honoured, and approval refuses to double-book. A rule copied into three places drifts
/// in three directions, and the two that are not on screen drift silently. Every one of them
/// resolves through here.</para>
///
/// <para><b>A stay needs more nights than it sleeps.</b> The range checked is <c>[check-in,
/// check-out + 1]</c>: the nights themselves, plus the two turnover days the stay holds after it
/// (FR-106). Omitting the tail would let a group be accepted whose departure collides with the
/// next group's arrival — the exact case the turnover window exists to prevent.</para>
/// </summary>
public class AvailabilityService(OccupancyProvider occupancy)
{
    private static readonly CultureInfo Sv = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>
    /// Evaluates a requested stay against real bookings and whole-property blocks.
    /// </summary>
    /// <param name="excludeBookingId">
    /// The booking being evaluated, when it is already stored — left out of the occupancy scan so
    /// it cannot collide with its own nights. A <c>Pending</c> inquiry never occupies anything, so
    /// this matters only for re-evaluating a booking that already counts.
    /// </param>
    public async Task<StayAvailability> EvaluateStayAsync(
        DateOnly checkIn,
        DateOnly checkOut,
        Guid? excludeBookingId = null)
    {
        // A malformed range is the model validator's business (StayRequestInput.Validate); saying
        // "unavailable" here would put a second, contradictory message on the same field.
        if (checkOut <= checkIn)
            return StayAvailability.Available;

        var lastDayNeeded = TurnoverWindow.LastDay(checkOut);
        var reasons = await occupancy.GetReasonsAsync(checkIn, lastDayNeeded, excludeBookingId);

        return Evaluate(checkIn, checkOut, reasons);
    }

    /// <summary>
    /// The pure half of <see cref="EvaluateStayAsync"/> (unit-testable, NFR-26): partitions the
    /// stay's dates into the nights it sleeps and the turnover days it holds afterwards, and
    /// reports which of them something already holds.
    /// </summary>
    public static StayAvailability Evaluate(
        DateOnly checkIn,
        DateOnly checkOut,
        IReadOnlyDictionary<DateOnly, UnavailableReason> reasons)
    {
        if (checkOut <= checkIn)
            return StayAvailability.Available;

        var stayConflicts = new List<DateOnly>();
        var turnoverConflicts = new List<DateOnly>();

        for (var date = checkIn; date <= TurnoverWindow.LastDay(checkOut); date = date.AddDays(1))
        {
            if (!reasons.TryGetValue(date, out var reason) || reason == UnavailableReason.None)
                continue;

            if (date < checkOut)
                stayConflicts.Add(date);
            else
                turnoverConflicts.Add(date);
        }

        return stayConflicts.Count == 0 && turnoverConflicts.Count == 0
            ? StayAvailability.Available
            : new StayAvailability(false, stayConflicts, turnoverConflicts);
    }

    /// <summary>
    /// The guest-form rule that a stay cannot start in the past, as a Swedish message or null
    /// (NFR-32). <paramref name="today"/> is passed in rather than read from the ambient clock, so
    /// a test gets the same answer every day it runs.
    ///
    /// <para><b>Deliberately not part of <see cref="EvaluateStayAsync"/>.</b> A past date is not a
    /// statement about the property — nothing holds it — and the admin legitimately records stays
    /// that have already happened: a booking taken by telephone after the fact (FR-112), and the
    /// ordinary path where a stay is marked completed. Folding this into the shared availability
    /// rule would block approval in exactly those cases. It belongs to the public form alone,
    /// which is the surface where a past date can only be a mistake.</para>
    /// </summary>
    public static string? DescribePastDate(DateOnly checkIn, DateOnly today) =>
        checkIn < today
            ? "Incheckningsdatumet har redan passerat. Välj ett datum framåt i tiden."
            : null;

    /// <summary>
    /// The Swedish explanation shown to the guest (NFR-32). Names the dates rather than the
    /// mechanism — "upptaget" is what the guest needs; which booking or block caused it is not
    /// theirs to know (FR-09, NFR-07).
    /// </summary>
    public static string Describe(StayAvailability availability)
    {
        if (availability.IsAvailable)
            return string.Empty;

        var message = new StringBuilder();

        if (availability.ConflictingStayDates.Count > 0)
        {
            message.Append(availability.ConflictingStayDates.Count == 1 ? "Datumet " : "Datumen ");
            message.Append(FormatRuns(availability.ConflictingStayDates));
            message.Append(availability.ConflictingStayDates.Count == 1 ? " är tyvärr upptaget." : " är tyvärr upptagna.");
        }

        if (availability.ConflictingTurnoverDates.Count > 0)
        {
            if (message.Length > 0)
                message.Append(' ');
            message.Append("Vi behöver två dagar för städning efter er avresa, och ");
            message.Append(FormatRuns(availability.ConflictingTurnoverDates));
            message.Append(" är upptaget. Välj gärna en tidigare avresa eller andra datum.");
        }
        else
        {
            message.Append(" Välj gärna andra datum.");
        }

        return message.ToString();
    }

    /// <summary>
    /// The Swedish explanation shown to the <b>admin</b> (NFR-32). Says the same thing as
    /// <see cref="Describe"/> in the register the admin needs — it names the rule being enforced
    /// rather than inviting the reader to pick other dates, which is not the admin's move to make.
    /// </summary>
    public static string DescribeForAdmin(StayAvailability availability)
    {
        if (availability.IsAvailable)
            return string.Empty;

        var message = new StringBuilder();

        if (availability.ConflictingStayDates.Count > 0)
        {
            message.Append(availability.ConflictingStayDates.Count == 1 ? "Datumet " : "Datumen ");
            message.Append(FormatRuns(availability.ConflictingStayDates));
            message.Append(" är redan upptagna. Gården hyrs ut till en grupp i taget, så bokningen kan inte godkännas över de datumen.");
        }

        if (availability.ConflictingTurnoverDates.Count > 0)
        {
            if (message.Length > 0)
                message.Append(' ');
            message.Append("Städdagarna efter avresan (");
            message.Append(FormatRuns(availability.ConflictingTurnoverDates));
            message.Append(") är upptagna av en annan bokning.");
        }

        return message.ToString();
    }

    /// <summary>
    /// Formats dates as Swedish runs — "10–15 oktober", "10 oktober och 14–15 oktober". Compressed
    /// into contiguous runs rather than printed as a min–max span: a span would name free days as
    /// unavailable, which is the kind of small untruth a guest checks against the calendar and
    /// loses confidence over.
    /// </summary>
    private static string FormatRuns(IReadOnlyList<DateOnly> dates)
    {
        var runs = new List<string>();
        var runStart = dates[0];
        var previous = dates[0];

        for (var i = 1; i <= dates.Count; i++)
        {
            var isEnd = i == dates.Count;
            if (!isEnd && dates[i] == previous.AddDays(1))
            {
                previous = dates[i];
                continue;
            }

            runs.Add(FormatRun(runStart, previous));
            if (!isEnd)
            {
                runStart = dates[i];
                previous = dates[i];
            }
        }

        return runs.Count switch
        {
            1 => runs[0],
            2 => $"{runs[0]} och {runs[1]}",
            _ => $"{string.Join(", ", runs.Take(runs.Count - 1))} och {runs[^1]}"
        };
    }

    private static string FormatRun(DateOnly start, DateOnly end)
    {
        if (start == end)
            return start.ToString("d MMMM", Sv);
        if (start.Month == end.Month && start.Year == end.Year)
            return $"{start.Day}–{end.ToString("d MMMM", Sv)}";
        return $"{start.ToString("d MMMM", Sv)} – {end.ToString("d MMMM", Sv)}";
    }
}
