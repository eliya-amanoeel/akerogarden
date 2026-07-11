namespace AkeroGarden.Web.Data.Entities;

/// <summary>
/// Drives when a <see cref="CleaningZone"/> is offered and which completion-form toggle it
/// belongs to (requirements.md §8.11, §5.5, FR-93). Persisted as a string for legibility and
/// stability against reordering.
/// </summary>
public enum CleaningZoneKind
{
    /// <summary>Huvudbyggnad — offered whenever the booking has any overnight lodging.</summary>
    MainBuilding,

    /// <summary>A sleeping-building zone — offered when a room is assigned in a
    /// <see cref="Building"/> whose <see cref="Building.PhysicalBuildingId"/> matches.</summary>
    SleepingBuilding,

    /// <summary>Konferensbyggnad — offered when a booked <see cref="Facility"/> whose
    /// <see cref="Facility.PhysicalBuildingId"/> matches is selected.</summary>
    Conference,

    /// <summary>Hela lägret — rate-card reference only, not a guest option (§5.5).</summary>
    Everything
}
