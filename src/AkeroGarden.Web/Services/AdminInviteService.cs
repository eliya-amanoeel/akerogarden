using AkeroGarden.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AkeroGarden.Web.Services;

/// <summary>
/// Admin invite lifecycle (Phase 9, FR-74): sending, resending, and removing pending
/// invitations. Business rules live here, not in the Razor component (NFR-26/27).
/// A user is "pending" while <see cref="ApplicationUser.SetupCompletedAt"/> is null;
/// setup links expire after 1 hour (DataProtectionTokenProviderOptions.TokenLifespan).
/// </summary>
public class AdminInviteService(
    UserManager<ApplicationUser> userManager,
    IEmailService emailService,
    IOptions<BookingEmailOptions> emailOptions,
    IDataProtectionProvider dataProtectionProvider)
{
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
        return InviteResult.Sent;
    }

    /// <summary>Regenerates and resends the setup link for an already-known pending user.</summary>
    public async Task ResendInviteAsync(ApplicationUser user) => await SendInviteEmailAsync(user);

    /// <summary>
    /// Hard-deletes a never-completed invite (safe pre-activation — Identity users are
    /// infrastructure, not app data). Blocked if the account has since been activated.
    /// </summary>
    public async Task<HardDeleteResult> RemoveInviteAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null) return HardDeleteResult.NotFound;
        if (user.SetupCompletedAt is not null) return HardDeleteResult.Blocked;

        await userManager.DeleteAsync(user);
        return HardDeleteResult.Deleted;
    }

    /// <summary>Stamps first-time activation (idempotent) after a successful password reset.</summary>
    public async Task StampSetupCompletedAsync(ApplicationUser user)
    {
        if (user.SetupCompletedAt is not null) return;

        user.SetupCompletedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);
    }

    private async Task SendInviteEmailAsync(ApplicationUser user)
    {
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var protector = dataProtectionProvider.CreateProtector("PasswordReset");
        var code = Uri.EscapeDataString(protector.Protect($"{user.Email}|{token}"));
        var setupUrl = $"{emailOptions.Value.AdminBaseUrl.TrimEnd('/')}/aterstall-losenord?code={code}";

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
