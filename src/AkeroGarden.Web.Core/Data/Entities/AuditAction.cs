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

    // ── Nightly jobs (FR-114) — always the System actor, tagged with the run ─────
    // What the jobs did that the booking-lifecycle actions above do not already cover. Link issuance
    // stays CompletionLinkIssued (one verb, see above).
    PreArrivalReminderSent,
    DeletionWarningSent,
    // Written without BookingId on purpose: a booking-scoped entry is cascade-deleted with the very
    // booking it describes (FR-98), so the record that it was deleted would vanish with it. Kept by
    // reference only, it falls under the non-booking two-year retention instead.
    BookingDeletedByRetention,
    // Sending never throws (NFR-13), so without this a failed email left no trace in the data at all.
    // Its presence in a run marks the run Misslyckades.
    EmailSendFailed,
}
