using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Models;

/// <summary>
/// The completion form the booking's contact person submits on <c>/slutfor/{token}</c> (Phase 4,
/// §6.6). Bound via <c>[SupplyParameterFromForm]</c> under Static SSR.
///
/// <para><b>Server-authoritative.</b> Nothing here is trusted as-submitted: the overnight count is
/// re-checked against the booking, meal choices are validated against the active meal list and the
/// server-resolved meal occasions (§4.1), and a self-catering booking's meals are dropped —
/// regardless of what the client posts (mirrors <see cref="AkeroGarden.Web.Core.Services.InquiryNormalizer"/>).</para>
/// </summary>
public class CompletionFormModel
{
    /// <summary>One entry per overnight participant. The count must equal the booking's
    /// <c>OvernightGuestCount</c> — the rows are server-pre-rendered, not client-added.</summary>
    public List<OvernightParticipantEntry> Overnight { get; set; } = [];

    /// <summary>Optional day guests (§4.2, FR-58) — variable count, added/removed via server
    /// postback. Only applicable when the booking's meal plan is <c>FullBoardAll</c>; dropped
    /// server-side for <c>SelfCateringAll</c>.</summary>
    public List<DayGuestEntry> DayGuests { get; set; } = [];

    // ── Group meal choice (§4.1, FR-105) ─────────────────────────────────────────
    /// <summary>The group's chosen meal option id (as a string) per sitting, aligned by index to
    /// the server-resolved selection slots
    /// (<see cref="AkeroGarden.Web.Core.Services.ResolvedMealOccasions.SelectionSlots"/>). One
    /// choice per sitting for everybody — nobody selects individually (FR-105).
    ///
    /// <para>Kept as <see cref="string"/> (not <see cref="Guid"/>?) because a placeholder select
    /// posts an empty value for every unselected slot, and the SSR model binder fails to bind ""
    /// into a <c>List&lt;Guid?&gt;</c> element — nulling the whole collection. Parsed to a Guid
    /// server-side in <see cref="AkeroGarden.Web.Core.Services.CompletionNormalizer"/>. Only the id
    /// round-trips; each slot's date + meal type are re-resolved on the server.</para></summary>
    public List<string?> MealChoices { get; set; } = [];

    // ── Edge-day meal occasions (§4.1, FR-105) ───────────────────────────────────
    // Which meals the group takes on the arrival and departure day. Stored on the booking, not
    // derived from the stated check-in/check-out times — see MealOccasionResolver.

    public bool ArrivalBreakfast { get; set; }

    public bool ArrivalLunch { get; set; }

    public bool ArrivalDinner { get; set; }

    public bool DepartureBreakfast { get; set; }

    public bool DepartureLunch { get; set; }

    public bool DepartureDinner { get; set; }

    // ── Day-guest meal attendance (§4.2) ─────────────────────────────────────────
    // One setting for every day guest on the booking, not per day guest.

    public bool DayGuestBreakfast { get; set; }

    public bool DayGuestLunch { get; set; }

    public bool DayGuestDinner { get; set; }

    // ── Dietary preferences (§4.6) ───────────────────────────────────────────────
    /// <summary>How many vegetarian portions the kitchen must prepare. A count, not a per-person
    /// flag — nobody needs to know which individuals they are.</summary>
    public int VegetarianCount { get; set; }

    /// <summary>How many vegan portions the kitchen must prepare (§4.6).</summary>
    public int VeganCount { get; set; }

    // ── Cleaning (FR-93, §5.5) ────────────────────────────────────────────────────
    /// <summary>Huvudbyggnad self-clean/buy toggle. Default false (self-clean is the default).</summary>
    public bool MainBuildingCleaning { get; set; }

    /// <summary>The combined sleeping-building(s) toggle — intent only; the priced selection rows
    /// resolve from the booking's building assignments (§8.12/8.16), not from this flag directly.</summary>
    public bool SleepingCleaning { get; set; }

    /// <summary>Konferensbyggnad toggle — only honored server-side when the Konferensrum facility
    /// was actually selected on the booking (§5.5); ignored otherwise regardless of what posts.</summary>
    public bool ConferenceCleaning { get; set; }

    // ── Allergy consent (FR-110, art. 9.2 a) ─────────────────────────────────────
    /// <summary>Explicit consent to processing the declared allergies, which are health data and
    /// therefore special-category under GDPR art. 9. Required **only when at least one allergy is
    /// actually declared** on this form (see
    /// <see cref="AkeroGarden.Web.Core.Services.CompletionNormalizer.ValidateAllergyConsent"/>).
    ///
    /// <para><b>Why conditional and not always required.</b> Most groups declare no allergies at
    /// all. Demanding consent to process health data nobody supplied is meaningless, and worse, a
    /// consent the guest must give to get past the form is not "freely given" in the sense of art.
    /// 7.4 — making it mandatory would weaken the very basis it exists to establish.</para></summary>
    public bool AllergyConsent { get; set; }

    // ── Org / invoice (FR-60) ────────────────────────────────────────────────────
    public bool IsOrganisation { get; set; }

    public string? OrgName { get; set; }

    public string? OrgNumber { get; set; }

    public string? InvoiceAddress { get; set; }
}

/// <summary>The allergy fields both participant kinds carry (§4.6), so one validation rule covers
/// both rather than being written twice and drifting apart.</summary>
public interface IAllergyEntry
{
    List<Allergy> Allergies { get; }

    string? AllergyNotes { get; }
}

/// <summary>One overnight participant's entry on the completion form (§8.5, FR-57). Name, age and
/// allergies only — meals are the group's single choice, and there is no room to assign.</summary>
public class OvernightParticipantEntry : IAllergyEntry
{
    public string? FullName { get; set; }

    public AgeCategory AgeCategory { get; set; } = AgeCategory.Adult;

    /// <summary>Multi-select allergies (§4.6) — medical constraints only. Empty when none apply.</summary>
    public List<Allergy> Allergies { get; set; } = [];

    public string? AllergyNotes { get; set; }
}

/// <summary>One day guest's entry on the completion form (§4.2, §8.5, FR-58). A day guest occupies
/// no bed and attends a single date for meals only; which meals is one booking-level setting, and
/// what they eat is the group's choice — so neither appears here.</summary>
public class DayGuestEntry : IAllergyEntry
{
    public string? FullName { get; set; }

    public AgeCategory AgeCategory { get; set; } = AgeCategory.Adult;

    /// <summary>The single date attended (§4.2), as a string (<c>yyyy-MM-dd</c>) — kept as string
    /// for the same SSR-binding reason as <see cref="CompletionFormModel.MealChoices"/> (an empty
    /// select value fails to bind into a nullable value type). Parsed server-side.</summary>
    public string? AttendanceDate { get; set; }

    public List<Allergy> Allergies { get; set; } = [];

    public string? AllergyNotes { get; set; }
}
