using System.Security.Claims;
using AkeroGarden.Web.Core.Data;

namespace AkeroGarden.Unit;

/// <summary>
/// Where each role goes when it is sent "home" (FR-104). Three separate places send a user home —
/// the redirect after login, the logo on every page, and the way out of the access-denied page — so
/// this rule having exactly one implementation is the point, and this is that implementation's test.
/// </summary>
public class AdminRolesTests
{
    private static ClaimsPrincipal UserIn(params string[] roles) =>
        new(new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r)),
            "Test", ClaimTypes.Name, ClaimTypes.Role));

    [Fact]
    public void Administrator_lands_on_the_dashboard() =>
        Assert.Equal("/", AdminRoles.LandingPathFor(UserIn(AdminRoles.Administrator)));

    [Fact]
    public void Coordinator_lands_on_its_own_list_view_not_the_dashboard()
    {
        // "/" is the dashboard, which this role may not open — landing there would put a brand-new
        // coordinator on an access-denied page the moment they finish logging in.
        Assert.Equal("/listor", AdminRoles.LandingPathFor(UserIn(AdminRoles.Coordinator)));
    }

    [Fact]
    public void An_account_holding_both_roles_lands_on_the_dashboard()
    {
        // Should not occur (SetRoleAsync replaces rather than adds), but a role check is an OR, so
        // such an account can open the dashboard — and must therefore be sent somewhere it can use.
        Assert.Equal("/", AdminRoles.LandingPathFor(UserIn(AdminRoles.Administrator, AdminRoles.Coordinator)));
    }

    [Fact]
    public void A_role_less_account_falls_back_to_the_dashboard() =>
        Assert.Equal("/", AdminRoles.LandingPathFor(UserIn()));

    [Fact]
    public void Both_names_are_covered_by_the_combined_role_string()
    {
        // AdminRoles.Both is what the shared pages put in [Authorize(Roles = …)]; if a role were
        // missing from it, that page would silently stop being reachable for one of them.
        var names = AdminRoles.Both.Split(',');
        Assert.Equal(AdminRoles.All.Order(), names.Order());
    }
}
