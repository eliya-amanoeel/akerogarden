namespace AkeroGarden.Web.Core.Data.Entities;

/// <summary>
/// The specific action recorded on an <see cref="AuditLog"/> entry (§8.15). Persisted as a string
/// so the value is legible in the database and stable against reordering. Booking-lifecycle actions
/// (FR-95) are defined here in Slice A; reference-data and admin-account actions (FR-96) are added
/// in Slice B.
/// </summary>
public enum AuditAction
{
    // ── Booking lifecycle (FR-95) ──────────────────────────────────────────────
    BookingCreatedByAdmin,
    BookingApproved,
    BookingDenied,
    InquiryClosed,
    BookingCancelled,
    BookingMarkedArrived,
    BookingMarkedCompleted,
    // One verb for every completion link that reaches a guest, whoever or whatever sent it —
    // approval, the automatic issue at 40 days, an admin resend, or the 14-day nudge. Not split into
    // issued-vs-regenerated: every link after the first invalidates its predecessor (§2.6), so
    // "was something invalidated" is just "is this the first such entry for this booking", readable
    // from the order. The issuance job would also straddle the split, since it both issues first
    // links and replaces expired ones. Each entry's message names the trigger.
    CompletionLinkIssued,
    CompletionSubmitted,
    ParticipantsEdited,
    AdminNotesEdited,
    BuildingAssignmentChanged,

    // ── Reference data (FR-96) — EntityType disambiguates which kind ────────────
    ReferenceDataCreated,
    ReferenceDataUpdated,
    ReferenceDataActivated,
    ReferenceDataDeactivated,
    ReferenceDataDeleted,

    // ── Admin accounts / invitations (FR-96, FR-74) ─────────────────────────────
    AdminInvited,
    AdminInviteResent,
    AdminInviteRemoved,
    AdminAccountActivated,
    AdminAccountDeactivated,
    AdminAccountSetupCompleted,
    AdminRoleChanged,
}
