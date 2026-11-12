using System.Globalization;
using System.Text;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// The verdict on a requested stay: whether the property is free for it, and if not, exactly which
/// dates are not (FR-10, FR-106).
/// </summary>
/// <param name="IsAvailable">True when every date the stay needs is free.</param>
/// <param name="CheckOut">The stay's check-out date — the one held day the group does not sleep.</param>
/// <param name="ConflictingDates">
/// Unavailable dates among those the stay holds — <c>[check-in, check-out]</c>, both inclusive
/// (FR-106) — in order.
/// </param>
/// <param name="ConflictReasons">
/// Why each conflicting date is unavailable. The guest is never told — which booking or block
/// caused it is not theirs to know (FR-09) — but the admin is, because "another group" and "a
/// whole-property block" call for different actions, and naming the wrong one sent the admin
/// looking for a booking that did not exist.
/// </param>
public readonly record struct StayAvailability(
    bool IsAvailable,
    DateOnly CheckOut,
    IReadOnlyList<DateOnly> ConflictingDates,
    IReadOnlyDictionary<DateOnly, UnavailableReason> ConflictReasons)
{
    /// <summary>A stay with nothing in its way.</summary>
    public static StayAvailability Available { get; } =
        new(true, default, [], new Dictionary<DateOnly, UnavailableReason>());

    /// <summary>
    /// True when the check-out day is among the conflicts. Worth saying on its own: the group does
    /// not sleep that night, so a reader does not expect it to need to be free (FR-106).
    /// </summary>
    public bool CheckOutDayConflicts => ConflictingDates.Contains(CheckOut);
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
/// <para><b>A stay holds one day more than it sleeps.</b> The range checked is <c>[check-in,
/// check-out]</c>, both inclusive: the nights, plus the check-out day, which is held for cleaning
/// (FR-106). Checking the nights alone would let a group be accepted whose departure lands on the
/// next group's arrival.</para>
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

        var reasons = await occupancy.GetReasonsAsync(checkIn, checkOut, excludeBookingId);

        return Evaluate(checkIn, checkOut, reasons);
    }

    /// <summary>
    /// The pure half of <see cref="EvaluateStayAsync"/> (unit-testable, NFR-26): reports which of
    /// the dates the stay holds — check-in through check-out — something already holds.
    /// </summary>
    public static StayAvailability Evaluate(
        DateOnly checkIn,
        DateOnly checkOut,
        IReadOnlyDictionary<DateOnly, UnavailableReason> reasons)
    {
        if (checkOut <= checkIn)
            return StayAvailability.Available;

        var conflicts = new List<DateOnly>();
        var conflictReasons = new Dictionary<DateOnly, UnavailableReason>();

        for (var date = checkIn; date <= checkOut; date = date.AddDays(1))
        {
            if (!reasons.TryGetValue(date, out var reason) || reason == UnavailableReason.None)
                continue;

            conflictReasons[date] = reason;
            conflicts.Add(date);
        }

        return conflicts.Count == 0
            ? StayAvailability.Available
            : new StayAvailability(false, checkOut, conflicts, conflictReasons);
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

        var dates = availability.ConflictingDates;
        var message = new StringBuilder();

        message.Append(dates.Count == 1 ? "Datumet " : "Datumen ");
        message.Append(FormatRuns(dates));
        message.Append(dates.Count == 1 ? " är tyvärr upptaget." : " är tyvärr upptagna.");

        if (availability.CheckOutDayConflicts)
        {
            // The guest does not sleep on the check-out day, so they do not expect it to need to be
            // free — say that it does, and why, in their terms ("göra i ordning", not the mechanism).
            message.Append($" Den {availability.CheckOut.ToString("d MMMM", Sv)} är er avresedag, och den behöver"
                + " också vara ledig eftersom vi gör i ordning gården mellan grupperna.");
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

        var reasons = availability.ConflictReasons;
        var checkOut = availability.CheckOut;
        var byGroup = availability.ConflictingDates
            .Where(d => reasons[d].HasFlag(UnavailableReason.Occupied))
            .ToList();
        var blocked = availability.ConflictingDates
            .Where(d => reasons[d].HasFlag(UnavailableReason.WholePropertyBlock))
            .ToList();

        var sentences = new List<string>();

        // The check-out day is named whenever it is among the conflicts: the stay's nights may all be
        // free, and a conflict on a day the group does not sleep is the one that most needs saying.
        if (byGroup.Count > 0)
        {
            sentences.Add((byGroup.Count == 1 ? "Datumet " : "Datumen ")
                + FormatRuns(byGroup)
                + (byGroup.Count == 1 ? " är redan upptaget." : " är redan upptagna.")
                + " Gården hyrs ut till en grupp i taget"
                + (byGroup.Contains(checkOut) ? " och avresedagen behövs för städning" : "")
                + ", så bokningen kan inte godkännas över de datumen.");
        }

        if (blocked.Count > 0)
        {
            sentences.Add($"Hela anläggningen är blockerad {FormatRuns(blocked)}"
                + (blocked.Contains(checkOut) ? ", och avresedagen behöver vara fri för städning." : ".")
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
