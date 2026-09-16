using System.Net;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AkeroGarden.Web.Core.Services;

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
    /// Invites a new admin in the given role (FR-104), or — if the email already belongs to a
    /// still-pending invite — resends it, applying <paramref name="role"/> in case the inviter
    /// picked a different one this time. Blocked only when the email belongs to an
    /// already-activated account.
    /// </summary>
    public async Task<InviteResult> InviteAsync(string email, string role)
    {
        if (!AdminRoles.All.Contains(role))
            return InviteResult.CreateFailed($"Okänd roll: {role}.");

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (existing.SetupCompletedAt is not null)
                return InviteResult.BlockedActivated;

            // A pending invite has never been used, so re-picking the role is a correction of the
            // original invite rather than a role change — no separate AdminRoleChanged entry.
            await ApplyRoleAsync(existing, role);
            await RecordAccountAuditAsync(existing, AuditAction.AdminInviteResent,
                $"Inbjudan skickades på nytt till: {email} (roll: {role}).");
            // Commit BEFORE sending. See the note on SendInviteEmailAsync — an unsaved audit row must
            // never be left pending across a slow SMTP call.
            await db.SaveChangesAsync();
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

        await ApplyRoleAsync(newUser, role);
        await RecordAccountAuditAsync(newUser, AuditAction.AdminInvited,
            $"Konto bjöds in: {email} (roll: {role}).");
        // Commit BEFORE sending — see the note on SendInviteEmailAsync.
        await db.SaveChangesAsync();
        await SendInviteEmailAsync(newUser);
        return InviteResult.Sent;
    }

    /// <summary>The account's role (FR-104), or null for a role-less account — which the seeder's
    /// backfill should have eliminated, so a null here means something created a user behind
    /// <see cref="AdminSeeder"/>'s back.</summary>
    public async Task<string?> GetRoleAsync(ApplicationUser user) =>
        (await userManager.GetRolesAsync(user)).FirstOrDefault();

    /// <summary>
    /// Changes an existing account's role (FR-104). Audited under its own verb (FR-96) because a
    /// role change silently widens or narrows what that person can see — the one account action
    /// with no visible trace on the account itself. No-ops when the role is unchanged, so a
    /// re-save never writes a misleading audit row.
    /// </summary>
    public async Task<bool> SetRoleAsync(ApplicationUser user, string role)
    {
        if (!AdminRoles.All.Contains(role))
            return false;

        var current = await GetRoleAsync(user);
        if (current == role)
            return false;

        await ApplyRoleAsync(user, role);
        // Cut the old role out of every session the account already holds. Role membership is baked
        // into the auth cookie and into an open circuit's principal, so without this a demoted
        // Administratör keeps full access in the tab they already have open.
        await userManager.UpdateSecurityStampAsync(user);
        await RecordAccountAuditAsync(user, AuditAction.AdminRoleChanged,
            $"Rollen ändrades för {user.Email}: {current ?? "ingen roll"} → {role}.");
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>Makes <paramref name="role"/> the account's only role. Identity allows many roles
    /// per user; FR-104 defines exactly one, and two roles at once would make the restricted role
    /// meaningless (role checks are an OR).</summary>
    private async Task ApplyRoleAsync(ApplicationUser user, string role)
    {
        var current = await userManager.GetRolesAsync(user);
        var stale = current.Where(r => r != role).ToList();
        if (stale.Count > 0)
            await userManager.RemoveFromRolesAsync(user, stale);

        if (!current.Contains(role))
            await userManager.AddToRoleAsync(user, role);
    }

    /// <summary>Regenerates and resends the setup link for an already-known pending user.</summary>
    public async Task ResendInviteAsync(ApplicationUser user)
    {
        await RecordAccountAuditAsync(user, AuditAction.AdminInviteResent,
            $"Inbjudan skickades på nytt till: {user.Email}.");
        // Commit BEFORE sending — see the note on SendInviteEmailAsync.
        await db.SaveChangesAsync();
        await SendInviteEmailAsync(user);
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
        // Deactivating must END access, not just prevent the next sign-in. The login page already
        // refuses an inactive account, but a session that signed in BEFORE the deactivation kept
        // working — its cookie is valid for up to 30 days and nothing re-checked IsActive. Rotating
        // the security stamp is Identity's "every session issued before now is void" signal; it is
        // what SecurityStampValidator and the admin host's revalidating auth-state provider compare
        // against, so the account is out within the revalidation interval (1 minute) on both new
        // requests and already-open tabs. Rotated on ACTIVATION too, deliberately: same one-line
        // rule either way, and it costs an account that was just re-enabled nothing.
        await userManager.UpdateSecurityStampAsync(user);
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

    /// <summary>
    /// Sends the invite/setup email. <b>Always call this AFTER <c>SaveChangesAsync</c>, never before.</b>
    ///
    /// <para>SMTP is the slowest thing this service does, and every caller has an unsaved
    /// <c>AuditLog</c> row pending when it runs. The admin host shares ONE <c>DbContext</c> for the
    /// whole Blazor circuit, so while this call is in flight a second admin action on the same page
    /// adds its own audit row to that same change tracker — and both operations then call
    /// <c>SaveChangesAsync</c> over a tracker holding both rows. Both try to insert both, and the
    /// loser gets <c>duplicate key value violates unique constraint "PK_AuditLogs"</c>, which takes
    /// the circuit down with it. That is exactly what happened on production on 2026-09-08 at 16:00
    /// on /konton: two interactions three seconds apart, the first still inside this method.</para>
    ///
    /// <para>Committing first shrinks that window to nothing. It also fixes an ordering bug in its own
    /// right — the invite email used to go out before the commit, so a failed save left a real invite
    /// sent with no record that it had been. Sending afterwards cannot un-send an email either, but a
    /// send failure is logged and non-fatal (NFR-13), whereas a lost audit row is silent.</para>
    /// </summary>
    private async Task SendInviteEmailAsync(ApplicationUser user)
    {
        var setupUrl = await GenerateSetupLinkAsync(user);
        var isCoordinator = await GetRoleAsync(user) == AdminRoles.Coordinator;

        // A Samordnare invitee only ever sees the two lists, so telling them they may "administrate
        // the booking system" would misdescribe the account they are about to activate.
        var subject = isCoordinator
            ? "Du har fått ett konto för deltagar- och matlistor — Åkerögården"
            : "Du har bjudits in som administratör — Åkerögården";

        var intro = isCoordinator
            ? "Du har fått ett konto i Åkerögårdens bokningssystem för att hämta deltagarlistor och matlistor."
            : "Du har bjudits in att administrera Åkerögårdens bokningssystem.";

        // Descriptive link text, not the raw URL — the setup link is a data-protected token roughly
        // 400 characters long, and printing it dumps a wall of gibberish into the email. Matches how
        // every booking email already links ("Slutför din bokning här").
        await emailService.SendAsync(
            user.Email!,
            user.Email!,
            subject,
            $"<p>{intro}</p>" +
            $"<p>Klicka på länken nedan för att välja ditt lösenord och aktivera ditt konto. Länken gäller i 1 timme.</p>" +
            $"<p><a href=\"{WebUtility.HtmlEncode(setupUrl)}\">Aktivera ditt konto</a></p>");
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
