using AkeroGarden.Web.Data.Entities;

namespace AkeroGarden.Web.Services;

/// <summary>
/// Records audit-trail entries (§6.9, FR-95/96). The entry is <b>added to the current
/// <see cref="Data.ApplicationDbContext"/> but not saved</b> — it persists atomically with the
/// caller's own <c>SaveChangesAsync</c>, so a rolled-back mutation leaves no orphan audit row and a
/// committed one is always accompanied by its trail entry.
/// </summary>
public interface IAuditWriter
{
    /// <summary>
    /// Queues an audit entry for the current unit of work. The acting admin is resolved from the
    /// ambient auth context unless <paramref name="actor"/> is supplied — pass
    /// <see cref="AuditActor.Guest"/> for a completion submission and <see cref="AuditActor.System"/>
    /// for a background job. <paramref name="summary"/> must contain no guest personal data (NFR-37).
    /// </summary>
    Task RecordAsync(
        AuditAction action,
        string entityType,
        Guid entityId,
        string summary,
        Guid? bookingId = null,
        string? bookingReference = null,
        AuditActor? actor = null);
}
