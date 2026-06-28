using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AkeroGarden.Integration;

public class AdminSeederIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public AdminSeederIntegrationTests(DbFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SeedAsync_CreatesAdminUser_WhenDatabaseIsEmpty()
    {
        var email = $"seed-{Guid.NewGuid()}@akerogarden.se";

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:AdminEmail"] = email,
                ["Seed:AdminPassword"] = "Testlösenord!99"
            })
            .Build();

        var services = _fixture.BuildServices(s => s.AddSingleton<IConfiguration>(config));
        using var scope = services.CreateScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Confirm the user does not yet exist
        Assert.Null(await userManager.FindByEmailAsync(email));

        var seeder = new AdminSeeder(userManager, config, NullLogger<AdminSeeder>.Instance);
        await seeder.SeedAsync();

        var created = await userManager.FindByEmailAsync(email);
        Assert.NotNull(created);
        Assert.True(created.IsActive);
        Assert.True(created.EmailConfirmed);
    }
}
