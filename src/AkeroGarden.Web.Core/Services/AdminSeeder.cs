using AkeroGarden.Web.Core.Data;
using Microsoft.AspNetCore.Identity;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Startup seeding for the admin host: guarantees both roles (FR-104) exist, gives every
/// role-less account <see cref="AdminRoles.Administrator"/>, and creates the very first admin
/// when the user table is empty. Idempotent — it runs on every boot.
/// </summary>
public class AdminSeeder(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IConfiguration configuration,
    ILogger<AdminSeeder> logger)
{
    public async Task SeedAsync()
    {
        await EnsureRolesExistAsync();
        await BackfillAdministratorRoleAsync();
        await CreateFirstAdminAsync();
    }

    /// <summary>Creates any missing role. Roles are matched by name, so this is safe to re-run.</summary>
    private async Task EnsureRolesExistAsync()
    {
        foreach (var role in AdminRoles.All)
        {
            if (await roleManager.RoleExistsAsync(role))
                continue;

            var result = await roleManager.CreateAsync(new IdentityRole(role));
            if (result.Succeeded)
                logger.LogInformation("Rollen {Role} skapades.", role);
            else
                logger.LogError("Kunde inte skapa rollen {Role}: {Errors}", role,
                    string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }

    /// <summary>
    /// Gives <see cref="AdminRoles.Administrator"/> to every account that has no role at all.
    /// Before FR-104 no role was created or assigned anywhere, so every existing account is
    /// role-less — and the admin pages now demand a role. Without this backfill the first deploy
    /// carrying FR-104 would lock every existing admin out of their own panel. Only ever *adds* a
    /// role, so a deliberate <see cref="AdminRoles.Coordinator"/> assignment is never overwritten.
    /// </summary>
    private async Task BackfillAdministratorRoleAsync()
    {
        foreach (var user in userManager.Users.ToList())
        {
            if ((await userManager.GetRolesAsync(user)).Count > 0)
                continue;

            var result = await userManager.AddToRoleAsync(user, AdminRoles.Administrator);
            if (result.Succeeded)
                logger.LogInformation("Rollen {Role} tilldelades det befintliga kontot {Email}.",
                    AdminRoles.Administrator, user.Email);
            else
                logger.LogError("Kunde inte tilldela rollen {Role} till {Email}: {Errors}",
                    AdminRoles.Administrator, user.Email,
                    string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }

    private async Task CreateFirstAdminAsync()
    {
        if (userManager.Users.Any())
            return;

        var email = configuration["Seed:AdminEmail"];
        var password = configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Inga adminanvändare finns och Seed:AdminEmail/Seed:AdminPassword är inte konfigurerade. Skapa en användare manuellt.");
            return;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsActive = true,
        };

        var result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            logger.LogError("Kunde inte skapa adminanvändare: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
            return;
        }

        // The first admin must be a full Administratör — nobody else exists to grant it afterwards.
        await userManager.AddToRoleAsync(user, AdminRoles.Administrator);
        logger.LogInformation("Första adminanvändaren skapad: {Email}", email);
    }
}
