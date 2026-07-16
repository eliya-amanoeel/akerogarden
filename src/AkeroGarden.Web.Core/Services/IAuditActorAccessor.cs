namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Resolves the current acting admin for audit attribution (§6.9). Only invoked for admin-initiated
/// actions; Guest (completion) and System (background job) actors are supplied explicitly by the
/// caller, never resolved here.
/// </summary>
public interface IAuditActorAccessor
{
    /// <summary>The current authenticated admin as an <see cref="AuditActor"/>, or
    /// <see cref="AuditActor.System"/> when no admin is in context.</summary>
    Task<AuditActor> GetCurrentAsync();
}
