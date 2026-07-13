using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AkeroGarden.Web.Services;

/// <summary>
/// Admin invite lifecycle (Phase 9, FR-74): sending, resending, and removing pending
/// invitations. Business rules live here, not in the Razor component (NFR-26/27).
/// A user is "pending" while <see cref="ApplicationUser.SetupCompletedAt"/> is null;
/// setup links expire after 1 hour (DataProtectionTokenProviderOptions.TokenLifespan).
/// Lifecycle actions are audited (FR-96) — admin accounts are staff data, so the account
/// email may be recorded (NFR-37 governs guest data only).
/// </summary>
public class AdminInviteService(
    UserManager<ApplicationUser> userManager,
    IEmailService emailService,
    IOptions<BookingEmailOptions> emailOptions,
    IDataProtectionProvider dataProtectionProvider,
    IAuditWriter audit,
    ApplicationDbContext db)
{
    private const string AccountEntity = "AdminAccount";

    /// <summary>Queues an admin-account audit entry (§8.15) — actor defaults to the ambient admin.
    /// EntityId is the Identity user id; not booking-scoped, so age-purged by the retention job.</summary>
    private Task RecordAccountAuditAsync(ApplicationUser user, AuditAction action, string summary, AuditActor? actor = null)
        => audit.RecordAsync(action, AccountEntity, Guid.Parse(user.Id), summary, actor: actor);

    /// <summary>
    /// Invites a new admin, or — if the email already belongs to a still-pending
    /// invite — resends it. Blocked only when the email belongs to an already-activated account.
    /// </summary>
    public async Task<InviteResult> InviteAsync(string email)
    {
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (existing.SetupCompletedAt is not null)
                return InviteResult.BlockedActivated;

            await SendInviteEmailAsync(existing);
            await RecordAccountAuditAsync(existing, AuditAction.AdminInviteResent,
                $"Inbjudan skickades på nytt till: {email}.");
            await db.SaveChangesAsync();
            return InviteResult.Sent;
        }

        var newUser = new ApplicationUser
        {
            UserName = email,
            Email = email,
            IsActive = true
        };

        var tempPassword = Guid.NewGuid().ToString("N")[..16] + "Aa1!";
        var createResult = await userManager.CreateAsync(newUser, tempPassword);
        if (!createResult.Succeeded)
            return InviteResult.CreateFailed(string.Join(" ", createResult.Errors.Select(e => e.Description)));

        await SendInviteEmailAsync(newUser);
        await RecordAccountAuditAsync(newUser, AuditAction.AdminInvited,
            $"Administratör bjöds in: {email}.");
        await db.SaveChangesAsync();
        return InviteResult.Sent;
    }

    /// <summary>Regenerates and resends the setup link for an already-known pending user.</summary>
    public async Task ResendInviteAsync(ApplicationUser user)
    {
        await SendInviteEmailAsync(user);
        await RecordAccountAuditAsync(user, AuditAction.AdminInviteResent,
            $"Inbjudan skickades på nytt till: {user.Email}.");
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Hard-deletes a never-completed invite (safe pre-activation — Identity users are
    /// infrastructure, not app data). Blocked if the account has since been activated.
    /// </summary>
    public async Task<HardDeleteResult> RemoveInviteAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return HardDeleteResult.NotFound;
        if (user.SetupCompletedAt is not null) return HardDeleteResult.Blocked;

        var email = user.Email;
        await userManager.DeleteAsync(user);
        // Audit references the (now-deleted) user id — EntityId is a plain Guid, not an FK.
        await RecordAccountAuditAsync(user, AuditAction.AdminInviteRemoved,
            $"Väntande inbjudan togs bort: {email}.");
        await db.SaveChangesAsync();
        return HardDeleteResult.Deleted;
    }

    /// <summary>Stamps first-time activation (idempotent) after a successful password reset.</summary>
    public async Task StampSetupCompletedAsync(ApplicationUser user)
    {
        if (user.SetupCompletedAt is not null) return;

        user.SetupCompletedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);
    }

    /// <summary>
    /// Completes a first-time invite activation (FR-74, §6.4): stores the invitee's required
    /// first name, last name and phone, and stamps setup as completed — in a single update. The
    /// caller (setup page) has already reset the password with the one-time token.
    /// </summary>
    public async Task<IdentityResult> CompleteActivationAsync(
        ApplicationUser user, string firstName, string lastName, string phone)
    {
        user.FirstName = firstName.Trim();
        user.LastName = lastName.Trim();
        user.PhoneNumber = phone.Trim();
        user.SetupCompletedAt ??= DateTime.UtcNow;
        // Actor is the invitee activating their own account (no admin session yet) — attribute it to
        // them. The audit row is added to the shared context and saved by UpdateAsync below.
        await RecordAccountAuditAsync(user, AuditAction.AdminAccountSetupCompleted,
            $"Kontot aktiverades av användaren: {user.Email}.",
            AuditActor.Admin(Guid.Parse(user.Id), user.Email, user.DisplayName));
        return await userManager.UpdateAsync(user);
    }

    /// <summary>
    /// Activates or deactivates an existing admin account (FR-74). Audited (FR-96); the audit row
    /// is saved together with the account update.
    /// </summary>
    public async Task SetAccountActiveAsync(ApplicationUser user, bool isActive)
    {
        user.IsActive = isActive;
        await RecordAccountAuditAsync(user,
            isActive ? AuditAction.AdminAccountActivated : AuditAction.AdminAccountDeactivated,
            $"Administratörskonto {(isActive ? "aktiverades" : "inaktiverades")}: {user.Email}.");
        await userManager.UpdateAsync(user);
    }

    /// <summary>
    /// Builds the one-time setup/activation link for a user (the URL the invite email carries):
    /// a fresh password-reset token, wrapped with the email in a data-protected <c>?code=</c>. Public
    /// so a Development-only E2E test seam can drive the activation flow the browser otherwise can't
    /// reach (the link is normally only delivered by email).
    /// </summary>
    public async Task<string> GenerateSetupLinkAsync(ApplicationUser user)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var protector = dataProtectionProvider.CreateProtector("PasswordReset");
        var code = Uri.EscapeDataString(protector.Protect($"{user.Email}|{token}"));
        return $"{emailOptions.Value.AdminBaseUrl.TrimEnd('/')}/aterstall-losenord?code={code}";
    }

    private async Task SendInviteEmailAsync(ApplicationUser user)
    {
        var setupUrl = await GenerateSetupLinkAsync(user);

        await emailService.SendAsync(
            user.Email!,
            user.Email!,
            "Du har bjudits in som administratör — Åkerögården",
            $"<p>Du har bjudits in att administrera Åkerögårdens bokningssystem.</p>" +
            $"<p>Klicka på länken nedan för att välja ditt lösenord och aktivera ditt konto. Länken gäller i 1 timme.</p>" +
            $"<p><a href=\"{setupUrl}\">{setupUrl}</a></p>");
    }
}

public readonly struct InviteResult
{
    public static readonly InviteResult Sent = new(InviteOutcome.Sent, null);
    public static readonly InviteResult BlockedActivated = new(InviteOutcome.BlockedActivated, null);
    public static InviteResult CreateFailed(string error) => new(InviteOutcome.CreateFailed, error);

    private InviteResult(InviteOutcome outcome, string? error)
    {
        Outcome = outcome;
        Error = error;
    }

    public InviteOutcome Outcome { get; }
    public string? Error { get; }
}

public enum InviteOutcome
{
    Sent,
    BlockedActivated,
    CreateFailed
}
