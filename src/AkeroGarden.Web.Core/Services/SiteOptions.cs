namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Where this environment's two sites live — bound from the "Site" configuration section, set per
/// environment (production, staging, local). Every absolute link the system builds is made here:
/// the guest's completion link, the booking-terms link and the email signature's links, the admin
/// panel link in notifications, the password-setup link, the booking-form link the admin hands out,
/// and the canonical URL, Open Graph tags, robots.txt and sitemap of the public site.
///
/// <para>One section, read by everything, so a link cannot point at a different environment from
/// the page that made it. Åkerögården's own contact details are not per environment and are not
/// configuration — see <see cref="ContactDetails"/>.</para>
/// </summary>
public class SiteOptions
{
    /// <summary>The public site's base URL, e.g. <c>https://akerogarden.se</c>.</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>The admin panel's base URL, e.g. <c>https://admin.akerogarden.se</c>.</summary>
    public string AdminBaseUrl { get; set; } = string.Empty;

    /// <summary>An absolute URL on the public site — <c>PublicUrl("/boka")</c>.</summary>
    public string PublicUrl(string path) => Join(PublicBaseUrl, path);

    /// <summary>An absolute URL on the admin panel — <c>AdminUrl("/bokningar/…")</c>.</summary>
    public string AdminUrl(string path) => Join(AdminBaseUrl, path);

    private static string Join(string baseUrl, string path) =>
        baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
}
