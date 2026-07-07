using AkeroGarden.Web.Data.Entities;

namespace AkeroGarden.Web.Services;

/// <summary>Swedish display labels for booking enums (NFR-32), shared across the admin pages.</summary>
public static class BookingDisplay
{
    public static string StatusLabel(BookingStatus status) => status switch
    {
        BookingStatus.Pending => "Väntar",
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
        BookingStatus.Approved => "badge badge--approved",
        BookingStatus.DetailsComplete => "badge badge--complete",
        BookingStatus.Active => "badge badge--active",
        BookingStatus.Completed => "badge badge--done",
        BookingStatus.Closed => "badge badge--done",
        BookingStatus.Denied => "badge badge--denied",
        BookingStatus.Cancelled => "badge badge--cancelled",
        _ => "badge badge--neutral"
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
        MealPlan.Mixed => "Blandat",
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
        AgeCategory.Adult => "Vuxen (13+)",
        AgeCategory.Child => "Barn (4–12)",
        AgeCategory.Infant => "Barn (0–3)",
        _ => age.ToString()
    };

    public static string MealTypeLabel(MealType meal) => meal switch
    {
        MealType.Lunch => "Lunch",
        MealType.Dinner => "Middag",
        _ => meal.ToString()
    };

    public static string DietaryLabel(DietaryRequirement diet) => diet switch
    {
        DietaryRequirement.None => "Inga",
        DietaryRequirement.Vegetarian => "Vegetarian",
        DietaryRequirement.Vegan => "Vegan",
        DietaryRequirement.GlutenFree => "Glutenintolerans",
        DietaryRequirement.LactoseFree => "Laktosintolerans",
        DietaryRequirement.NutAllergy => "Nötallergi",
        DietaryRequirement.Other => "Annat",
        _ => diet.ToString()
    };

    /// <summary>Joined Swedish dietary summary for a participant, appending free-text notes for
    /// <see cref="DietaryRequirement.Other"/>. Returns "—" when there are no requirements.</summary>
    public static string DietarySummary(IEnumerable<DietaryRequirement> requirements, string? notes)
    {
        var relevant = requirements.Where(d => d != DietaryRequirement.None).ToList();
        if (relevant.Count == 0) return "—";
        var parts = relevant.Select(d =>
            d == DietaryRequirement.Other && !string.IsNullOrWhiteSpace(notes)
                ? $"{DietaryLabel(d)} ({notes.Trim()})"
                : DietaryLabel(d));
        return string.Join(", ", parts);
    }
}
