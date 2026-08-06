using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Models;

/// <summary>
/// The completion form the booking's contact person submits on <c>/slutfor/{token}</c> (Phase 4,
/// §6.6). Bound via <c>[SupplyParameterFromForm]</c> under Static SSR. Slice 3 covers overnight
/// participants + org/invoice; day guests (§4.2) arrive in slice 4 and room assignment (FR-56) in
/// slice 5.
///
/// <para><b>Server-authoritative.</b> Nothing here is trusted as-submitted: the overnight count is
/// re-checked against the booking, meal choices are validated against the active meal list and the
/// server-derived meal slots (§4.1), and self-catering participants have their meals dropped —
/// regardless of what the client posts (mirrors <see cref="AkeroGarden.Web.Core.Services.InquiryNormalizer"/>).</para>
/// </summary>
public class CompletionFormModel
{
    /// <summary>One entry per overnight participant. The count must equal the booking's
    /// <c>OvernightGuestCount</c> — the rows are server-pre-rendered, not client-added.</summary>
    public List<OvernightParticipantEntry> Overnight { get; set; } = [];

    /// <summary>Optional day guests (§4.2, FR-58) — variable count, added/removed via server
    /// postback. Only applicable when the booking's meal plan is <c>FullBoardAll</c> or <c>Mixed</c>;
    /// dropped server-side for <c>SelfCateringAll</c>.</summary>
    public List<DayGuestEntry> DayGuests { get; set; } = [];

    // ── Group defaults (presentation only) ───────────────────────────────────────
    // The "Standard för hela gruppen" convenience controls cascade one choice to every participant
    // via completion-form.js; they are NOT participant data and are ignored by CompletionNormalizer.
    // They are bound to the model purely so their displayed value survives an add/remove-day-guest
    // server round-trip (which re-renders the whole form) instead of blanking out.

    /// <summary>Whether the group-default "Helpension för alla" box is ticked (presentation only).</summary>
    public bool GroupDefaultFullBoard { get; set; }

    /// <summary>Group-default meal choice <b>per serving slot</b> (presentation only), aligned by index
    /// to the same server-derived slot list as <see cref="OvernightParticipantEntry.MealChoices"/>. Each
    /// per-day dropdown in the "Standard för hela gruppen" panel cascades its value to that same slot for
    /// every participant via completion-form.js. Kept as <see cref="string"/> for the same SSR-binding
    /// reason as <see cref="OvernightParticipantEntry.MealChoices"/>; ignored by CompletionNormalizer.</summary>
    public List<string?> GroupDefaultMeals { get; set; } = [];

    // ── Cleaning (FR-93, §5.5) ────────────────────────────────────────────────────
    /// <summary>Huvudbyggnad self-clean/buy toggle. Default false (self-clean is the default).</summary>
    public bool MainBuildingCleaning { get; set; }

    /// <summary>The combined sleeping-building(s) toggle — intent only; the priced selection rows
    /// resolve from room assignment (§8.12), not from this flag directly.</summary>
    public bool SleepingCleaning { get; set; }

    /// <summary>Konferensbyggnad toggle — only honored server-side when the Konferensrum facility
    /// was actually selected on the booking (§5.5); ignored otherwise regardless of what posts.</summary>
    public bool ConferenceCleaning { get; set; }

    // ── Org / invoice (FR-60) ────────────────────────────────────────────────────
    public bool IsOrganisation { get; set; }

    public string? OrgName { get; set; }

    public string? OrgNumber { get; set; }

    public string? InvoiceAddress { get; set; }
}

/// <summary>One overnight participant's entry on the completion form (§8.5, FR-57).</summary>
public class OvernightParticipantEntry
{
    public string? FullName { get; set; }

    public AgeCategory AgeCategory { get; set; } = AgeCategory.Adult;

    /// <summary>Pre-filled from the booking's <c>MealPlan</c>, overridable per participant (§4.1).
    /// When false the participant has no meal selection (self-catering).</summary>
    public bool IsFullBoard { get; set; }

    /// <summary>Chosen meal option id (as a string) per applicable meal slot, aligned by index to
    /// the server-derived slot list (<see cref="AkeroGarden.Web.Core.Services.MealsByDayCalculator"/>).
    /// A null/empty entry is an unselected slot. Kept as <see cref="string"/> (not
    /// <see cref="Guid"/>?) because a placeholder select posts an empty value for every unselected
    /// slot, and the SSR model binder fails to bind "" into a <c>List&lt;Guid?&gt;</c> element —
    /// nulling the whole collection. Parsed to a Guid server-side in <see cref="CompletionNormalizer"/>.
    /// Only the id round-trips; the slot's date + meal type are re-derived on the server.</summary>
    public List<string?> MealChoices { get; set; } = [];

    /// <summary>Multi-select dietary requirements (§4.6). Empty when none apply.</summary>
    public List<DietaryRequirement> Dietary { get; set; } = [];

    public string? DietaryNotes { get; set; }

}

/// <summary>One day guest's entry on the completion form (§4.2, §8.5, FR-58). A day guest occupies
/// no bed and attends a single date for meals only.</summary>
public class DayGuestEntry
{
    public string? FullName { get; set; }

    public AgeCategory AgeCategory { get; set; } = AgeCategory.Adult;

    /// <summary>The single date attended (§4.2), as a string (<c>yyyy-MM-dd</c>) — kept as string
    /// for the same SSR-binding reason as <see cref="OvernightParticipantEntry.MealChoices"/>
    /// (an empty select value fails to bind into a nullable value type). Parsed server-side.</summary>
    public string? AttendanceDate { get; set; }

    /// <summary>Whether the day guest attends the breakfast buffet (no meal selection needed).</summary>
    public bool AttendsBreakfast { get; set; }

    /// <summary>Chosen lunch option id (string; empty = not attending lunch).</summary>
    public string? LunchChoice { get; set; }

    /// <summary>Chosen dinner option id (string; empty = not attending dinner).</summary>
    public string? DinnerChoice { get; set; }

    public List<DietaryRequirement> Dietary { get; set; } = [];

    public string? DietaryNotes { get; set; }
}
