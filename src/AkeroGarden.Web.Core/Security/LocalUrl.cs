namespace AkeroGarden.Web.Core.Security;

/// <summary>
/// Whether a caller-supplied return URL may be redirected to (post-launch security review, finding
/// #21).
///
/// <para>The auth cookie appends <c>?ReturnUrl=…</c> when it bounces an unauthenticated request to the
/// login page, but the login page ignored it and always sent the user to their role's landing page. No
/// open redirect — that is the safe failure — but an admin following a deep link from a notification
/// email had to navigate back to wherever they were going, which partly defeats the deliberate
/// <c>SameSite=Lax</c> auth-cookie decision made for exactly that journey.</para>
///
/// <para><strong>Honouring the parameter is the part that needs care.</strong> A redirect target taken
/// from the query string is the classic open-redirect vector: an attacker mails an admin a link to
/// <c>/login?ReturnUrl=https://evil.example/</c>, the victim signs in for real, and the site itself
/// hands them onward to a convincing fake. This is deliberately a strict allow-list of shapes rather
/// than a blocklist of bad ones — anything not recognisably a path on this site is refused, and the
/// caller falls back to the role landing page.</para>
/// </summary>
public static class LocalUrl
{
    /// <summary>
    /// True only for a relative path rooted at this site: it must start with a single <c>/</c>, and
    /// must not begin <c>//</c> or <c>/\</c> (both of which browsers read as protocol-relative
    /// absolute URLs pointing at another host), nor contain a scheme.
    /// </summary>
    public static bool IsSafe(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        // Must be site-rooted. This also rejects "https://evil.example" and "evil.example/path".
        if (url[0] != '/')
            return false;

        // "//evil.example" and "/\evil.example" are protocol-relative — same host-changing effect as
        // an absolute URL, and the single most commonly missed case in this check.
        if (url.Length > 1 && (url[1] == '/' || url[1] == '\\'))
            return false;

        // A control character can be used to smuggle a break past naive parsers; a well-formed local
        // path never contains one.
        foreach (var c in url)
        {
            if (char.IsControl(c))
                return false;
        }

        return true;
    }
}
