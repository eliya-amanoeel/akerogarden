using System.Globalization;
using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Services;

/// <summary>Swedish display labels for booking and job-run enums (NFR-32), shared across the admin pages.</summary>
public static class BookingDisplay
{
    private static readonly CultureInfo Sv = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>
    /// A booking amount the Swedish way — <c>21 250 kr</c>, digits grouped and non-breaking, so the
    /// number never wraps away from its unit. Shared by the guest's emails and the admin page
    /// (§5.6), so the price the admin confirmed and the price the guest reads are written alike.
    /// </summary>
    public static string Kronor(decimal amount) => $"{amount.ToString("#,0.##", Sv)}\u00A0kr";

    public static string StatusLabel(BookingStatus status) => status switch
    {
        BookingStatus.Pending => "Väntar",
        BookingStatus.AwaitingDeposit => "Väntar på handpenning",
        BookingStatus.Approved => "Godkänd",
        BookingStatus.Denied => "Avslagen",
        BookingStatus.DetailsComplete => "Uppgifter klara",
        BookingStatus.Active => "På plats",
        BookingStatus.Completed => "Avslutad",
        BookingStatus.Cancelled => "Avbokad",
        BookingStatus.Closed => "Besvarad",
        _ => status.ToString()
    };

    /// <summary>CSS class for the coloured status badge (Phase 6 admin). Pairs with
    /// <see cref="StatusLabel"/>. All colour combinations are WCAG-AA on white.</summary>
    public static string StatusBadgeClass(BookingStatus status) => status switch
    {
        BookingStatus.Pending => "badge badge--pending",
        BookingStatus.AwaitingDeposit => "badge badge--deposit",
        BookingStatus.Approved => "badge badge--approved",
        BookingStatus.DetailsComplete => "badge badge--complete",
        BookingStatus.Active => "badge badge--active",
        BookingStatus.Completed => "badge badge--done",
        BookingStatus.Closed => "badge badge--done",
        BookingStatus.Denied => "badge badge--denied",
        BookingStatus.Cancelled => "badge badge--cancelled",
        _ => "badge badge--neutral"
    };

    /// <summary>Label for a booking's status badge on any admin screen. A booking waiting for its
    /// deposit past the due date reads <c>Handpenning förfallen</c> rather than its plain status
    /// (FR-116): it holds dates nobody else can book, and that must show wherever the status shows,
    /// not only on the dashboard. Not a status of its own — the filter still lists statuses.</summary>
    public static string StatusLabel(Booking booking, DateOnly today) =>
        DepositPolicy.IsDepositOverdue(booking.Status, booking.DepositDueDate, today)
            ? "Handpenning förfallen"
            : StatusLabel(booking.Status);

    /// <summary>Badge class pairing with <see cref="StatusLabel(Booking, DateOnly)"/>. Overdue reuses
    /// the red badge, and the word travels with it, so colour is never the only signal.</summary>
    public static string StatusBadgeClass(Booking booking, DateOnly today) =>
        DepositPolicy.IsDepositOverdue(booking.Status, booking.DepositDueDate, today)
            ? "badge badge--denied"
            : StatusBadgeClass(booking.Status);

    /// <summary>Outcome label for a nightly job run (FR-114). Null = not finished: still running,
    /// or the process stopped mid-run.</summary>
    public static string JobRunOutcomeLabel(JobRunOutcome? outcome) => outcome switch
    {
        JobRunOutcome.Succeeded => "Lyckades",
        JobRunOutcome.Failed => "Misslyckades",
        _ => "Ej avslutad",
    };

    /// <summary>The job history's result filter, worded exactly as the outcome badges it selects.</summary>
    public static string JobRunResultLabel(JobRunResult result) => result switch
    {
        JobRunResult.Succeeded => JobRunOutcomeLabel(JobRunOutcome.Succeeded),
        JobRunResult.Failed => JobRunOutcomeLabel(JobRunOutcome.Failed),
        _ => JobRunOutcomeLabel(null),
    };

    /// <summary>The status badge for a job run — green / red reuse the booking status badges
    /// (<see cref="StatusBadgeClass"/>) rather than adding colours, so the label always travels with
    /// the colour and the contrast work done for them applies here too.</summary>
    public static string JobRunOutcomeBadgeClass(JobRunOutcome? outcome) => outcome switch
    {
        JobRunOutcome.Succeeded => "badge badge--approved",
        JobRunOutcome.Failed => "badge badge--denied",
        _ => "badge badge--neutral",
    };

    public static string TypeLabel(BookingType type) => type switch
    {
        BookingType.StayRequest => "Bokningsförfrågan",
        BookingType.GeneralInquiry => "Allmän förfrågan",
        _ => type.ToString()
    };

    public static string MealPlanLabel(MealPlan? plan) => plan switch
    {
        MealPlan.FullBoardAll => "Helpension för alla",
        MealPlan.SelfCateringAll => "Självhushåll för alla",
        _ => "—"
    };

    public static string ScopeLabel(BlockScope scope) => scope switch
    {
        BlockScope.WholeProperty => "Hela anläggningen",
        BlockScope.Building => "Byggnad",
        BlockScope.Room => "Rum",
        _ => scope.ToString()
    };

    public static string ParticipantTypeLabel(ParticipantType type) => type switch
    {
        ParticipantType.Overnight => "Övernattande",
        ParticipantType.DayGuest => "Dagsgäst",
        _ => type.ToString()
    };

    public static string AgeLabel(AgeCategory age) => age switch
    {
        AgeCategory.Adult => "Vuxen (4+)",
        AgeCategory.Child => "Barn (0–3)",
        _ => age.ToString()
    };

    public static string MealTypeLabel(MealType meal) => meal switch
    {
        MealType.Lunch => "Lunch",
        MealType.Dinner => "Middag",
        _ => meal.ToString()
    };

    /// <summary>The dish's diet as the admin chooses it (FR-22). The public menu says it as a label
    /// on the dish instead — "Vegetariskt" / "Veganskt" (FR-83).</summary>
    public static string DishDietLabel(DishDiet diet) => diet switch
    {
        DishDiet.None => "Ingen",
        DishDiet.Vegetarian => "Vegetarisk",
        DishDiet.Vegan => "Vegansk",
        _ => diet.ToString()
    };

    public static string AllergyLabel(Allergy allergy) => allergy switch
    {
        Allergy.None => "Inga allergier",
        Allergy.Gluten => "Glutenintolerans",
        Allergy.Lactose => "Laktosintolerans",
        Allergy.Nut => "Nötallergi",
        Allergy.Other => "Annat",
        _ => allergy.ToString()
    };

    /// <summary>Joined Swedish allergy summary for a participant — the one rendering every screen,
    /// list and email uses. Returns "—" only when nothing is declared.
    ///
    /// <para><b>A written note is always shown.</b> A note is a declared allergy whether or not a
    /// box is ticked (the completion form treats it so, FR-110), so it is folded into "Annat" when
    /// that is ticked and otherwise appended — "Glutenintolerans · svår" or the note alone. It used
    /// to appear only beside a ticked "Annat", so a note on its own read "—" in the guest's summary
    /// email and the participant list, and the completion form offered "Lägg till allergi" over
    /// it; only the kitchen list had worked around it.</para></summary>
    public static string AllergySummary(IEnumerable<Allergy> allergies, string? notes)
    {
        var ticked = allergies.Where(a => a != Allergy.None).Distinct().ToList();
        var note = notes?.Trim();
        var hasNote = !string.IsNullOrEmpty(note);

        var parts = ticked.Select(a =>
            a == Allergy.Other && hasNote ? $"{AllergyLabel(a)} ({note})" : AllergyLabel(a));
        var summary = string.Join(", ", parts);

        if (hasNote && !ticked.Contains(Allergy.Other))
            summary = summary.Length == 0 ? note! : $"{summary} · {note}";

        return summary.Length == 0 ? "—" : summary;
    }

    /// <summary>The booking's dietary-preference counts as one Swedish line (§4.6) — "3 vegetariska,
    /// 1 vegansk". Returns "—" when neither is requested.</summary>
    public static string PreferenceSummary(int vegetarianCount, int veganCount)
    {
        var parts = new List<string>(2);
        if (vegetarianCount > 0) parts.Add($"{vegetarianCount} vegetariska");
        if (veganCount > 0) parts.Add($"{veganCount} veganska");
        return parts.Count == 0 ? "—" : string.Join(", ", parts);
    }

    /// <summary>A stated check-in/check-out time (§2.8, FR-100), falling back to the standard when a
    /// booking predates the field or never stated one.</summary>
    public static string TimeLabel(TimeOnly? time, TimeOnly standard) =>
        (time ?? standard).ToString("HH\\:mm");
}
