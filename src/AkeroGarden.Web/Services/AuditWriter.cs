using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;

namespace AkeroGarden.Web.Services;

/// <summary>
/// Default <see cref="IAuditWriter"/>: builds the <see cref="AuditLog"/> row and adds it to the
/// shared <see cref="ApplicationDbContext"/> without saving (see the interface remarks). Uses the
/// registered <see cref="TimeProvider"/> for the timestamp so tests can control it.
/// </summary>
public sealed class AuditWriter(
    ApplicationDbContext db,
    IAuditActorAccessor actorAccessor,
    TimeProvider clock) : IAuditWriter
{
    public async Task RecordAsync(
        AuditAction action,
        string entityType,
        Guid entityId,
        string summary,
        Guid? bookingId = null,
        string? bookingReference = null,
        AuditActor? actor = null)
    {
        var resolved = actor ?? await actorAccessor.GetCurrentAsync();

        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = clock.GetUtcNow().UtcDateTime,
            ActorType = resolved.Type,
            ActorUserId = resolved.UserId,
            ActorEmailSnapshot = resolved.Email,
            ActorNameSnapshot = resolved.Name,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            BookingId = bookingId,
            BookingReferenceSnapshot = bookingReference,
            Summary = summary,
        });
    }
}
