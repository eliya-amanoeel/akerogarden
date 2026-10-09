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
/// <param name="ConflictReasons">
/// Why each conflicting date (from either list) is unavailable. The guest is never told — which
/// booking or block caused it is not theirs to know (FR-09) — but the admin is, because "another
/// group" and "a whole-property block" call for different actions, and naming the wrong one sent
/// the admin looking for a booking that did not exist.
/// </param>
public readonly record struct StayAvailability(
    bool IsAvailable,
    IReadOnlyList<DateOnly> ConflictingStayDates,
    IReadOnlyList<DateOnly> ConflictingTurnoverDates,
    IReadOnlyDictionary<DateOnly, UnavailableReason> ConflictReasons)
{
    /// <summary>A stay with nothing in its way.</summary>
    public static StayAvailability Available { get; } =
        new(true, [], [], new Dictionary<DateOnly, UnavailableReason>());
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
        var conflictReasons = new Dictionary<DateOnly, UnavailableReason>();

        for (var date = checkIn; date <= TurnoverWindow.LastDay(checkOut); date = date.AddDays(1))
        {
            if (!reasons.TryGetValue(date, out var reason) || reason == UnavailableReason.None)
                continue;

            conflictReasons[date] = reason;
            if (date < checkOut)
                stayConflicts.Add(date);
            else
                turnoverConflicts.Add(date);
        }

        return stayConflicts.Count == 0 && turnoverConflicts.Count == 0
            ? StayAvailability.Available
            : new StayAvailability(false, stayConflicts, turnoverConflicts, conflictReasons);
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
            // Says why without the mechanism: not how many days, not "cleaning", and "inte lediga"
            // rather than "bokade" — the cause may be a closure the guest must not be told about
            // (FR-09). Plural agreement follows the dates, which "är upptaget" got wrong for a range.
            var days = availability.ConflictingTurnoverDates.Count;
            message.Append("Vi behöver också tid att göra i ordning gården mellan två grupper, och ");
            message.Append(days == 1 ? "dagen närmast efter er avresa (" : "dagarna närmast efter er avresa (");
            message.Append(FormatRuns(availability.ConflictingTurnoverDates));
            message.Append(days == 1 ? ") är inte ledig." : ") är inte lediga.");
            message.Append(" Välj gärna en tidigare avresa eller andra datum.");
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
    ///
    /// <para><b>It names the cause, which <see cref="Describe"/> deliberately does not.</b> Another
    /// group and a whole-property block call for different actions — talk to the guest about other
    /// dates, or change the block — so each is reported on its own. It once put every unavailable
    /// date under "Gården hyrs ut till en grupp i taget", and a block was explained as a booking
    /// that did not exist. A date held by both is reported under both.</para>
    /// </summary>
    public static string DescribeForAdmin(StayAvailability availability)
    {
        if (availability.IsAvailable)
            return string.Empty;

        static bool HeldByAGroup(UnavailableReason r) =>
            (r & (UnavailableReason.Occupied | UnavailableReason.Turnover)) != 0;

        var reasons = availability.ConflictReasons;
        var groupNights = availability.ConflictingStayDates.Where(d => HeldByAGroup(reasons[d])).ToList();
        var groupTurnover = availability.ConflictingTurnoverDates.Where(d => HeldByAGroup(reasons[d])).ToList();
        var blocked = availability.ConflictingStayDates.Concat(availability.ConflictingTurnoverDates)
            .Where(d => reasons[d].HasFlag(UnavailableReason.WholePropertyBlock))
            .Order()
            .ToList();

        var sentences = new List<string>();

        if (groupNights.Count > 0)
        {
            sentences.Add((groupNights.Count == 1 ? "Datumet " : "Datumen ")
                + FormatRuns(groupNights)
                + (groupNights.Count == 1 ? " är redan upptaget." : " är redan upptagna.")
                + " Gården hyrs ut till en grupp i taget, så bokningen kan inte godkännas över de datumen.");
        }

        if (groupTurnover.Count > 0)
            sentences.Add($"Städdagarna efter avresan ({FormatRuns(groupTurnover)}) är upptagna av en annan bokning.");

        if (blocked.Count > 0)
        {
            // A block on the cleaning days after check-out stops the stay as surely as one on its
            // nights (FR-106), and is the case that most needs saying — the stay's own dates look free.
            var coversTurnover = blocked.Any(availability.ConflictingTurnoverDates.Contains);
            sentences.Add($"Hela anläggningen är blockerad {FormatRuns(blocked)}"
                + (coversTurnover ? ", och städdagarna efter avresan behöver vara fria." : ".")
                + " Bokningen kan inte godkännas förrän blockeringen tas bort eller ändras.");
        }

        return string.Join(" ", sentences);
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
