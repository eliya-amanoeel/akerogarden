using System.ComponentModel.DataAnnotations;

namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// One person on a booking (requirements.md §8.5) — either an <see cref="ParticipantType.Overnight"/>
/// guest (occupies a bed, may be assigned a room) or a <see cref="ParticipantType.DayGuest"/>
/// (no bed/room, attends a single <see cref="AttendanceDate"/> for meals). Entered by the booking's
/// contact person on the completion form on the whole group's behalf, or by the admin in the panel.
///
/// <para><b>Snapshot pattern (NFR-16).</b> <see cref="RoomId"/> is the live link that powers
/// conflict detection, occupancy views, and room exclusion (FR-56); <see cref="RoomAssignmentSnapshot"/>
/// preserves the room name + building agreed at assignment time so history survives later edits to
/// the Room record.</para>
/// </summary>
public class Participant
{
    public Guid Id { get; set; }

    public Guid BookingId { get; set; }

    public Booking? Booking { get; set; }

    /// <summary>Required for fire-safety/headcount purposes (FR-57).</summary>
    [Required(ErrorMessage = "Namn krävs.")]
    [StringLength(200, ErrorMessage = "Namnet får vara högst 200 tecken.")]
    public string FullName { get; set; } = string.Empty;

    public ParticipantType ParticipantType { get; set; }

    /// <summary>Age tier — drives the accommodation pricing tier (Adult/Child paid, Infant free)
    /// as reference data only; no total is computed (C8, §5.1/5.2).</summary>
    public AgeCategory AgeCategory { get; set; }

    /// <summary>Day guests only — the single date attended (§4.2, I12). Null for overnight
    /// participants; a visitor attending several days is entered as one record per date.</summary>
    public DateOnly? AttendanceDate { get; set; }

    /// <summary>Live FK → <see cref="Room"/> (§8.5). Null for day guests and for not-yet-assigned
    /// overnight participants. Enables room-conflict detection, occupancy views, and exclusion of
    /// assigned/blocked rooms (FR-21, FR-56).</summary>
    public Guid? RoomId { get; set; }

    public Room? Room { get; set; }

    /// <summary>Room name + building captured at assignment time, for history (NFR-16); the live
    /// link is <see cref="RoomId"/>. Null for day guests and until a room is assigned.</summary>
    [StringLength(300)]
    public string? RoomAssignmentSnapshot { get; set; }

    /// <summary>Pre-populated from the booking's <see cref="Booking.MealPlan"/>
    /// (<c>FullBoardAll</c> → true, <c>SelfCateringAll</c> → false, <c>Mixed</c> → set per
    /// participant); overridable by the contact person on the completion form and by the admin
    /// in the panel (§4.1, FR-57). Self-catering participants have no meal selection.</summary>
    public bool IsFullBoard { get; set; }

    /// <summary>Day guests only — true if attending the breakfast buffet (§8.5, FR-58). Always
    /// true for overnight guests (no selection needed — breakfast is included).</summary>
    public bool AttendsBreakfast { get; set; }

    /// <summary>Multi-select dietary requirements (§4.6, §8.5). Stored as a value-converted
    /// string list — never queried in a WHERE, so no join table or array column is needed.</summary>
    public List<DietaryRequirement> DietaryRequirements { get; set; } = [];

    /// <summary>Free text for <see cref="DietaryRequirement.Other"/> (§4.6) — max 200 chars.</summary>
    [StringLength(200, ErrorMessage = "Kostanteckningen får vara högst 200 tecken.")]
    public string? DietaryNotes { get; set; }

    /// <summary>The per-day meal selections for a full-board participant (§8.6). Empty for
    /// self-catering participants and for day guests who do not attend a catered meal.</summary>
    public List<MealPreference> MealPreferences { get; set; } = [];
}
