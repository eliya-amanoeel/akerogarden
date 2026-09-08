using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Web.Core.Security;

/// <summary>
/// Response security headers for both hosts (Phase 11) — a Content-Security-Policy plus the four
/// companion headers. Until this shipped the solution sent none of them: <c>UseHsts()</c> and
/// antiforgery were the only header-level protections in place.
///
/// <para><strong>Why the CSP is the load-bearing one.</strong> NFR-35 ("no resource may load from a
/// third-party host while a page renders") had no enforcement mechanism — only a convention plus a
/// single E2E test on a single page. A <c>default-src 'self'</c> makes the requirement impossible to
/// break by accident, on every page, rather than merely disapproved of. It is defence in depth
/// against the next edit, not a fix for a present leak: nothing third-party is requested today.</para>
///
/// <para><strong>The inline import map is why <c>script-src</c> carries a hash.</strong> Blazor's
/// <c>&lt;ImportMap /&gt;</c> renders an inline <c>&lt;script type="importmap"&gt;</c> — the only
/// inline script either host emits. Its content carries the static-asset fingerprints, so it changes
/// on every deploy and differs per host; a hardcoded hash would be wrong the first time anything is
/// rebuilt. The framework has no nonce support (verified against 10.0.2 — the
/// <c>Microsoft.AspNetCore.Components.*</c> assemblies contain no nonce plumbing at all), so a
/// runtime-computed hash is the only way to keep <c>'unsafe-inline'</c> out of <c>script-src</c>.
/// <see cref="ImportMapScriptHash"/> computes it; an integration test on each host hashes the
/// <em>actually rendered</em> import map and asserts the header contains that exact value, so a
/// framework serialization change fails CI instead of blanking production.</para>
///
/// <para><strong><c>style-src 'self'</c> admits no inline styling at all, deliberately.</strong>
/// The markup carried 19 inline <c>style</c> attributes when this was written; they were converted
/// to component modifiers in <c>site.css</c> in the same change, rather than exempted with
/// <c>style-src-attr 'unsafe-inline'</c>. So a style attribute appearing in markup is now a
/// <em>visible</em> defect — the browser drops it — instead of a silently permitted habit.
/// Stylesheet-object writes from script (<c>el.style.display = …</c>) are not inline styling and
/// remain allowed by CSP; the page scripts rely on that and the E2E suite proves it in a browser.
/// One case could not stay in script: the completion form's per-sitting meal cells had a
/// server-rendered <c>display:none</c> that JavaScript then cleared, so both halves moved to the
/// <c>.meal-cell--hidden</c> class together.</para>
/// </summary>
public static class AkeroSecurityHeaders
{
    /// <summary>
    /// Emits the security headers on every response.
    /// </summary>
    /// <param name="interactiveCircuit">
    /// True for the admin host. Blazor Interactive Server talks to the server over a WebSocket, which
    /// needs <c>connect-src</c>. CSP3 says <c>'self'</c> already covers a same-origin <c>wss:</c>, and
    /// current Chrome and Firefox agree — but Safari has historically not, and the failure mode is the
    /// admin panel not working at all, so the origin is named explicitly rather than relied upon.
    /// </param>
    public static IApplicationBuilder UseAkeroSecurityHeaders(
        this IApplicationBuilder app, bool interactiveCircuit)
    {
        var hash = app.ApplicationServices.GetRequiredService<ImportMapScriptHash>();
        var isDevelopment = app.ApplicationServices
            .GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().IsDevelopment();

        // The policy varies only by request host (the wss: origin), so it is built once per host and
        // reused. Bounded in practice by the hostnames bound to the app.
        var byHost = new ConcurrentDictionary<string, string>();

        return app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            var host = context.Request.Host.Value ?? string.Empty;

            headers["Content-Security-Policy"] = byHost.GetOrAdd(
                host, h => BuildPolicy(hash.Value, interactiveCircuit, h, isDevelopment));

            // Stops a browser from second-guessing a declared Content-Type — the classic route to
            // getting a user-supplied file interpreted as script.
            headers["X-Content-Type-Options"] = "nosniff";

            // frame-ancestors 'none' above is the modern equivalent and is what actually enforces
            // this; X-Frame-Options is kept for anything that only understands the older header.
            headers["X-Frame-Options"] = "DENY";

            // same-origin, not strict-origin-when-cross-origin: nothing here needs to tell another
            // site where a visitor came from, and the outbound links on /kontakt (OpenStreetMap,
            // SL, Google Maps) are exactly the requests we would rather send no referrer with.
            headers["Referrer-Policy"] = "same-origin";

            // The app asks for none of these. Denying them means an injected iframe or script cannot
            // prompt the visitor for them in our name either.
            headers["Permissions-Policy"] =
                "accelerometer=(), camera=(), display-capture=(), geolocation=(), gyroscope=(), " +
                "magnetometer=(), microphone=(), payment=(), usb=()";

            await next(context);
        });
    }

    /// <summary>
    /// The policy itself. Kept as one place so the integration tests can assert the exact string
    /// rather than a paraphrase of it.
    /// </summary>
    public static string BuildPolicy(
        string importMapHash, bool interactiveCircuit, string host, bool isDevelopment)
    {
        var script = string.IsNullOrEmpty(importMapHash) ? "'self'" : $"'self' {importMapHash}";

        var connect = new StringBuilder("'self'");
        if (interactiveCircuit && !string.IsNullOrEmpty(host))
            connect.Append(" wss://").Append(host);
        // `dotnet watch` serves its browser-refresh WebSocket from a second port, which connect-src
        // 'self' does not cover — without this, hot reload silently stops working in local dev. Only
        // ever added in Development; the integration tests assert the Production string, which has it
        // nowhere. Note this widens *only* the dev policy: everything else below is identical, so the
        // E2E suite (which runs Development) still exercises the real rules.
        if (isDevelopment)
            connect.Append(" ws://localhost:* wss://localhost:*");

        return string.Join("; ",
        [
            "default-src 'self'",
            $"script-src {script}",
            "style-src 'self'",
            // data: is needed for the one inline SVG background in site.css.
            "img-src 'self' data:",
            "font-src 'self'",
            $"connect-src {connect}",
            "form-action 'self'",
            "frame-ancestors 'none'",
            "base-uri 'self'",
            "object-src 'none'",
        ]);
    }
}

/// <summary>
/// The SHA-256 of the inline import map Blazor renders, in <c>'sha256-…'</c> form ready for
/// <c>script-src</c>. Computed once per process on first use: the import map is derived from the
/// static-asset manifest, so it is fixed for the lifetime of a deployment but differs between
/// deployments and between the two hosts.
///
/// <para>Resolved lazily rather than in the composition root because the import map is built from the
/// static-asset endpoints, which are not populated until the app is fully built.</para>
/// </summary>
public sealed class ImportMapScriptHash(IServiceProvider services)
{
    private string? _value;

    /// <summary>
    /// <c>'sha256-…'</c>, or an empty string if no import map is in play (in which case
    /// <c>script-src 'self'</c> stands alone — correct, because there is then no inline script).
    /// </summary>
    public string Value => _value ??= Compute();

    private string Compute()
    {
        var definition = services.GetService<ImportMapDefinition>()
            ?? BuildFromResourceCollection();

        if (definition is null)
            return string.Empty;

        // ToString() is what <ImportMap /> writes into the page. If a future framework version
        // changes that by so much as a byte, the paired integration test fails — it hashes the
        // rendered page's import map and compares it to this.
        var json = definition.ToString();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return $"'sha256-{Convert.ToBase64String(digest)}'";
    }

    private ImportMapDefinition? BuildFromResourceCollection()
    {
        // The asset collection is not a DI registration — MapStaticAssets attaches it to the routing
        // endpoints as metadata, which is why resolving ImportMapDefinition from the root provider
        // returns nothing (measured: it does). Reading it off the endpoint data source gets the same
        // collection <ImportMap /> renders from, without a request in hand.
        var assets = services.GetService<ResourceAssetCollection>()
            ?? services.GetService<EndpointDataSource>()?.Endpoints
                .Select(endpoint => endpoint.Metadata.GetMetadata<ResourceAssetCollection>())
                .FirstOrDefault(collection => collection is not null);

        return assets is null ? null : ImportMapDefinition.FromResourceCollection(assets);
    }
}
