using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

public class AuthFlowIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public AuthFlowIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private async Task<(UserManager<ApplicationUser> UserManager, ApplicationUser User)>
        CreateUserAsync(string password = "Testlösenord!99")
    {
        var services = _fixture.BuildServices();
        var scope = services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = $"{Guid.NewGuid()}@akerogarden.se",
            Email = $"{Guid.NewGuid()}@akerogarden.se",
            EmailConfirmed = true,
            IsActive = true,
        };
        var result = await userManager.CreateAsync(user, password);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Description)));

        return (userManager, user);
    }

    [Fact]
    public async Task CheckPassword_ReturnsTrue_WithCorrectCredentials()
    {
        var (userManager, user) = await CreateUserAsync();

        var valid = await userManager.CheckPasswordAsync(user, "Testlösenord!99");

        Assert.True(valid);
    }

    [Fact]
    public async Task CheckPassword_ReturnsFalse_WithWrongPassword()
    {
        var (userManager, user) = await CreateUserAsync();

        var valid = await userManager.CheckPasswordAsync(user, "FelagtLösenord!00");

        Assert.False(valid);
    }

    [Fact]
    public async Task AccessFailed_LocksOut_AfterFiveAttempts()
    {
        var (userManager, user) = await CreateUserAsync();

        for (var i = 0; i < 5; i++)
            await userManager.AccessFailedAsync(user);

        var lockedOut = await userManager.IsLockedOutAsync(user);
        Assert.True(lockedOut);
    }

    [Fact]
    public async Task PasswordReset_TokenCanBeGenerated_AndConsumed()
    {
        const string newPassword = "NyttLösenord!77";
        var (userManager, user) = await CreateUserAsync();

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        Assert.NotEmpty(token);

        var result = await userManager.ResetPasswordAsync(user, token, newPassword);
        Assert.True(result.Succeeded);

        var valid = await userManager.CheckPasswordAsync(user, newPassword);
        Assert.True(valid);
    }
}
