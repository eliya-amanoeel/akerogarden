using AkeroGarden.Web.Core.Data;
using Microsoft.AspNetCore.Identity;

namespace AkeroGarden.Web.Core.Services;

public class AdminSeeder(
    UserManager<ApplicationUser> userManager,
    IConfiguration configuration,
    ILogger<AdminSeeder> logger)
{
    public async Task SeedAsync()
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

        if (result.Succeeded)
            logger.LogInformation("Första adminanvändaren skapad: {Email}", email);
        else
            logger.LogError("Kunde inte skapa adminanvändare: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
    }
}
