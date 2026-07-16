using Microsoft.AspNetCore.Identity;

namespace AkeroGarden.Web.Core.Data;

public class ApplicationUser : IdentityUser
{
    public bool IsActive { get; set; } = true;
    public DateTime? SetupCompletedAt { get; set; }

    // Admin profile (§6.4). Collected by the invitee themselves when they complete setup
    // (ResetPassword page). PhoneNumber is the built-in IdentityUser property. Used to attribute
    // audit-trail actions by name rather than email (§8.15 ActorNameSnapshot).
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>Full name for display and audit attribution; falls back to the email when unset.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(FirstName) && string.IsNullOrWhiteSpace(LastName)
            ? Email ?? UserName ?? string.Empty
            : $"{FirstName} {LastName}".Trim();
}
