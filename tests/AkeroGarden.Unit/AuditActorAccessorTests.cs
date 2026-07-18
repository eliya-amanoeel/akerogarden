using System.Security.Claims;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Moq;

namespace AkeroGarden.Unit;

/// <summary>
/// Actor resolution is what makes the audit trail attributable (§6.9, §8.15) — every entry must name
/// who acted. The two fallbacks matter as much as the happy path: when no admin can be resolved the
/// accessor books the action to <see cref="AuditActor.System"/>, so an unattributable change is never
/// mis-attributed to an admin who did not make it.
/// </summary>
public class AuditActorAccessorTests
{
    private static readonly Guid UserId = Guid.Parse("8f1d4d3c-6b2a-4f5e-9c7d-1a2b3c4d5e6f");

    private static AuditActorAccessor CreateAccessor(ClaimsPrincipal principal, ApplicationUser? resolvedUser)
    {
        var authState = new Mock<AuthenticationStateProvider>();
        authState.Setup(a => a.GetAuthenticationStateAsync())
            .ReturnsAsync(new AuthenticationState(principal));

        var store = new Mock<IUserStore<ApplicationUser>>();
        var mgr = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        mgr.Setup(m => m.GetUserAsync(principal)).ReturnsAsync(resolvedUser);

        return new AuditActorAccessor(authState.Object, mgr.Object);
    }

    private static ClaimsPrincipal Authenticated() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "TestAuth"));

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    [Fact]
    public async Task GetCurrentAsync_NoAuthenticatedUser_IsSystem()
    {
        var accessor = CreateAccessor(Anonymous(), resolvedUser: null);

        var actor = await accessor.GetCurrentAsync();

        Assert.Equal(AuditActorType.System, actor.Type);
        Assert.Null(actor.UserId);
        Assert.Null(actor.Email);
    }

    [Fact]
    public async Task GetCurrentAsync_AuthenticatedButUserNoLongerExists_IsSystem()
    {
        // The account was deleted or the principal is stale — resolve to System rather than guess.
        var accessor = CreateAccessor(Authenticated(), resolvedUser: null);

        var actor = await accessor.GetCurrentAsync();

        Assert.Equal(AuditActorType.System, actor.Type);
        Assert.Null(actor.UserId);
    }

    [Fact]
    public async Task GetCurrentAsync_AuthenticatedAdmin_CarriesIdEmailAndName()
    {
        var user = new ApplicationUser
        {
            Id = UserId.ToString(),
            Email = "anna@akerogarden.se",
            FirstName = "Anna",
            LastName = "Andersson",
        };
        var accessor = CreateAccessor(Authenticated(), user);

        var actor = await accessor.GetCurrentAsync();

        Assert.Equal(AuditActorType.Admin, actor.Type);
        Assert.Equal(UserId, actor.UserId);
        Assert.Equal("anna@akerogarden.se", actor.Email);
        Assert.Equal("Anna Andersson", actor.Name);
    }

    [Fact]
    public async Task GetCurrentAsync_AdminWithoutProfileName_FallsBackToEmailForAttribution()
    {
        // An invited admin who has not completed setup has no first/last name; the trail must still
        // carry something readable rather than a blank ActorNameSnapshot.
        var user = new ApplicationUser
        {
            Id = UserId.ToString(),
            Email = "ny.admin@akerogarden.se",
        };
        var accessor = CreateAccessor(Authenticated(), user);

        var actor = await accessor.GetCurrentAsync();

        Assert.Equal(AuditActorType.Admin, actor.Type);
        Assert.Equal("ny.admin@akerogarden.se", actor.Name);
    }
}

/// <summary>
/// The public host has no Identity and no Blazor auth state, so its accessor must never produce an
/// admin actor — the only audited public path (completion via magic link) passes
/// <see cref="AuditActor.Guest"/> explicitly.
/// </summary>
public class SystemAuditActorAccessorTests
{
    [Fact]
    public async Task GetCurrentAsync_IsAlwaysSystem()
    {
        var actor = await new SystemAuditActorAccessor().GetCurrentAsync();

        Assert.Equal(AuditActorType.System, actor.Type);
        Assert.Null(actor.UserId);
        Assert.Null(actor.Email);
        Assert.Null(actor.Name);
    }
}
