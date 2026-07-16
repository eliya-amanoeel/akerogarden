using AkeroGarden.Web.Core.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Resolves the acting admin from the Blazor circuit's authentication state (the correct source for
/// Interactive Server — there is no live <c>HttpContext</c> during circuit events). Loads the
/// <see cref="ApplicationUser"/> to capture the admin's name + email for the audit snapshot
/// (§8.15). Falls back to <see cref="AuditActor.System"/> when there is no authenticated admin, so
/// an action is never mis-attributed.
/// </summary>
public sealed class AuditActorAccessor(
    AuthenticationStateProvider authStateProvider,
    UserManager<ApplicationUser> userManager) : IAuditActorAccessor
{
    public async Task<AuditActor> GetCurrentAsync()
    {
        var state = await authStateProvider.GetAuthenticationStateAsync();
        var principal = state.User;
        if (principal.Identity?.IsAuthenticated != true)
            return AuditActor.System;

        var user = await userManager.GetUserAsync(principal);
        if (user is null)
            return AuditActor.System;

        return AuditActor.Admin(Guid.Parse(user.Id), user.Email, user.DisplayName);
    }
}
