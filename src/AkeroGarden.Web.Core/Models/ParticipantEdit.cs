using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Models;

/// <summary>
/// The admin's post-submission edits to the <b>group's meals</b> (FR-48/105), applied in the panel
/// without any status change or guest reopen (C10).
///
/// <para>Most of what the admin corrects stopped being per-participant: the meal choices, the
/// edge-day and day-guest meal flags, and the dietary-preference counts are all booking-level facts
/// now (FR-105, §4.2, §4.6), so they live here rather than being repeated on every participant row.
/// <see cref="ParticipantEdit"/> keeps only what genuinely belongs to a person, and the stated times
/// are saved on their own — see <c>BookingService.SaveStatedTimesAsync</c>.</para>
/// </summary>
public class GroupMealEdit
{
    /// <summary>The dish chosen for each meal of the stay, keyed by day and lunch/dinner rather than
    /// by an existing row's id.
    ///
    /// <para><b>Keyed by the meal, not by the row, so a dish can be chosen for a meal that has none
    /// yet.</b> Ticking an arrival- or departure-day meal creates a meal the booking did not have
    /// before; keying by row id meant such a meal could never be given a dish from the panel at all,
    /// because there was no row to render a dropdown for. The admin could produce an "Ej vald" line
    /// on the kitchen list and then had no way to resolve it.</para>
    ///
    /// <para>An entry with no chosen option is legitimate — the admin has added a meal and not yet
    /// decided what it is. That is a normal in-between state, unlike the guest's submission, which
    /// must be complete; the Matlista prints it as "Ej vald" with its headcount so someone
    /// decides.</para></summary>
    public List<MealChoiceEdit> Meals { get; set; } = [];

    // ── Meal occasions (§4.1, §4.2, FR-105) ─────────────────────────────────────
    public bool ArrivalBreakfast { get; set; }

    public bool ArrivalLunch { get; set; }

    public bool ArrivalDinner { get; set; }

    public bool DepartureBreakfast { get; set; }

    public bool DepartureLunch { get; set; }

    public bool DepartureDinner { get; set; }

    public bool DayGuestBreakfast { get; set; }

    public bool DayGuestLunch { get; set; }

    public bool DayGuestDinner { get; set; }

    // ── Dietary preferences (§4.6) ──────────────────────────────────────────────
    public int VegetarianCount { get; set; }

    public int VeganCount { get; set; }
}

/// <summary>
/// An admin's edits to one participant (FR-48) — the roster fields only: who they are, how old, and
/// what they cannot eat. The group's meals moved to <see cref="GroupMealEdit"/>, and the stated
/// times are saved on their own.
/// </summary>
public class ParticipantEdit
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public AgeCategory AgeCategory { get; set; }

    public List<Allergy> Allergies { get; set; } = [];

    public string? AllergyNotes { get; set; }
}

/// <summary>The dish chosen for one meal of the stay (admin correction, FR-48). Identified by when
/// the meal is, not by the id of a row that may not exist yet.</summary>
public class MealChoiceEdit
{
    public DateOnly Date { get; set; }

    public MealType MealType { get; set; }

    /// <summary>The chosen option, or null when the admin has not decided yet.</summary>
    public Guid? MealOptionId { get; set; }
}
