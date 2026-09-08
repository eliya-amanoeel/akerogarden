using System.Diagnostics;
using System.Text.RegularExpressions;
using OpenTelemetry;

namespace AkeroGarden.Web.Core.Security;

/// <summary>
/// Removes credential-bearing segments from URLs before they are exported as telemetry
/// (post-launch security review, finding #1).
///
/// <para><strong>Why this exists.</strong> Application Insights records the full request URL on every
/// request. Two of this system's URLs carry a credential in the URL itself, so both were being written
/// verbatim into Log Analytics and kept for the workspace's retention period:</para>
/// <list type="bullet">
///   <item><c>/slutfor/{token}</c> — the booking completion magic link. The token is a bearer
///   credential: per <see cref="Services.CompletionAccessPolicy"/>, holding it grants read access to a
///   booking and the right to submit the completion form, which means guest names and allergy data.
///   Verified in production on 2026-09-08: three distinct 43-character tokens across six requests.</item>
///   <item><c>?code={payload}</c> — the admin password-reset and invite link. The payload is a
///   DataProtection-protected <c>{email}|{token}</c> blob. Fourteen occurrences in production.</item>
/// </list>
///
/// <para><strong>Why redact rather than stop collecting URLs.</strong> The route is worth keeping —
/// "someone opened a completion link and got a 200" is exactly the diagnostic this system needs, and
/// dropping the URL entirely would lose it. Only the credential segment is replaced, so the shape of
/// the request survives and the secret does not.</para>
///
/// <para><strong>Why the placeholder is a bare word.</strong> <c>redacted</c> rather than something
/// like <c>[redacted]</c>: the exporter parses these values as URLs, and brackets are not valid in a
/// path segment unescaped. A placeholder that keeps the string a well-formed URL cannot cause the
/// field to be dropped or mangled downstream.</para>
///
/// <para>NFR-05 forbids personal data in URLs and is satisfied — neither URL contains any. What is at
/// stake here is the credential <em>to</em> that data, which is the same exposure by a different
/// route.</para>
/// </summary>
public static class TelemetryUrlRedactor
{
    /// <summary>The value substituted for any credential found in a URL.</summary>
    public const string Placeholder = "redacted";

    /// <summary>
    /// The completion-link path segment. Matches <c>/slutfor/</c> followed by the token, stopping at
    /// the next path separator, query or fragment so anything after the token is preserved.
    /// </summary>
    private static readonly Regex CompletionToken = new(
        @"(?<=/slutfor/)[^/?#]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The password-reset / invite <c>code</c> query parameter, whether it opens the query string or
    /// follows another parameter. Other parameters are left intact.
    /// </summary>
    private static readonly Regex CodeParameter = new(
        @"(?<=(^|[?&])code=)[^&#]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Returns <paramref name="value"/> with any credential segment replaced by
    /// <see cref="Placeholder"/>. Returns the input unchanged when it carries no credential, so the
    /// common case allocates nothing new.
    /// </summary>
    /// <param name="value">A full URL, a path, or a query string — the same rules apply to each.</param>
    public static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var result = value;

        if (result.Contains("/slutfor/", StringComparison.OrdinalIgnoreCase))
            result = CompletionToken.Replace(result, Placeholder);

        if (result.Contains("code=", StringComparison.OrdinalIgnoreCase))
            result = CodeParameter.Replace(result, Placeholder);

        return result;
    }
}

/// <summary>
/// Applies <see cref="TelemetryUrlRedactor"/> to every URL-bearing tag on a finished span, so no
/// credential reaches the exporter (post-launch security review, finding #1).
///
/// <para><strong>Registration order is load-bearing.</strong> OpenTelemetry runs processors in the
/// order they are added, and an exporter is itself a processor. This one must be registered
/// <em>before</em> <c>UseAzureMonitor</c> — added afterwards it would run after the batch export
/// processor has already taken the activity, and the redaction would either be too late or race the
/// exporter's background thread. Both hosts register it first; see their <c>Program.cs</c>.</para>
///
/// <para>Every URL tag the ASP.NET Core instrumentation might set is covered rather than only the one
/// in use today. The stable HTTP semantic conventions split the URL across <c>url.path</c> and
/// <c>url.query</c>; older versions emitted <c>http.url</c> and <c>http.target</c>. Covering all of
/// them means a convention change in a future instrumentation package cannot silently reopen this.</para>
/// </summary>
public sealed class UrlRedactingProcessor : BaseProcessor<Activity>
{
    private static readonly string[] UrlTags =
        ["url.full", "url.path", "url.query", "http.url", "http.target"];

    /// <inheritdoc />
    public override void OnEnd(Activity data)
    {
        foreach (var tag in UrlTags)
        {
            if (data.GetTagItem(tag) is not string value || value.Length == 0)
                continue;

            var redacted = TelemetryUrlRedactor.Redact(value);
            if (!string.Equals(redacted, value, StringComparison.Ordinal))
                data.SetTag(tag, redacted);
        }
    }
}
