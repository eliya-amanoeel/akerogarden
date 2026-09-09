using AkeroGarden.Web.Public.Components;
using AkeroGarden.Web.Core.Security;
using AkeroGarden.Web.Core.Services;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Suppress the "Server: Kestrel" response header (post-launch security review, finding #19). It names
// the server software on every response and buys an attacker a free fingerprint; nothing needs it.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// ── Diagnostics (Phase 11) — structured logging, correlation IDs, failure diagnosis ──────────
// Config-gated: local dev (no connection string) exports nothing; staging/prod send traces/logs/
// metrics to Application Insights via OpenTelemetry.
var appInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrEmpty(appInsightsConnectionString))
{
    builder.Services.AddOpenTelemetry()
        // Strips the completion token out of /slutfor/{token} before anything is exported (post-launch
        // security review, finding #1 — the token is a bearer credential for guest data and was being
        // written verbatim into Log Analytics). Registered BEFORE UseAzureMonitor on purpose:
        // processors run in registration order and the exporter is itself a processor, so adding this
        // afterwards would redact too late. See UrlRedactingProcessor.
        .WithTracing(tracing => tracing.AddProcessor(new UrlRedactingProcessor()))
        // Drop the OpenTelemetry instrument metrics (finding #13). These are the bulk of AppMetrics:
        // on 2026-09-08 http.client.open_connections alone produced 35,594 rows and
        // http.client.active_requests 21,677, against 2,074 for everything else combined. With this
        // view active, AppMetrics contained ONLY the internal _APPRESOURCEPREVIEW_ row — measured, not
        // assumed.
        //
        // Two mechanisms are needed and they cover different sources, which cost three deploy cycles
        // to establish: this view stops the OTel instruments, while EnablePerformanceCounters below
        // stops AppPerformanceCounters, which no view can reach. Removing either one lets that half
        // back in.
        .WithMetrics(metrics => metrics.AddView(_ => MetricStreamConfiguration.Drop))
        .UseAzureMonitor(options =>
        {
            options.ConnectionString = appInsightsConnectionString;
            // Verified off: AppPerformanceCounters went to zero rows within minutes of this shipping.
            options.EnablePerformanceCounters = false;
            // No observable effect either way — those metrics do not appear to be flowing on this
            // configuration. Kept because it is the documented switch for them.
            options.EnableStandardMetrics = false;
        });
}

// ── Forwarded headers (NFR-04 — real client IP behind the App Service load balancer) ──────────
// App Service terminates TLS and reverse-proxies to the app, so RemoteIpAddress is the platform
// front end, not the guest. The inquiry rate limiter keys on the client IP, so without this it
// would throttle the whole site as one client. UseForwardedHeaders (registered first in the
// pipeline) rewrites RemoteIpAddress/scheme from X-Forwarded-For/-Proto. ForwardLimit = 1 + cleared
// known proxies takes the single platform front end's appended client IP and is not spoofable by a
// client-set header.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ── Core services (Phase 10b — grouped composition-root extensions in AkeroGarden.Web.Core) ──
builder.Services.AddAkeroCommon();
builder.Services.AddAkeroDatabase(builder.Configuration);

// ── Data Protection key persistence (Phase-7 hardening) ─────────────────────────
// The public ring protects the antiforgery token on the booking form. Isolated from the admin ring
// by application name so one host can never unprotect the other's payloads (Phase 10b — shared
// nothing). Config-gated: local dev (no blob conn) keeps the default filesystem key ring.
var dataProtectionOptions = new DataProtectionOptions();
builder.Configuration.GetSection("DataProtection").Bind(dataProtectionOptions);
builder.Services.AddAkeroDataProtection(dataProtectionOptions, "AkeroGarden.Public");

// ── Antiforgery (the inquiry / completion forms POST) ──────────────────────────
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

// ── Shared services (reference data, booking engine, guest email, audit writer) ─
builder.Services.AddAkeroReferenceDataServices();
builder.Services.AddAkeroBookingEngine();
builder.Services.AddAkeroEmail(builder.Configuration);
builder.Services.AddAkeroAudit();
// The public host has no Identity/auth state. Its only audited path (completion) supplies
// AuditActor.Guest explicitly, so this accessor is never invoked; it satisfies AuditWriter's
// constructor and defaults any stray public audit entry to the system actor.
builder.Services.AddScoped<IAuditActorAccessor, SystemAuditActorAccessor>();

// ── Facility image reads (the CSP/image finding) ──────────────────────────────
// The public host now SERVES images from /bilder/{name} rather than pointing the browser at the
// storage account, so it needs blob read access it previously did not. Its managed identity already
// holds the data-plane role; Bicep now supplies Blob__ServiceUri to this site too.
builder.Services.AddAkeroImageStorage(builder.Configuration);

// ── Public-only concern: distributed inquiry rate limiting (NFR-04) ───────────────────────────
builder.Services.AddAkeroInquiryRateLimiting(builder.Configuration);

// ── Blazor (pure Static SSR — no Interactive Server, no Identity, no SignalR) ───
builder.Services.AddRazorComponents();

// ── Health probe (Phase 11 — post-launch security review, finding #20) ────────
// Liveness ONLY: no database or dependency checks are registered, deliberately. App Service removes a
// failing instance from rotation, and on a single-worker B1 plan a health check that touches Postgres
// would turn a transient database blip into a full site outage — strictly worse than having no probe.
// This answers "is the process up and serving HTTP", which is what App Service actually needs.
builder.Services.AddHealthChecks();

// ── HSTS (Phase 11 security headers) ──────────────────────────────────────────
// The framework default is 30 days; a year is the conventional setting and is simply a longer-lived
// promise to the browser that this host is HTTPS-only. Deliberately WITHOUT IncludeSubDomains and
// WITHOUT preload: both bind subdomains that do not exist yet, and neither can be withdrawn quickly
// once a browser has recorded it — the wrong trade for a solo-maintained site.
builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
// Forwarded headers first, so RemoteIpAddress/scheme reflect the real client before any downstream
// middleware (HTTPS redirect, rate limiter) reads them (NFR-04).
app.UseForwardedHeaders();

// ── Security headers (Phase 11) — CSP + nosniff/frame/referrer/permissions ────────────────────
// Registered early so every response carries them, including error pages and static assets.
// interactiveCircuit: false — the public host is pure Static SSR, so no WebSocket origin is
// added to connect-src.
app.UseAkeroSecurityHeaders(interactiveCircuit: false);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();

// ── Technical SEO (Phase 11) — robots.txt + sitemap.xml ───────────────────────────────────────
// Both are generated rather than served as files in wwwroot, for two reasons: the sitemap is built
// from the app's OWN routes (a hand-kept list goes stale silently — a missing page produces no
// error anywhere), and robots.txt has to differ by environment. Only Production invites crawlers;
// staging.akerogarden.se is publicly reachable and serves the same pages, so anywhere else answers
// "Disallow: /" rather than competing with the real site as duplicate content.
app.MapGet("/robots.txt", (IOptions<BookingEmailOptions> emailOptions, IWebHostEnvironment env) =>
    Results.Text(
        SitemapBuilder.BuildRobots(emailOptions.Value.PublicBaseUrl, allowIndexing: env.IsProduction()),
        "text/plain"));

app.MapGet("/sitemap.xml", (
    IOptions<BookingEmailOptions> emailOptions, EndpointDataSource endpoints) =>
{
    var patterns = endpoints.Endpoints
        .OfType<RouteEndpoint>()
        .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty);

    return Results.Text(
        SitemapBuilder.BuildSitemap(
            emailOptions.Value.PublicBaseUrl, SitemapBuilder.SelectIndexablePaths(patterns)),
        "application/xml");
});

// Liveness probe for App Service (finding #20). Excluded from sitemap.xml by SitemapBuilder — it is
// an infrastructure endpoint, not a page.
app.MapHealthChecks("/health");

// ── Facility images (post-launch security review — the CSP/image finding) ─────────────────────
// Images are served BY THE APP, not fetched from the storage account by the browser. The
// img-src 'self' Content-Security-Policy shipped for NFR-39 blocks a Blob URL outright, so FR-81's
// "render from their anonymous Blob URLs" and the CSP could not both be true. This endpoint makes
// them so — and the visitor's browser never contacts the storage domain, which is what NFR-35 is
// actually about.
//
// The name is validated against an exact allow-list before it reaches storage: it must look like a
// name this app generated. Blob names are random GUIDs whose content never changes, so the response
// is immutable and cacheable for a year — that keeps essentially all image traffic off the B1
// instance after the first fetch.
app.MapGet($"{FacilityImagePath.Prefix}/{{blobName}}", async (
    string blobName, IImageStorage storage, HttpContext http, CancellationToken ct) =>
{
    if (!FacilityImagePath.IsValidBlobName(blobName))
        return Results.NotFound();

    var image = await storage.OpenFacilityImageAsync(blobName, ct);
    if (image is null)
        return Results.NotFound();

    http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    return Results.Stream(image.Content, image.ContentType, enableRangeProcessing: true,
        entityTag: image.ETag is null ? null : new Microsoft.Net.Http.Headers.EntityTagHeaderValue(image.ETag));
});

app.MapRazorComponents<App>();

app.Run();

// Exposed (public) so the host-separation integration tests can boot this host in-memory via
// WebApplicationFactory<Program>. Referenced under the `PublicApp` extern alias from the test project.
public partial class Program;
