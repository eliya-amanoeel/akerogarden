namespace AkeroGarden.Web.Data.Entities;

/// <summary>
/// Whether a participant occupies a bed overnight or attends for meals only (requirements.md
/// §8.5, §4.2). An <see cref="Overnight"/> participant may be assigned a room; a
/// <see cref="DayGuest"/> occupies no bed or room and attends a single date
/// (<see cref="Participant.AttendanceDate"/>). Persisted as a string for legibility.
/// </summary>
public enum ParticipantType
{
    Overnight,
    DayGuest
}
