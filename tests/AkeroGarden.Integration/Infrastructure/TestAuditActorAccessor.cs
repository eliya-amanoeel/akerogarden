using AkeroGarden.Web.Services;

namespace AkeroGarden.Integration.Infrastructure;

/// <summary>
/// Test double for <see cref="IAuditActorAccessor"/> — there is no Blazor circuit in integration
/// tests to resolve a real admin. Defaults to <see cref="AuditActor.System"/>; audit-specific tests
/// register an instance with a known admin actor to assert attribution.
/// </summary>
public sealed class TestAuditActorAccessor(AuditActor actor) : IAuditActorAccessor
{
    public TestAuditActorAccessor() : this(AuditActor.System) { }

    public Task<AuditActor> GetCurrentAsync() => Task.FromResult(actor);
}
