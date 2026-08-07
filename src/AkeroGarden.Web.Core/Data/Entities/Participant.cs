using System.ComponentModel.DataAnnotations;

namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// One person on a booking (requirements.md §8.5) — either an <see cref="ParticipantType.Overnight"/>
/// guest (occupies a bed) or a <see cref="ParticipantType.DayGuest"/> (no bed, attends a single
/// <see cref="AttendanceDate"/> for meals). Entered by the booking's contact person on the
/// completion form on the whole group's behalf, or by the admin in the panel.
///
/// <para><b>A roster, not a decision record.</b> This entity answers <i>who is here, how old are
/// they, and what can't they eat</i> — nothing else. Everything else it once carried turned out to
/// be a booking-level decision stored per person, and each was removed rather than kept in sync:</para>
/// <list type="bullet">
///   <item><c>RoomId</c> / <c>RoomAssignmentSnapshot</c> — per-participant room assignment was
///     withdrawn (FR-56 tombstoned). Who sleeps in which room was never information the property
///     used, so the system records only which <b>buildings</b> the group sleeps in — see
///     <see cref="BookingBuildingAssignment"/> (§8.16, FR-103) — and the group divides itself among
///     the rooms offline. <see cref="Room"/> survives as capacity reference data feeding the
///     availability calendar, but is no longer an assignment target for anyone.</item>
///   <item><c>IsFullBoard</c> — catering is all-or-nothing per booking (§4.1), so this was a copy
///     of <see cref="Booking.MealPlan"/> on every row.</item>
///   <item><c>MealPreferences</c> — the group makes one choice per sitting (FR-105); see
///     <see cref="BookingMealSelection"/>.</item>
///   <item><c>AttendsBreakfast</c> — day-guest meal attendance is one booking-level setting
///     (§4.2); overnight guests always attend, which is derived, not stored.</item>
/// </list>
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

    /// <summary>Multi-select medical constraints (§4.6, §8.5) — allergies and intolerances only;
    /// dietary <i>preferences</i> are counts on the booking. Stored as a value-converted string
    /// list — never queried in a WHERE, so no join table or array column is needed.
    ///
    /// <para>Special-category health data under GDPR art. 9: never duplicated into the audit trail
    /// (NFR-37).</para></summary>
    public List<Allergy> Allergies { get; set; } = [];

    /// <summary>Free text for <see cref="Allergy.Other"/> (§4.6) — max 200 chars.</summary>
    [StringLength(200, ErrorMessage = "Allergianteckningen får vara högst 200 tecken.")]
    public string? AllergyNotes { get; set; }
}
