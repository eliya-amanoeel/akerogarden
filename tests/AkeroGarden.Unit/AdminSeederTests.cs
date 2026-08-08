using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AkeroGarden.Unit;

/// <summary>
/// Startup seeding. Since FR-104 this does three things, not one: guarantee both roles exist,
/// give every role-less account <c>Administratör</c>, and create the first admin on an empty
/// database. The backfill is the load-bearing one — before FR-104 no role was created or assigned
/// anywhere, so without it the deploy that introduces role checks locks every existing admin out.
/// </summary>
public class AdminSeederTests
{
    private static Mock<UserManager<ApplicationUser>> CreateUserManagerMock(
        IEnumerable<ApplicationUser> existingUsers,
        IList<string>? rolesOfExistingUsers = null)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var mgr = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        mgr.Setup(m => m.Users).Returns(existingUsers.AsQueryable());
        mgr.Setup(m => m.GetRolesAsync(It.IsAny<ApplicationUser>()))
           .ReturnsAsync(rolesOfExistingUsers ?? []);
        mgr.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
           .ReturnsAsync(IdentityResult.Success);
        return mgr;
    }

    /// <param name="existingRoles">Roles already present in AspNetRoles.</param>
    private static Mock<RoleManager<IdentityRole>> CreateRoleManagerMock(params string[] existingRoles)
    {
        var store = new Mock<IRoleStore<IdentityRole>>();
        var mgr = new Mock<RoleManager<IdentityRole>>(
            store.Object, null!, null!, null!, null!);
        mgr.Setup(m => m.RoleExistsAsync(It.IsAny<string>()))
           .ReturnsAsync((string r) => existingRoles.Contains(r));
        mgr.Setup(m => m.CreateAsync(It.IsAny<IdentityRole>()))
           .ReturnsAsync(IdentityResult.Success);
        return mgr;
    }

    private static AdminSeeder Build(
        Mock<UserManager<ApplicationUser>> users,
        Mock<RoleManager<IdentityRole>> roles,
        IConfiguration? config = null) =>
        new(users.Object, roles.Object, config ?? new ConfigurationBuilder().Build(),
            NullLogger<AdminSeeder>.Instance);

    private static IConfiguration SeedConfig(string email, string password) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:AdminEmail"] = email,
                ["Seed:AdminPassword"] = password
            })
            .Build();

    // ── Roles ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedAsync_CreatesBothRoles_WhenNoneExist()
    {
        var users = CreateUserManagerMock([]);
        var roles = CreateRoleManagerMock();

        await Build(users, roles).SeedAsync();

        roles.Verify(m => m.CreateAsync(It.Is<IdentityRole>(r => r.Name == AdminRoles.Administrator)), Times.Once);
        roles.Verify(m => m.CreateAsync(It.Is<IdentityRole>(r => r.Name == AdminRoles.Coordinator)), Times.Once);
    }

    [Fact]
    public async Task SeedAsync_CreatesOnlyTheMissingRole()
    {
        var users = CreateUserManagerMock([]);
        var roles = CreateRoleManagerMock(AdminRoles.Administrator);

        await Build(users, roles).SeedAsync();

        // Re-creating an existing role would fail on the unique name index every boot.
        roles.Verify(m => m.CreateAsync(It.Is<IdentityRole>(r => r.Name == AdminRoles.Administrator)), Times.Never);
        roles.Verify(m => m.CreateAsync(It.Is<IdentityRole>(r => r.Name == AdminRoles.Coordinator)), Times.Once);
    }

    // ── Backfill (audit H2) ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedAsync_GivesAdministratorRole_ToExistingUserWithNoRole()
    {
        var existing = new ApplicationUser { Email = "existing@test.se" };
        var users = CreateUserManagerMock([existing], rolesOfExistingUsers: []);
        var roles = CreateRoleManagerMock(AdminRoles.All);

        await Build(users, roles).SeedAsync();

        users.Verify(m => m.AddToRoleAsync(existing, AdminRoles.Administrator), Times.Once);
    }

    [Fact]
    public async Task SeedAsync_LeavesAnExistingRoleAlone()
    {
        // A deliberately-restricted coordinator must not be silently promoted to full access on
        // the next app restart — the backfill only ever fills in a *missing* role.
        var coordinator = new ApplicationUser { Email = "kok@test.se" };
        var users = CreateUserManagerMock([coordinator], rolesOfExistingUsers: [AdminRoles.Coordinator]);
        var roles = CreateRoleManagerMock(AdminRoles.All);

        await Build(users, roles).SeedAsync();

        users.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    // ── First admin ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedAsync_DoesNotCreateUser_WhenUsersExist()
    {
        var users = CreateUserManagerMock([new ApplicationUser { Email = "existing@test.se" }]);
        var roles = CreateRoleManagerMock(AdminRoles.All);

        await Build(users, roles, SeedConfig("admin@akerogarden.se", "TestLösenord!23")).SeedAsync();

        users.Verify(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SeedAsync_DoesNotCreateUser_WhenConfigMissing()
    {
        var users = CreateUserManagerMock([]);
        var roles = CreateRoleManagerMock(AdminRoles.All);

        await Build(users, roles).SeedAsync();

        users.Verify(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SeedAsync_CreatesFirstAdmin_AndMakesItAnAdministrator()
    {
        var users = CreateUserManagerMock([]);
        users.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
             .ReturnsAsync(IdentityResult.Success);
        var roles = CreateRoleManagerMock();

        await Build(users, roles, SeedConfig("admin@akerogarden.se", "TestLösenord!23")).SeedAsync();

        users.Verify(m => m.CreateAsync(
            It.Is<ApplicationUser>(u => u.Email == "admin@akerogarden.se"),
            "TestLösenord!23"),
            Times.Once);

        // Nobody else exists to grant it afterwards, so the first admin must land with the role.
        users.Verify(m => m.AddToRoleAsync(
            It.Is<ApplicationUser>(u => u.Email == "admin@akerogarden.se"),
            AdminRoles.Administrator),
            Times.Once);
    }

    [Fact]
    public async Task SeedAsync_DoesNotAssignRole_WhenFirstAdminCreationFails()
    {
        var users = CreateUserManagerMock([]);
        users.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
             .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "för svagt lösenord" }));
        var roles = CreateRoleManagerMock();

        await Build(users, roles, SeedConfig("admin@akerogarden.se", "svagt")).SeedAsync();

        users.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }
}
