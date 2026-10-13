using System.Security.Claims;

namespace AkeroGarden.Web.Core.Data;

/// <summary>
/// The two admin roles (FR-104). The names are stored verbatim in <c>AspNetRoles</c> and appear in
/// the admin UI, so they are Swedish (NFR-32) — never rename one without a data migration, because
/// role membership is matched by name, not by id.
///
/// <para><b>Administratör</b> — full access, everything described in the SRS.
/// <b>Samordnare</b> — the external coordinator / kitchen manager: reads and downloads the two
/// per-booking lists (FR-44/45) and nothing else. No booking detail page, no guest contact details,
/// no admin notes, no reference data, no accounts, no dashboard.</para>
///
/// <para>Authorisation is enforced <i>per page</i> — every routable admin component carries an
/// explicit <c>[Authorize(Roles = …)]</c>. Do not gate by hiding fragments inside a shared page:
/// <c>BookingDetail</c> alone exposes contact details, notes and every state-machine action, so one
/// missed fragment check leaks guest data. A new page with no role attribute is a bug.</para>
/// </summary>
public static class AdminRoles
{
    public const string Administrator = "Administratör";
    public const string Coordinator = "Samordnare";

    /// <summary>Both roles, for the pages either may open (the list views and the coordinator's handbook page, FR-117) and for role seeding.</summary>
    public const string Both = $"{Administrator},{Coordinator}";

    /// <summary>Every role the system defines — the set the seeder guarantees exists.</summary>
    public static readonly string[] All = [Administrator, Coordinator];

    /// <summary>
    /// Where this user's "home" is. The admin host's <c>/</c> is the dashboard, which
    /// <see cref="Coordinator"/> must not see, so that role's home is its own list view instead.
    ///
    /// <para>One definition on purpose. Three separate things send a user home — the redirect after
    /// login, the logo in the top-left of every page, and the way out of the access-denied page — and
    /// if any one of them keeps pointing at <c>/</c>, a coordinator walks into an authorisation wall
    /// doing something completely ordinary. Route them all through here.</para>
    /// </summary>
    public static string LandingPath(bool isAdministrator, bool isCoordinator) =>
        !isAdministrator && isCoordinator ? "/listor" : "/";

    /// <summary>
    /// <see cref="LandingPath"/> for an authenticated principal. The login page cannot use this
    /// overload — at that moment the cookie exists but the request's principal is still anonymous —
    /// so it asks the user store and calls <see cref="LandingPath"/> directly. Same rule, one copy.
    /// </summary>
    public static string LandingPathFor(ClaimsPrincipal user) =>
        LandingPath(user.IsInRole(Administrator), user.IsInRole(Coordinator));
}
