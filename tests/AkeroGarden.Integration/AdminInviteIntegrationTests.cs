using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the admin invite lifecycle (Phase 9, FR-74): sending a fresh
/// invite, resending a still-pending one, blocking re-invite/removal once activated, hard-
/// deleting a never-completed invite, and stamping <see cref="ApplicationUser.SetupCompletedAt"/>
/// on first successful password reset.
/// </summary>
public class AdminInviteIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public AdminInviteIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private (IServiceProvider Services, AdminInviteService Invites, UserManager<ApplicationUser> Users, FakeEmailService Email)
        NewProvider()
    {
        var fake = new FakeEmailService();
        var services = _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(fake);
            s.Configure<BookingEmailOptions>(o =>
            {
                o.AdminBaseUrl = "https://admin.akerogarden.se";
                o.PublicBaseUrl = "https://akerogarden.se";
            });
            s.AddDataProtection();
            s.AddScoped<AdminInviteService>();
        });
        var scope = services.CreateScope();
        var invites = scope.ServiceProvider.GetRequiredService<AdminInviteService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (services, invites, users, fake);
    }

    [Fact]
    public async Task InviteAsync_CreatesPendingUser_AndSendsEmail()
    {
        var (_, invites, users, email) = NewProvider();
        var address = $"{Guid.NewGuid()}@akerogarden.se";

        var result = await invites.InviteAsync(address);

        Assert.Equal(InviteOutcome.Sent, result.Outcome);
        var user = await users.FindByEmailAsync(address);
        Assert.NotNull(user);
        Assert.Null(user!.SetupCompletedAt);
        Assert.Single(email.Sent);
        Assert.Equal(address, email.Sent.First().ToAddress);
    }

    [Fact]
    public async Task InviteAsync_OnPendingEmail_ResendsInsteadOfBlocking()
    {
        var (_, invites, _, email) = NewProvider();
        var address = $"{Guid.NewGuid()}@akerogarden.se";
        await invites.InviteAsync(address);

        var result = await invites.InviteAsync(address);

        Assert.Equal(InviteOutcome.Sent, result.Outcome);
        Assert.Equal(2, email.Sent.Count);
    }

    [Fact]
    public async Task InviteAsync_OnActivatedEmail_IsBlocked()
    {
        var (_, invites, users, _) = NewProvider();
        var address = $"{Guid.NewGuid()}@akerogarden.se";
        await invites.InviteAsync(address);
        var user = await users.FindByEmailAsync(address);
        await invites.StampSetupCompletedAsync(user!);

        var result = await invites.InviteAsync(address);

        Assert.Equal(InviteOutcome.BlockedActivated, result.Outcome);
    }

    [Fact]
    public async Task RemoveInviteAsync_DeletesPendingUser()
    {
        var (_, invites, users, _) = NewProvider();
        var address = $"{Guid.NewGuid()}@akerogarden.se";
        await invites.InviteAsync(address);
        var user = await users.FindByEmailAsync(address);

        var result = await invites.RemoveInviteAsync(user!.Id);

        Assert.Equal(HardDeleteResult.Deleted, result);
        Assert.Null(await users.FindByEmailAsync(address));
    }

    [Fact]
    public async Task RemoveInviteAsync_OnActivatedUser_IsBlocked()
    {
        var (_, invites, users, _) = NewProvider();
        var address = $"{Guid.NewGuid()}@akerogarden.se";
        await invites.InviteAsync(address);
        var user = await users.FindByEmailAsync(address);
        await invites.StampSetupCompletedAsync(user!);

        var result = await invites.RemoveInviteAsync(user!.Id);

        Assert.Equal(HardDeleteResult.Blocked, result);
        Assert.NotNull(await users.FindByEmailAsync(address));
    }

    [Fact]
    public async Task StampSetupCompletedAsync_SetsTimestamp_Once()
    {
        var (_, invites, users, _) = NewProvider();
        var address = $"{Guid.NewGuid()}@akerogarden.se";
        await invites.InviteAsync(address);
        var user = await users.FindByEmailAsync(address);

        await invites.StampSetupCompletedAsync(user!);
        var firstStamp = (await users.FindByEmailAsync(address))!.SetupCompletedAt;

        Assert.NotNull(firstStamp);

        await invites.StampSetupCompletedAsync(user!);
        var secondStamp = (await users.FindByEmailAsync(address))!.SetupCompletedAt;

        Assert.Equal(firstStamp, secondStamp);
    }
}
