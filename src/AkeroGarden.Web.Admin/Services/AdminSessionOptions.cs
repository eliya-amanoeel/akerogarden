namespace AkeroGarden.Web.Admin.Services;

/// <summary>
/// How often a signed-in admin session is re-checked against the database (FR-74).
/// </summary>
public class AdminSessionOptions
{
    /// <summary>
    /// Seconds between revalidations of an open Blazor circuit, and of the auth cookie
    /// (<c>SecurityStampValidatorOptions.ValidationInterval</c>). One minute: the window in which a
    /// deactivated or demoted admin still has access, traded against one indexed user lookup per
    /// signed-in admin per interval — negligible for the 2–3 accounts this system is built for.
    /// Configurable only so the E2E suite can shorten it; there is no reason to change it in an
    /// environment that serves people.
    /// </summary>
    public int RevalidationSeconds { get; set; } = 60;
}
