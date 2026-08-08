using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AkeroGarden.Integration;

/// <summary>
/// Startup seeding against a real Identity store.
///
/// Every test here takes its own database via <see cref="DbFixture.BuildIsolatedServicesAsync"/>.
/// That is not tidiness: <c>AdminSeeder</c> creates the first admin only when the user table is
/// empty, so tests that share one database would pass or fail depending on which xUnit ran first.
/// </summary>
public class AdminSeederIntegrationTests(DbFixture fixture) : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture = fixture;

    private static IConfiguration SeedConfig(string email) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:AdminEmail"] = email,
                ["Seed:AdminPassword"] = "Testlösenord!99"
            })
            .Build();

    private async Task<(IServiceScope Scope, UserManager<ApplicationUser> Users, RoleManager<IdentityRole> Roles, AdminSeeder Seeder)>
        ArrangeAsync(IConfiguration config)
    {
        var services = await _fixture.BuildIsolatedServicesAsync(s => s.AddSingleton(config));
        var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var seeder = new AdminSeeder(users, roles, config, NullLogger<AdminSeeder>.Instance);
        return (scope, users, roles, seeder);
    }

    [Fact]
    public async Task SeedAsync_CreatesAdminUser_WhenDatabaseIsEmpty()
    {
        var email = $"seed-{Guid.NewGuid()}@akerogarden.se";
        var (scope, users, _, seeder) = await ArrangeAsync(SeedConfig(email));
        using var _scope = scope;

        Assert.Null(await users.FindByEmailAsync(email));

        await seeder.SeedAsync();

        var created = await users.FindByEmailAsync(email);
        Assert.NotNull(created);
        Assert.True(created.IsActive);
        Assert.True(created.EmailConfirmed);
    }

    /// <summary>
    /// Both roles land in AspNetRoles and the first admin holds Administratör. Against a real store,
    /// so this also proves the Swedish role names survive Identity's normalisation — they are matched
    /// by name, and "Administratör" normalises to "ADMINISTRATÖR".
    /// </summary>
    [Fact]
    public async Task SeedAsync_CreatesBothRoles_AndGivesTheFirstAdminTheAdministratorRole()
    {
        var email = $"seed-{Guid.NewGuid()}@akerogarden.se";
        var (scope, users, roles, seeder) = await ArrangeAsync(SeedConfig(email));
        using var _scope = scope;

        await seeder.SeedAsync();

        Assert.True(await roles.RoleExistsAsync(AdminRoles.Administrator));
        Assert.True(await roles.RoleExistsAsync(AdminRoles.Coordinator));

        var created = await users.FindByEmailAsync(email);
        Assert.NotNull(created);
        Assert.Equal([AdminRoles.Administrator], await users.GetRolesAsync(created));
    }

    /// <summary>Re-running the seeder on an already-seeded database must be a no-op, because it runs
    /// on every boot of the admin host — a second role-create would fail the unique name index.</summary>
    [Fact]
    public async Task SeedAsync_IsIdempotent_WhenRunTwice()
    {
        var email = $"seed-{Guid.NewGuid()}@akerogarden.se";
        var (scope, users, roles, seeder) = await ArrangeAsync(SeedConfig(email));
        using var _scope = scope;

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        Assert.Single(users.Users.ToList());
        Assert.Equal(AdminRoles.All.Length, roles.Roles.Count());

        var created = await users.FindByEmailAsync(email);
        Assert.Equal([AdminRoles.Administrator], await users.GetRolesAsync(created!));
    }

    /// <summary>
    /// Audit H2 — the finding this slice exists for. Accounts created before FR-104 hold no role at
    /// all, and every admin page now demands one, so the deploy that introduces role checks would
    /// lock the client out of their own panel without this backfill. Also pins that the same pass
    /// never promotes a deliberately-restricted coordinator.
    /// </summary>
    [Fact]
    public async Task SeedAsync_BackfillsAdministrator_OntoRolelessAccounts_ButNotOntoACoordinator()
    {
        var (scope, users, roles, seeder) = await ArrangeAsync(new ConfigurationBuilder().Build());
        using var _scope = scope;

        // A pre-FR-104 account: created directly, no role assigned anywhere.
        var legacy = await CreateUserAsync(users, "legacy");
        Assert.Empty(await users.GetRolesAsync(legacy));

        // A deliberately restricted account.
        var coordinator = await CreateUserAsync(users, "kok");
        Assert.True((await roles.CreateAsync(new IdentityRole(AdminRoles.Coordinator))).Succeeded);
        Assert.True((await users.AddToRoleAsync(coordinator, AdminRoles.Coordinator)).Succeeded);

        await seeder.SeedAsync();

        Assert.Equal([AdminRoles.Administrator], await users.GetRolesAsync(legacy));
        Assert.Equal([AdminRoles.Coordinator], await users.GetRolesAsync(coordinator));
    }

    private static async Task<ApplicationUser> CreateUserAsync(UserManager<ApplicationUser> users, string prefix)
    {
        var address = $"{prefix}-{Guid.NewGuid()}@akerogarden.se";
        var user = new ApplicationUser
        {
            UserName = address,
            Email = address,
            EmailConfirmed = true,
            IsActive = true,
        };
        Assert.True((await users.CreateAsync(user, "Testlösenord!99")).Succeeded);
        return user;
    }
}
