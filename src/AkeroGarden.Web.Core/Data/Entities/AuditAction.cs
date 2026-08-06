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
    CompletionLinkRegenerated,
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
}
