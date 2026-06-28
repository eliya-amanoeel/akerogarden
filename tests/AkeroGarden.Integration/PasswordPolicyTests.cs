using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

public class PasswordPolicyTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public PasswordPolicyTests(DbFixture fixture) => _fixture = fixture;

    private static ApplicationUser NewUser() => new()
    {
        UserName = $"{Guid.NewGuid()}@akerogarden.se",
        Email = $"{Guid.NewGuid()}@akerogarden.se",
        EmailConfirmed = true,
        IsActive = true,
    };

    [Fact]
    public async Task CreateUser_WithPasswordUnder12Chars_Fails()
    {
        using var scope = _fixture.BuildServices().CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var result = await userManager.CreateAsync(NewUser(), "Short!1"); // 7 chars

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "PasswordTooShort");
    }

    [Fact]
    public async Task CreateUser_WithNoNonAlphanumericChar_Fails()
    {
        using var scope = _fixture.BuildServices().CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // 15 chars, no special character — should fail RequireNonAlphanumeric
        var result = await userManager.CreateAsync(NewUser(), "LongerPassword1");

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Code == "PasswordRequiresNonAlphanumeric");
    }
}
