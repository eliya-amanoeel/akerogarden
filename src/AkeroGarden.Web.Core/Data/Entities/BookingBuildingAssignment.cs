namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// One lodging building a group sleeps in (requirements.md §8.16, FR-103). Assigned by the admin
/// before approval and editable for the life of the booking; one row per assigned building. The
/// group divides itself among the rooms inside those buildings and the system does not record who
/// sleeps where — <see cref="Room"/> is capacity reference data only, never an assignment target.
///
/// Keeps a live FK to <see cref="Building"/> for queries plus a snapshot of the building name and
/// its physical building taken at assignment time, so a later rename of that reference data does
/// not rewrite what was agreed (NFR-16). The live reference blocks hard-deleting the building
/// while it is assigned (FR-16).
/// </summary>
public class BookingBuildingAssignment
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }

    public Booking? Booking { get; set; }

    public Guid BuildingId { get; set; }

    public Building? Building { get; set; }

    public string BuildingNameSnapshot { get; set; } = string.Empty;

    public string PhysicalBuildingNameSnapshot { get; set; } = string.Empty;

    public DateTime AssignedAt { get; set; }
}
