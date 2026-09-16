using AkeroGarden.Web.Core.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AkeroGarden.Web.Admin.Services;

/// <summary>
/// Re-checks an open admin circuit against the database every
/// <see cref="AdminSessionOptions.RevalidationSeconds"/> (FR-74).
///
/// <para>Without this, a Blazor Interactive Server circuit resolves who the user is when the tab is
/// opened and never asks again: deactivating an admin — or demoting them (FR-104) — left every tab
/// they already had open fully working, indefinitely. The auth cookie had the same gap in slower
/// motion, and <c>SecurityStampValidatorOptions.ValidationInterval</c> in Program.cs closes that
/// half. Both compare the same security stamp, which <c>AdminInviteService</c> now rotates on
/// deactivate and on role change.</para>
///
/// <para>Deciding the session is invalid is only half the job: the framework's response is to mark
/// the circuit anonymous, nothing more. <c>SessionWatch</c> in the admin layout is what then acts
/// on it.</para>
///
/// <para>The account is also re-read, so an account deleted outright (no stamp to compare) or
/// deactivated by a route that forgets to rotate the stamp is still thrown out. That
/// belt-and-braces check is deliberate: the stamp is the fast path,
/// <see cref="ApplicationUser.IsActive"/> is the truth.</para>
/// </summary>
public sealed class AdminRevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    IServiceScopeFactory scopeFactory,
    IOptions<AdminSessionOptions> options,
    IOptions<IdentityOptions> identityOptions)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval { get; } =
        TimeSpan.FromSeconds(Math.Max(1, options.Value.RevalidationSeconds));

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        // Its own scope: this runs on a timer outside any request, so the circuit's scoped
        // DbContext/UserManager are not ours to reuse.
        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = await userManager.GetUserAsync(authenticationState.User);
        if (user is null || !user.IsActive)
            return false;

        if (!userManager.SupportsUserSecurityStamp)
            return true;

        var principalStamp = authenticationState.User
            .FindFirst(identityOptions.Value.ClaimsIdentity.SecurityStampClaimType)?.Value;
        var userStamp = await userManager.GetSecurityStampAsync(user);
        return principalStamp == userStamp;
    }
}
