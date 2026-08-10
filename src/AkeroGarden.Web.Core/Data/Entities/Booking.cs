using System.ComponentModel.DataAnnotations;

namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// A booking — either a stay request or a general inquiry (requirements.md §8.1).
/// For a <see cref="BookingType.GeneralInquiry"/> the stay-specific fields (dates, times, guest
/// counts, meal plan, meal occasions and selections, building assignments, dietary-preference
/// counts, other selections, org/invoice, completion token) are null/unused; only the contact
/// fields, <see cref="Message"/>, <see cref="BookingType"/>, <see cref="Status"/>,
/// <see cref="AdminNotes"/>, and the retention fields apply.
///
/// <para><b>Status is one-way (§2.5).</b> Mutate it only through
/// <see cref="AkeroGarden.Web.Core.Services.BookingService"/>, which routes every change through
/// <see cref="AkeroGarden.Web.Core.Services.BookingStateMachine"/> so illegal transitions are
/// blocked in code, not just hidden in the UI.</para>
///
/// <para><b>No monetary totals and no payment state (C8, FR-42 retired).</b> No booking total,
/// deposit amount, or balance is stored, and no invoicing/payment flags either — invoicing happens
/// entirely outside the system, so a flag here tracked nothing the system could act on.</para>
/// </summary>
public class Booking
{
    public Guid Id { get; set; }

    /// <summary>Database-generated identity — a global monotonic sequence (never reused, no
    /// per-year reset). Powers <see cref="BookingReference"/> (FR-92).</summary>
    public int SequenceNumber { get; set; }

    /// <summary>Human-readable reference shown to guest and admin alike, format
    /// <c>AG-YYYY-NNNN</c> (e.g. <c>AG-2026-0042</c>). <c>YYYY</c> is the creation year;
    /// <c>NNNN</c> is <see cref="SequenceNumber"/> zero-padded to 4 digits. Frozen at creation,
    /// never regenerated — not PII and not a security credential (FR-92).</summary>
    public string BookingReference { get; set; } = string.Empty;

    public BookingType BookingType { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;

    [Required(ErrorMessage = "Namn krävs.")]
    [StringLength(200, ErrorMessage = "Namnet får vara högst 200 tecken.")]
    public string ContactName { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-postadress krävs.")]
    [EmailAddress(ErrorMessage = "Ange en giltig e-postadress.")]
    [StringLength(320, ErrorMessage = "E-postadressen får vara högst 320 tecken.")]
    public string ContactEmail { get; set; } = string.Empty;

    [Phone(ErrorMessage = "Ange ett giltigt telefonnummer.")]
    [StringLength(50, ErrorMessage = "Telefonnumret får vara högst 50 tecken.")]
    public string? ContactPhone { get; set; }

    // ── Stay-specific (null for general inquiries) ──────────────────────────────
    public DateOnly? CheckIn { get; set; }

    public DateOnly? CheckOut { get; set; }

    /// <summary>The guest's stated arrival time (FR-100, §2.8), pre-filled with the standard 14:00.
    /// A request, not a commitment — the admin may adjust it, and any early-arrival surcharge is
    /// still negotiated outside the system.
    ///
    /// <para><b>Deliberately a separate <see cref="TimeOnly"/> rather than folding time into
    /// <see cref="CheckIn"/>.</b> The date values are load-bearing for availability computation,
    /// retention arithmetic, blocked periods and the turnover window; turning them into timestamps
    /// would ripple through all of it for no benefit.</para></summary>
    public TimeOnly? CheckInTime { get; set; }

    /// <summary>The guest's stated departure time (FR-100, §2.8), pre-filled with the standard
    /// 12:00. Same reasoning as <see cref="CheckInTime"/>.</summary>
    public TimeOnly? CheckOutTime { get; set; }

    public int? OvernightGuestCount { get; set; }

    /// <summary>Inquiry-stage estimate of guests attending meals only (no bed). The
    /// authoritative day-guest list comes from the day-guest Participant records (Phase 4).</summary>
    public int? DayGuestCount { get; set; }

    public MealPlan? MealPlan { get; set; }

    // ── Meal occasions (§4.1, §4.2, FR-105) ─────────────────────────────────────
    // The edge days are *stored*; the middle days are derived. Read them through
    // MealOccasionResolver rather than directly — it is the single resolved answer to "which meals
    // occur on day X of this stay", and re-deriving anywhere else reintroduces the second source of
    // truth these fields exist to remove.

    /// <summary>Which meals the group takes on the <b>arrival day</b> (FR-105). Stored, not derived
    /// from <see cref="CheckInTime"/>: a group arriving at 11:00 may perfectly reasonably want
    /// breakfast, and any cutoff rule would be wrong often enough to be worse than trusting the
    /// group (§4.1). Created with the standard schedule pre-filled — arrival dinner only — so a
    /// booking always has a coherent meal state before the guest reaches the completion form.</summary>
    public bool ArrivalBreakfast { get; set; }

    public bool ArrivalLunch { get; set; }

    public bool ArrivalDinner { get; set; }

    /// <summary>Which meals the group takes on the <b>departure day</b> (FR-105). Stored, not
    /// derived from <see cref="CheckOutTime"/> — see <see cref="ArrivalBreakfast"/>. Created with
    /// the standard schedule pre-filled: departure breakfast only.</summary>
    public bool DepartureBreakfast { get; set; }

    public bool DepartureLunch { get; set; }

    public bool DepartureDinner { get; set; }

    /// <summary>Which meals day guests attend — <b>one setting for every day guest on the
    /// booking</b>, not per day guest (§4.2).
    ///
    /// <para><b>Required, not merely convenient.</b> A day guest's attendance used to be implied by
    /// which meal rows existed for them; once meal selection moved to booking level that signal
    /// disappeared entirely, and without these flags a dinner-only visitor is indistinguishable
    /// from one eating all three — making the kitchen counts wrong.</para></summary>
    public bool DayGuestBreakfast { get; set; }

    public bool DayGuestLunch { get; set; }

    public bool DayGuestDinner { get; set; }

    // ── Dietary preferences (§4.6) ──────────────────────────────────────────────
    /// <summary>Number of vegetarian portions required (§4.6). A count, not a per-person flag — the
    /// kitchen needs the number, not the names, so this stores strictly less personal data than the
    /// per-participant list it replaced.</summary>
    public int VegetarianCount { get; set; }

    /// <summary>Number of vegan portions required (§4.6). See <see cref="VegetarianCount"/>.</summary>
    public int VeganCount { get; set; }

    /// <summary>The group's meal choice per sitting (§8.6, FR-105) — one row per (date, lunch|dinner)
    /// with an option-name snapshot. Empty until the guest submits the completion form, and always
    /// empty for a <see cref="Data.Entities.MealPlan.SelfCateringAll"/> booking.</summary>
    public List<BookingMealSelection> MealSelections { get; set; } = [];

    // ── Organisation / invoice (conditional) ────────────────────────────────────
    public bool IsOrganisation { get; set; }

    [StringLength(200, ErrorMessage = "Organisationsnamnet får vara högst 200 tecken.")]
    public string? OrgName { get; set; }

    [StringLength(50, ErrorMessage = "Organisationsnumret får vara högst 50 tecken.")]
    public string? OrgNumber { get; set; }

    [StringLength(500, ErrorMessage = "Fakturaadressen får vara högst 500 tecken.")]
    public string? InvoiceAddress { get; set; }

    // ── Selections (child records with price snapshots — NFR-33/34) ─────────────
    public List<BookingFacilitySelection> FacilitySelections { get; set; } = [];

    public List<BookingCleaningSelection> CleaningSelections { get; set; } = [];

    /// <summary>The lodging buildings the group sleeps in (§8.16, FR-103). Assigned by the admin
    /// before approval and editable afterwards; empty for general inquiries and for stay requests
    /// with no overnight guests.</summary>
    public List<BookingBuildingAssignment> BuildingAssignments { get; set; } = [];

    // ── Completion-form cleaning choice (FR-93, §5.5) ────────────────────────────
    /// <summary>Huvudbyggnad self-clean/buy toggle. Snapshot row written to
    /// <see cref="CleaningSelections"/> at completion submission. Null until completion / for
    /// general inquiries.</summary>
    public bool? MainBuildingCleaningPurchased { get; set; }

    /// <summary>The combined sleeping-building(s) toggle — intent flag. Priced selection rows for
    /// the occupied <see cref="CleaningZoneKind.SleepingBuilding"/> zone(s) are resolved from room
    /// assignment (§8.5/8.11) and snapshotted when assignment is set/changed.</summary>
    public bool? SleepingCleaningPurchased { get; set; }

    /// <summary>Konferensbyggnad toggle — shown/settable only when the Konferensrum facility is
    /// booked. Snapshot row written at completion submission.</summary>
    public bool? ConferenceCleaningPurchased { get; set; }

    /// <summary>Participants captured on the completion form (Phase 4, §8.5) — overnight guests
    /// and day guests. Empty until the guest submits the completion form.</summary>
    public List<Participant> Participants { get; set; } = [];

    // ── Message / notes ─────────────────────────────────────────────────────────
    [StringLength(5000, ErrorMessage = "Meddelandet får vara högst 5000 tecken.")]
    public string? Message { get; set; }

    /// <summary>Internal admin notes — never shown to the guest (§8.1, FR-39).</summary>
    public string? AdminNotes { get; set; }

    /// <summary>Required when moving to <see cref="BookingStatus.Denied"/>; shown to the guest
    /// in the denial email (FR-50).</summary>
    [StringLength(2000, ErrorMessage = "Avslagsmotiveringen får vara högst 2000 tecken.")]
    public string? DenialReason { get; set; }

    // ── Completion token (magic link — §2.6) ────────────────────────────────────
    public string? CompletionToken { get; set; }

    public DateTime? CompletionTokenExpiry { get; set; }

    // ── Cancellation (§8.1, FR-76–79) ───────────────────────────────────────────
    /// <summary>Set when status changes to <see cref="BookingStatus.Cancelled"/>; trigger for
    /// the 12-month GDPR retention.</summary>
    public DateTime? CancelledAt { get; set; }

    public CancellationInitiator? CancellationInitiator { get; set; }

    [StringLength(2000, ErrorMessage = "Avbokningsorsaken får vara högst 2000 tecken.")]
    public string? CancellationReason { get; set; }

    // ── Provenance / audit ──────────────────────────────────────────────────────
    /// <summary>True when the admin created the booking on behalf of a guest (FR-47).</summary>
    public bool CreatedByAdmin { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // ── Retention (FR-91; deletion job is Phase 5) ──────────────────────────────
    /// <summary>Computed per FR-91 by <see cref="AkeroGarden.Web.Core.Services.RetentionCalculator"/>;
    /// null for the live in-progress states (Approved/DetailsComplete/Active).</summary>
    public DateOnly? DeleteAfter { get; set; }

    /// <summary>Set when the 30-day pre-deletion warning is sent, so the daily job does not
    /// re-send it (FR-66).</summary>
    public DateTime? DeletionWarningEmailSentAt { get; set; }

    /// <summary>Set when the <b>pre-arrival reminder</b> is sent 14 days before check-in (FR-63/64),
    /// so the daily job fires it exactly once. Covers that email only — the completion nudge has its
    /// own flag; see <see cref="CompletionNudgeSentAt"/> for why they must never share one.</summary>
    public DateTime? ReminderEmailSentAt { get; set; }

    /// <summary>Set whenever a completion link is emailed to the guest — by the approval email, by
    /// the automatic issue roughly six weeks before check-in (FR-108), by an admin resend (FR-49), or
    /// by the completion nudge (FR-63). It dates the link for the nudge's 3-day grace period, and
    /// that is its only use.
    ///
    /// <para><b>Deliberately not what the issuance job keys on.</b> That job asks whether a
    /// <i>usable</i> link exists (FR-108), because a link that was sent and has since expired leaves
    /// the guest exactly as stranded as one that was never sent — which is what an admin sending the
    /// link early on a far-out booking produces.</para></summary>
    public DateTime? CompletionLinkSentAt { get; set; }

    /// <summary>Set when the <b>completion nudge</b> is sent 14 days before check-in to a booking
    /// still awaiting completion (FR-63); fires exactly once.
    ///
    /// <para><b>Tracked separately from <see cref="ReminderEmailSentAt"/> on purpose.</b> One booking
    /// may legitimately receive both, and while the two shared a flag a guest who was nudged and then
    /// submitted was marked "reminded" and never received the pre-arrival email with the address and
    /// times at all — every booking approved inside the 14-day window lost it. Do not merge them
    /// back.</para></summary>
    public DateTime? CompletionNudgeSentAt { get; set; }
}
