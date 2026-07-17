namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// One cleaning zone selected on a booking's inquiry form (requirements.md §8.12). Keeps a
/// live FK to <see cref="CleaningZone"/> for queries plus a snapshot of the zone's name and
/// price taken at selection time, so the price the guest agreed to survives later edits to the
/// zone (NFR-33). The live reference blocks hard-deleting the zone while selected (FR-88).
/// </summary>
public class BookingCleaningSelection
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }

    public Booking? Booking { get; set; }

    public Guid CleaningZoneId { get; set; }

    public CleaningZone? CleaningZone { get; set; }

    public string ZoneNameSnapshot { get; set; } = string.Empty;

    public decimal AmountExVatSnapshot { get; set; }

    public decimal VatRateSnapshot { get; set; }
}
