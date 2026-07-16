using AkeroGarden.Web.Public.Components;
using AkeroGarden.Web.Core.Services;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// ── Diagnostics (Phase 11) — structured logging, correlation IDs, failure diagnosis ──────────
// Config-gated: local dev (no connection string) exports nothing; staging/prod send traces/logs/
// metrics to Application Insights via OpenTelemetry.
var appInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrEmpty(appInsightsConnectionString))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(options =>
        options.ConnectionString = appInsightsConnectionString);
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

// ── Public-only concern: distributed inquiry rate limiting (NFR-04) ───────────────────────────
builder.Services.AddAkeroInquiryRateLimiting(builder.Configuration);

// ── Blazor (pure Static SSR — no Interactive Server, no Identity, no SignalR) ───
builder.Services.AddRazorComponents();

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
// Forwarded headers first, so RemoteIpAddress/scheme reflect the real client before any downstream
// middleware (HTTPS redirect, rate limiter) reads them (NFR-04).
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>();

app.Run();

// Exposed (public) so the host-separation integration tests can boot this host in-memory via
// WebApplicationFactory<Program>. Referenced under the `PublicApp` extern alias from the test project.
public partial class Program;
