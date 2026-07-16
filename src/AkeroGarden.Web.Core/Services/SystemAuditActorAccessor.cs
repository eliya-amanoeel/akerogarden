namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// <see cref="IAuditActorAccessor"/> for the public host, which has no Identity and no Blazor auth
/// state. The public surface never resolves an admin actor: its only audited path is completion,
/// which passes <see cref="AuditActor.Guest"/> explicitly (FR-54), so this accessor is never
/// actually invoked — it exists only to satisfy <see cref="AuditWriter"/>'s constructor. It returns
/// <see cref="AuditActor.System"/> as a defensive default so any unattributed public audit entry is
/// booked to the system, never mis-attributed to an admin.
/// </summary>
public sealed class SystemAuditActorAccessor : IAuditActorAccessor
{
    public Task<AuditActor> GetCurrentAsync() => Task.FromResult(AuditActor.System);
}
