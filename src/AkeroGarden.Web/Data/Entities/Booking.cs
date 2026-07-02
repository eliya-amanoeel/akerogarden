using System.ComponentModel.DataAnnotations;

namespace AkeroGarden.Web.Data.Entities;

/// <summary>
/// A booking — either a stay request or a general inquiry (requirements.md §8.1).
/// For a <see cref="BookingType.GeneralInquiry"/> the stay-specific fields (dates, guest
/// counts, meal plan, room choice, selections, org/invoice, deposit flags, completion token)
/// are null/unused; only the contact fields, <see cref="Message"/>, <see cref="BookingType"/>,
/// <see cref="Status"/>, <see cref="AdminNotes"/>, and the retention fields apply.
///
/// <para><b>Status is one-way (§2.5).</b> Mutate it only through
/// <see cref="AkeroGarden.Web.Services.BookingService"/>, which routes every change through
/// <see cref="AkeroGarden.Web.Services.BookingStateMachine"/> so illegal transitions are
/// blocked in code, not just hidden in the UI.</para>
///
/// <para><b>No monetary totals (C8).</b> No booking total, deposit amount, or balance is
/// stored — deposit/final tracking is boolean status only.</para>
/// </summary>
public class Booking
{
    public Guid Id { get; set; }

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

    public int? OvernightGuestCount { get; set; }

    /// <summary>Inquiry-stage estimate of guests attending meals only (no bed). The
    /// authoritative day-guest list comes from the day-guest Participant records (Phase 4).</summary>
    public int? DayGuestCount { get; set; }

    public MealPlan? MealPlan { get; set; }

    /// <summary>True = the contact person assigns rooms on the completion form; false = admin
    /// assigns rooms in the panel (§8.1, FR-56/57). Booking-wide, not per-participant.</summary>
    public bool GuestChoosesRooms { get; set; }

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

    // ── Deposit / final payment tracking (boolean status only — C8) ─────────────
    public bool DepositInvoiced { get; set; }

    public bool DepositPaid { get; set; }

    public bool FinalInvoiced { get; set; }

    public bool FinalPaid { get; set; }

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
    /// <summary>Computed per FR-91 by <see cref="AkeroGarden.Web.Services.RetentionCalculator"/>;
    /// null for the live in-progress states (Approved/DetailsComplete/Active).</summary>
    public DateOnly? DeleteAfter { get; set; }

    /// <summary>Set when the 30-day pre-deletion warning is sent, so the daily job does not
    /// re-send it (FR-66).</summary>
    public DateTime? DeletionWarningEmailSentAt { get; set; }
}
