namespace AkeroGarden.Web.Data.Entities;

/// <summary>
/// One facility selected on a booking's inquiry form (requirements.md §8.13). Keeps a live
/// FK to <see cref="Facility"/> for queries plus a snapshot of the facility's name and price
/// taken at selection time, so the price the guest agreed to survives later edits to the
/// facility (NFR-34). The live reference blocks hard-deleting the facility while selected (FR-30).
/// </summary>
public class BookingFacilitySelection
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }

    public Booking? Booking { get; set; }

    public Guid FacilityId { get; set; }

    public Facility? Facility { get; set; }

    public string FacilityNameSnapshot { get; set; } = string.Empty;

    public decimal AmountExVatSnapshot { get; set; }

    public decimal VatRateSnapshot { get; set; }
}
