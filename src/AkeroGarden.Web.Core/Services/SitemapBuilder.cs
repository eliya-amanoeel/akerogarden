using System.Text;
using System.Xml.Linq;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Builds <c>/sitemap.xml</c> and <c>/robots.txt</c> for the public host (Phase 11).
///
/// <para>The page list is not written down anywhere here — it is read from the application's own
/// routes at request time (see the public host's <c>Program.cs</c>). A hand-maintained list is the
/// obvious alternative and the wrong one: it goes stale silently, and a page missing from a sitemap
/// produces no error anywhere, so nothing would ever tell us.</para>
///
/// <para><strong>Indexing is decided by environment, and fails closed.</strong> Only Production
/// invites crawlers; everything else answers <c>Disallow: /</c>. This is not a detail —
/// <c>staging.akerogarden.se</c> is publicly reachable and serves the same pages, so without it the
/// staging site competes with the real one as duplicate content. Keying on the environment means no
/// new configuration and no Bicep change: <c>ASPNETCORE_ENVIRONMENT</c> is already set to
/// <c>Staging</c> / <c>Production</c> by the templates.</para>
/// </summary>
public static class SitemapBuilder
{
    private static readonly XNamespace Sitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";

    /// <summary>
    /// Routes that exist but must never be advertised: the token-gated completion form (a per-guest
    /// URL — advertising the *pattern* is pointless and indexing a real one would expose a booking),
    /// the two framework error pages, and the inquiry thank-you pages <c>/boka/tack</c> and
    /// <c>/kontakt/tack</c> (NFR-41) — all outcomes rather than destinations. The completion
    /// thank-you, <c>/slutfor/tack</c>, is covered by <c>/slutfor</c>.
    /// </summary>
    public static readonly string[] NeverIndexed =
        ["/slutfor", "/boka/tack", "/kontakt/tack", "/error", "/not-found"];

    /// <summary>
    /// Filters raw route patterns down to the public pages worth listing: no route parameters (a
    /// pattern like <c>/slutfor/{token}</c> is not a page), nothing in <see cref="NeverIndexed"/>,
    /// and no framework or infrastructure paths. Returned lowercase, de-duplicated and ordered so
    /// the output is stable between deployments — a sitemap that reshuffles on every deploy looks
    /// to a crawler like the site changed when it did not.
    /// </summary>
    public static IReadOnlyList<string> SelectIndexablePaths(IEnumerable<string> routePatterns)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var raw in routePatterns)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            var path = "/" + raw.Trim('/');
            if (path.Contains('{') || path.Contains('}'))
                continue;

            if (path.StartsWith("/_", StringComparison.Ordinal))
                continue;

            // The App Service health probe target (post-launch security review, finding #20). It is a
            // liveness endpoint, not a page: it has no extension and no "_" prefix, so without this it
            // would be advertised to search engines like any other route.
            if (path.Equals("/health", StringComparison.Ordinal))
                continue;

            // Anything file-shaped is not a page. MapStaticAssets registers a route endpoint per
            // asset — and per *variant*, so the first version of this shipped a sitemap advertising
            // /js/calendar.js, /js/calendar.js.gz and the fingerprinted forms of both. robots.txt
            // and sitemap.xml are caught by the same rule, being routes on this host as well.
            // A page route in this app never carries an extension; if one ever does, the paired
            // integration test (which asserts the sitemap equals the known page list) fails rather
            // than the sitemap quietly growing.
            var lastSegment = path[(path.LastIndexOf('/') + 1)..];
            if (lastSegment.Contains('.'))
                continue;

            if (NeverIndexed.Any(excluded =>
                    path.Equals(excluded, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(excluded + "/", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            paths.Add(path == "//" ? "/" : path.ToLowerInvariant());
        }

        return [.. paths];
    }

    /// <summary>Renders the sitemap XML for the given paths, as absolute URLs under the base URL.</summary>
    public static string BuildSitemap(string baseUrl, IEnumerable<string> paths)
    {
        var root = baseUrl.TrimEnd('/');
        var urlset = new XElement(Sitemap + "urlset",
            paths.Select(path => new XElement(Sitemap + "url",
                new XElement(Sitemap + "loc", path == "/" ? root + "/" : root + path))));

        // No <lastmod>: the honest value would be the last content change per page, which nothing
        // here tracks. A fabricated one (build date, "today") teaches a crawler to distrust the
        // field, which is worse than omitting it.
        return new XDeclaration("1.0", "utf-8", null) + Environment.NewLine
            + new XDocument(urlset).ToString();
    }

    /// <summary>
    /// Renders robots.txt. Outside Production this is a flat refusal, deliberately — see the type
    /// summary.
    /// </summary>
    public static string BuildRobots(string baseUrl, bool allowIndexing)
    {
        var builder = new StringBuilder();
        builder.AppendLine("User-agent: *");

        if (!allowIndexing)
        {
            // Staging and every other non-production environment. The comment is for a human who
            // finds this file and wonders whether it is a mistake.
            builder.AppendLine("Disallow: /");
            builder.AppendLine();
            builder.AppendLine("# Detta är inte produktionsmiljön. Endast akerogarden.se ska indexeras.");
            return builder.ToString();
        }

        foreach (var path in NeverIndexed)
            builder.AppendLine($"Disallow: {path}");

        builder.AppendLine();
        builder.AppendLine($"Sitemap: {baseUrl.TrimEnd('/')}/sitemap.xml");
        return builder.ToString();
    }
}
