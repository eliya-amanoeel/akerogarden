using AkeroGarden.Web.Core.Data.Entities;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// The resolved identity of whoever performed an audited action (§6.9). Carries only the acting
/// admin's identity — never any guest data (NFR-37). Guest and System actors carry no identity
/// fields; a magic-link completion is simply "the guest", a background job "the system".
/// </summary>
public sealed record AuditActor(AuditActorType Type, Guid? UserId, string? Email, string? Name)
{
    /// <summary>The booking's contact person acting via the completion magic link (FR-54) — no account.</summary>
    public static readonly AuditActor Guest = new(AuditActorType.Guest, null, null, null);

    /// <summary>An automated background job (reminders, retention — FR-63/69).</summary>
    public static readonly AuditActor System = new(AuditActorType.System, null, null, null);

    /// <summary>An authenticated admin acting in the panel.</summary>
    public static AuditActor Admin(Guid userId, string? email, string? name) =>
        new(AuditActorType.Admin, userId, email, name);
}
