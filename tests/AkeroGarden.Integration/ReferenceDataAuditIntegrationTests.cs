using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the reference-data + admin-account audit trail (Phase 11, §6.9, FR-96)
/// against real PostgreSQL. Asserts that each CRUD/lifecycle action writes one correctly-typed
/// <see cref="AuditLog"/> row, attributed to the acting admin (or the invitee themselves for
/// self-activation), and that these entries are <b>not</b> booking-scoped (BookingId null) so the
/// retention job age-purges them (FR-98).
/// </summary>
public class ReferenceDataAuditIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public ReferenceDataAuditIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private static readonly Guid AdminId = Guid.NewGuid();
    private const string AdminEmail = "admin@akerogarden.se";

    private IServiceProvider NewProvider()
    {
        var fake = new FakeEmailService();
        return _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(fake);
            s.AddScoped<PriceRuleService>();
            s.AddScoped<FacilityService>();
            s.AddDataProtection();
            s.AddScoped<AdminInviteService>();
            s.AddScoped<IAuditActorAccessor>(_ =>
                new TestAuditActorAccessor(AuditActor.Admin(AdminId, AdminEmail, "Test Admin")));
            s.Configure<BookingEmailOptions>(o =>
            {
                o.AdminBaseUrl = "https://admin.akerogarden.se";
                o.PublicBaseUrl = "https://akerogarden.se";
            });
        });
    }

    private static async Task<List<AuditLog>> RowsForAsync(IServiceProvider provider, Guid entityId)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AuditLogs.Where(a => a.EntityId == entityId).OrderBy(a => a.Timestamp).ToListAsync();
    }

    [Fact]
    public async Task PriceRule_lifecycle_writes_one_typed_row_per_action_attributed_to_admin()
    {
        var provider = NewProvider();
        Guid id;

        using (var scope = provider.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<PriceRuleService>();
            // CreateAsync assigns its own Id — capture it, don't assume ours survives.
            var pr = new PriceRule { Label = "Helpension vuxen", AmountExVat = 550m, VatRate = 0.12m, Unit = "per person och natt", IsActive = true };
            await svc.CreateAsync(pr);
            id = pr.Id;
            await svc.UpdateAsync(new PriceRule { Id = id, Label = "Helpension vuxen", AmountExVat = 575m, VatRate = 0.12m, Unit = "per person och natt" });
            await svc.SetActiveAsync(id, false);
            await svc.SetActiveAsync(id, true);
            await svc.DeleteAsync(id);
        }

        var rows = await RowsForAsync(provider, id);
        Assert.Equal(
            new[]
            {
                AuditAction.ReferenceDataCreated,
                AuditAction.ReferenceDataUpdated,
                AuditAction.ReferenceDataDeactivated,
                AuditAction.ReferenceDataActivated,
                AuditAction.ReferenceDataDeleted,
            },
            rows.Select(r => r.Action));

        Assert.All(rows, r =>
        {
            Assert.Equal(nameof(PriceRule), r.EntityType);
            Assert.Equal(AuditActorType.Admin, r.ActorType);
            Assert.Equal(AdminEmail, r.ActorEmailSnapshot);
            Assert.Null(r.BookingId);                 // not booking-scoped → age-purged (FR-98)
            Assert.Contains("Helpension vuxen", r.Summary);
        });
    }

    [Fact]
    public async Task Facility_hard_delete_writes_a_deleted_row()
    {
        var provider = NewProvider();
        Guid id;

        using (var scope = provider.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<FacilityService>();
            var facility = new Facility { Name = "Kapellet", IsActive = true };
            await svc.CreateAsync(facility); // CreateAsync assigns its own Id
            id = facility.Id;
            var result = await svc.TryHardDeleteAsync(id); // unreferenced → deletes
            Assert.Equal(HardDeleteResult.Deleted, result);
        }

        var rows = await RowsForAsync(provider, id);
        Assert.Equal(AuditAction.ReferenceDataCreated, rows[0].Action);
        Assert.Equal(AuditAction.ReferenceDataDeleted, rows[^1].Action);
        Assert.All(rows, r => Assert.Equal(nameof(Facility), r.EntityType));
    }

    [Fact]
    public async Task Admin_account_lifecycle_is_audited_with_correct_actors()
    {
        var provider = NewProvider();
        var email = $"{Guid.NewGuid():N}@akerogarden.se";

        Guid userId;
        using (var scope = provider.CreateScope())
        {
            var invites = scope.ServiceProvider.GetRequiredService<AdminInviteService>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            await invites.InviteAsync(email);
            var user = (await users.FindByEmailAsync(email))!;
            userId = Guid.Parse(user.Id);

            // Invitee completes their own activation → attributed to themselves, not the inviting admin.
            await invites.CompleteActivationAsync(user, "Bengt", "Bengtsson", "070-000 00 00");
            await invites.SetAccountActiveAsync(user, false);
            await invites.SetAccountActiveAsync(user, true);
        }

        var rows = await RowsForAsync(provider, userId);
        Assert.Equal(
            new[]
            {
                AuditAction.AdminInvited,
                AuditAction.AdminAccountSetupCompleted,
                AuditAction.AdminAccountDeactivated,
                AuditAction.AdminAccountActivated,
            },
            rows.Select(r => r.Action));

        Assert.All(rows, r => Assert.Equal("AdminAccount", r.EntityType));

        // Invite + activate/deactivate are admin actions; setup-completion is the invitee themselves.
        var invite = rows.Single(r => r.Action == AuditAction.AdminInvited);
        Assert.Equal(AdminEmail, invite.ActorEmailSnapshot);

        var activation = rows.Single(r => r.Action == AuditAction.AdminAccountSetupCompleted);
        Assert.Equal(userId, activation.ActorUserId);
        Assert.Equal(email, activation.ActorEmailSnapshot);
    }

    [Fact]
    public async Task Removing_a_pending_invite_is_audited()
    {
        var provider = NewProvider();
        var email = $"{Guid.NewGuid():N}@akerogarden.se";

        Guid userId;
        using (var scope = provider.CreateScope())
        {
            var invites = scope.ServiceProvider.GetRequiredService<AdminInviteService>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await invites.InviteAsync(email);
            userId = Guid.Parse((await users.FindByEmailAsync(email))!.Id);

            var result = await invites.RemoveInviteAsync(userId.ToString());
            Assert.Equal(HardDeleteResult.Deleted, result);
        }

        var rows = await RowsForAsync(provider, userId);
        Assert.Contains(rows, r => r.Action == AuditAction.AdminInvited);
        Assert.Contains(rows, r => r.Action == AuditAction.AdminInviteRemoved); // survives the user deletion
    }
}
