using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the admin invite lifecycle (Phase 9, FR-74): sending a fresh
/// invite, resending a still-pending one, blocking re-invite/removal once activated, hard-
/// deleting a never-completed invite, and stamping <see cref="ApplicationUser.SetupCompletedAt"/>
/// on first successful password reset — plus the role the invite now carries (FR-104).
/// </summary>
public class AdminInviteIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public AdminInviteIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private async Task<(AdminInviteService Invites, UserManager<ApplicationUser> Users, ApplicationDbContext Db, FakeEmailService Email)>
        NewProviderAsync()
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
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await DbFixture.EnsureAdminRolesAsync(scope.ServiceProvider);

        return (invites, users, db, fake);
    }

    [Fact]
    public async Task InviteAsync_CreatesPendingUser_AndSendsEmail()
    {
        var (invites, users, _, email) = await NewProviderAsync();
        var address = $"{Guid.NewGuid()}@akerogarden.se";

        var result = await invites.InviteAsync(address, AdminRoles.Administrator);

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
        var (invites, _, _, email) = await NewProviderAsync();
        var address = $"{Guid.NewGuid()}@akerogarden.se";
        await invites.InviteAsync(address, AdminRoles.Administrator);

        var result = await invites.InviteAsync(address, AdminRoles.Administrator);

        Assert.Equal(InviteOutcome.Sent, result.Outcome);
        Assert.Equal(2, email.Sent.Count);
    }

    [Fact]
    public async Task InviteAsync_OnActivatedEmail_IsBlocked()
    {
        var (invites, users, _, _) = await NewProviderAsync();
        var address = $"{Guid.NewGuid()}@akerogarden.se";
        await invites.InviteAsync(address, AdminRoles.Administrator);
        var user = await users.FindByEmailAsync(address);
        await invites.StampSetupCompletedAsync(user!);

        var result = await invites.InviteAsync(address, AdminRoles.Administrator);

        Assert.Equal(InviteOutcome.BlockedActivated, result.Outcome);
    }

    [Fact]
    public async Task RemoveInviteAsync_DeletesPendingUser()
    {
        var (invites, users, _, _) = await NewProviderAsync();
        var address = $"{Guid.NewGuid()}@akerogarden.se";
        await invites.InviteAsync(address, AdminRoles.Administrator);
        var user = await users.FindByEmailAsync(address);

        var result = await invites.RemoveInviteAsync(user!.Id);

        Assert.Equal(HardDeleteResult.Deleted, result);
        Assert.Null(await users.FindByEmailAsync(address));
    }

    [Fact]
    public async Task RemoveInviteAsync_OnActivatedUser_IsBlocked()
    {
        var (invites, users, _, _) = await NewProviderAsync();
        var address = $"{Guid.NewGuid()}@akerogarden.se";
        await invites.InviteAsync(address, AdminRoles.Administrator);
        var user = await users.FindByEmailAsync(address);
        await invites.StampSetupCompletedAsync(user!);

        var result = await invites.RemoveInviteAsync(user!.Id);

        Assert.Equal(HardDeleteResult.Blocked, result);
        Assert.NotNull(await users.FindByEmailAsync(address));
    }

    [Fact]
    public async Task CompleteActivationAsync_StoresProfileFields_AndStampsSetup()
    {
        var (invites, users, _, _) = await NewProviderAsync();
        var address = $"{Guid.NewGuid()}@akerogarden.se";
        await invites.InviteAsync(address, AdminRoles.Administrator);
        var user = await users.FindByEmailAsync(address);
        Assert.Null(user!.SetupCompletedAt);

        var result = await invites.CompleteActivationAsync(user, "  Anna ", " Andersson ", " 070-123 45 67 ");

        Assert.True(result.Succeeded);
        var reloaded = (await users.FindByEmailAsync(address))!;
        Assert.Equal("Anna", reloaded.FirstName);            // trimmed (FR-74)
        Assert.Equal("Andersson", reloaded.LastName);
        Assert.Equal("070-123 45 67", reloaded.PhoneNumber);
        Assert.NotNull(reloaded.SetupCompletedAt);           // activation stamped in the same update
        Assert.Equal("Anna Andersson", reloaded.DisplayName);
    }

    [Fact]
    public async Task StampSetupCompletedAsync_SetsTimestamp_Once()
    {
        var (invites, users, _, _) = await NewProviderAsync();
        var address = $"{Guid.NewGuid()}@akerogarden.se";
        await invites.InviteAsync(address, AdminRoles.Administrator);
        var user = await users.FindByEmailAsync(address);

        await invites.StampSetupCompletedAsync(user!);
        var firstStamp = (await users.FindByEmailAsync(address))!.SetupCompletedAt;

        Assert.NotNull(firstStamp);

        await invites.StampSetupCompletedAsync(user!);
        var secondStamp = (await users.FindByEmailAsync(address))!.SetupCompletedAt;

        Assert.Equal(firstStamp, secondStamp);
    }

    // ── Roles (FR-104) ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AdminRoles.Administrator)]
    [InlineData(AdminRoles.Coordinator)]
    public async Task InviteAsync_AssignsTheChosenRole(string role)
    {
        var (invites, users, _, _) = await NewProviderAsync();
        var address = $"{Guid.NewGuid()}@akerogarden.se";

        await invites.InviteAsync(address, role);

        var user = await users.FindByEmailAsync(address);
        Assert.Equal([role], await users.GetRolesAsync(user!));
    }

    [Fact]
    public async Task InviteAsync_RejectsAnUnknownRole_AndCreatesNoAccount()
    {
        var (invites, users, _, email) = await NewProviderAsync();
        var address = $"{Guid.NewGuid()}@akerogarden.se";

        var result = await invites.InviteAsync(address, "Superadmin");

        Assert.Equal(InviteOutcome.CreateFailed, result.Outcome);
        Assert.Null(await users.FindByEmailAsync(address));
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task InviteAsync_OnPendingEmail_AppliesTheNewlyChosenRole()
    {
        // Re-inviting a pending account is a correction of the original invite: the link has never
        // been used, so the role the inviter picks the second time is the one that must stick.
        var (invites, users, _, _) = await NewProviderAsync();
        var address = $"{Guid.NewGuid()}@akerogarden.se";
        await invites.InviteAsync(address, AdminRoles.Administrator);

        await invites.InviteAsync(address, AdminRoles.Coordinator);

        var user = await users.FindByEmailAsync(address);
        Assert.Equal([AdminRoles.Coordinator], await users.GetRolesAsync(user!));
    }

    [Fact]
    public async Task InviteAsync_ToACoordinator_SendsAnEmailThatDoesNotPromiseAdminAccess()
    {
        var (invites, _, _, email) = await NewProviderAsync();
        var address = $"{Guid.NewGuid()}@akerogarden.se";

        await invites.InviteAsync(address, AdminRoles.Coordinator);

        var sent = Assert.Single(email.Sent);
        Assert.DoesNotContain("administrera", sent.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deltagarlistor", sent.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InviteAsync_LinksWithDescriptiveText_NotTheRawUrl()
    {
        // The setup link is a data-protected token ~400 characters long. Printed as its own link
        // text it fills the email with gibberish, so the anchor carries words and the URL stays in
        // the href. Note this cannot catch a *wrong* base URL — the address comes from config, and
        // the test supplies its own; the local-dev AdminBaseUrl pointing at the public app's port
        // was found by hand, not here.
        var (invites, _, _, email) = await NewProviderAsync();
        var address = $"{Guid.NewGuid()}@akerogarden.se";

        await invites.InviteAsync(address, AdminRoles.Administrator);

        var sent = Assert.Single(email.Sent);
        Assert.Contains(">Aktivera ditt konto</a>", sent.HtmlBody);
        Assert.Contains("href=\"https://admin.akerogarden.se/aterstall-losenord?code=", sent.HtmlBody);
        Assert.DoesNotContain(">https://admin.akerogarden.se", sent.HtmlBody);
    }

    [Fact]
    public async Task SetRoleAsync_ReplacesTheRole_AndNeverLeavesTwo()
    {
        var (invites, users, _, _) = await NewProviderAsync();
        var address = $"{Guid.NewGuid()}@akerogarden.se";
        await invites.InviteAsync(address, AdminRoles.Administrator);
        var user = (await users.FindByEmailAsync(address))!;

        Assert.True(await invites.SetRoleAsync(user, AdminRoles.Coordinator));

        // Holding both would make the restriction meaningless — a role check is an OR.
        Assert.Equal([AdminRoles.Coordinator], await users.GetRolesAsync(user));
    }

    [Fact]
    public async Task SetRoleAsync_IsAuditedAsARoleChange()
    {
        var (invites, users, db, _) = await NewProviderAsync();
        var address = $"{Guid.NewGuid()}@akerogarden.se";
        await invites.InviteAsync(address, AdminRoles.Administrator);
        var user = (await users.FindByEmailAsync(address))!;

        await invites.SetRoleAsync(user, AdminRoles.Coordinator);

        var entry = await db.AuditLogs
            .Where(a => a.Action == AuditAction.AdminRoleChanged && a.EntityId == Guid.Parse(user.Id))
            .SingleAsync();
        Assert.Contains(AdminRoles.Coordinator, entry.Summary);
    }

    [Fact]
    public async Task SetRoleAsync_WritesNoAuditRow_WhenTheRoleIsUnchanged()
    {
        // Re-picking the role already selected must not leave a trail suggesting something changed.
        var (invites, users, db, _) = await NewProviderAsync();
        var address = $"{Guid.NewGuid()}@akerogarden.se";
        await invites.InviteAsync(address, AdminRoles.Administrator);
        var user = (await users.FindByEmailAsync(address))!;

        Assert.False(await invites.SetRoleAsync(user, AdminRoles.Administrator));

        Assert.Empty(await db.AuditLogs
            .Where(a => a.Action == AuditAction.AdminRoleChanged && a.EntityId == Guid.Parse(user.Id))
            .ToListAsync());
    }

    [Fact]
    public async Task SetRoleAsync_RejectsAnUnknownRole_AndLeavesTheCurrentOneIntact()
    {
        var (invites, users, _, _) = await NewProviderAsync();
        var address = $"{Guid.NewGuid()}@akerogarden.se";
        await invites.InviteAsync(address, AdminRoles.Coordinator);
        var user = (await users.FindByEmailAsync(address))!;

        Assert.False(await invites.SetRoleAsync(user, "Superadmin"));

        Assert.Equal([AdminRoles.Coordinator], await users.GetRolesAsync(user));
    }
}
