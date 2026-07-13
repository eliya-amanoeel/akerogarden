namespace AkeroGarden.Web.Data.Entities;

/// <summary>
/// An append-only accountability record — who changed what, and when (§6.9, §8.15, FR-95/96).
/// Semi-polymorphic: <see cref="EntityType"/> + <see cref="EntityId"/> identify the affected record
/// of any kind, while the optional hard FK <see cref="BookingId"/> (ON DELETE CASCADE) is set for
/// booking-scoped entries so they are purged with their booking (FR-98) and listable per booking.
///
/// <para><b>No guest personal data (NFR-37).</b> Booking-change entries carry only the action, the
/// acting user's identity, and the booking's reference — never guest names, contact details, or
/// special-category dietary/allergy data (GDPR art. 9). The trail is never edited or deleted through
/// the application (NFR-36); only the retention job removes rows (FR-98).</para>
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; }

    /// <summary>When the action occurred (UTC), set at write time.</summary>
    public DateTime Timestamp { get; set; }

    public AuditActorType ActorType { get; set; }

    /// <summary>The acting admin's ASP.NET Identity user id, when <see cref="ActorType"/> is
    /// <see cref="AuditActorType.Admin"/>; null for Guest/System. Not FK-constrained — Identity
    /// tables are infrastructure (§6.4).</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>The acting admin's email captured at write time (admins can be deactivated/removed —
    /// FR-74); null for Guest/System.</summary>
    public string? ActorEmailSnapshot { get; set; }

    /// <summary>The acting admin's full name captured at write time, for readable attribution;
    /// null for Guest/System or when the admin has no name set.</summary>
    public string? ActorNameSnapshot { get; set; }

    public AuditAction Action { get; set; }

    /// <summary>The kind of record affected — e.g. "Booking", "CleaningZone", "Room".</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>The affected record's id.</summary>
    public Guid EntityId { get; set; }

    /// <summary>FK → Booking (ON DELETE CASCADE) for booking-scoped entries; null for reference-data
    /// and admin-account entries. Drives per-booking listing and retention (FR-98).</summary>
    public Guid? BookingId { get; set; }
    public Booking? Booking { get; set; }

    /// <summary>The affected booking's reference (FR-92) for readability; null for non-booking
    /// entries. Not PII.</summary>
    public string? BookingReferenceSnapshot { get; set; }

    /// <summary>Human-readable Swedish description of the action. For reference-data entries (FR-96)
    /// may include a before/after of changed fields; for booking entries records the action only —
    /// never guest field values (NFR-37).</summary>
    public string Summary { get; set; } = string.Empty;
}
