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
}
