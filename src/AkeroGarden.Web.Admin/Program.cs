using AkeroGarden.Web.Admin.Components;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Services;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

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
// App Service terminates TLS and reverse-proxies to the app; UseForwardedHeaders (registered first
// in the pipeline) rewrites RemoteIpAddress/scheme from X-Forwarded-For/-Proto. ForwardLimit = 1 +
// cleared known proxies takes the single platform front end's appended client IP and is not
// spoofable by a client-set header.
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

// ── Identity (admin host only) ────────────────────────────────────────────────
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 12;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Password reset links expire in 1 hour (FR-73 — UI text says "1 timme")
builder.Services.Configure<DataProtectionTokenProviderOptions>(opts =>
    opts.TokenLifespan = TimeSpan.FromHours(1));

// ── Data Protection key persistence (Phase-7 hardening) ─────────────────────────
// The admin ring protects the auth cookie. Isolated from the public ring by application name so one
// host can never unprotect the other's payloads (Phase 10b — shared nothing). Config-gated: local
// dev (no blob conn) keeps the default filesystem key ring.
var dataProtectionOptions = new DataProtectionOptions();
builder.Configuration.GetSection("DataProtection").Bind(dataProtectionOptions);
builder.Services.AddAkeroDataProtection(dataProtectionOptions, "AkeroGarden.Admin");

// ── Antiforgery ───────────────────────────────────────────────────────────────
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

// ── Auth cookie ───────────────────────────────────────────────────────────────
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.AccessDeniedPath = "/login";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    // Lax (not Strict): a Strict auth cookie is not sent on a top-level navigation that originates
    // from another site, so an admin clicking a link in a notification email (e.g. "Öppna förfrågan
    // i adminpanelen") lands on the login page even while signed in. Lax sends the cookie on
    // top-level GET navigations while still withholding it on cross-site POSTs; admin actions remain
    // antiforgery-protected, so this does not weaken CSRF protection.
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
});

// ── Shared services (data-adjacent — also composed by the public host) ─────────
builder.Services.AddAkeroReferenceDataServices();
builder.Services.AddAkeroBookingEngine();
builder.Services.AddAkeroEmail(builder.Configuration);
builder.Services.AddAkeroAudit();
// Admin resolves the acting admin from the Blazor circuit's auth state for audit attribution.
builder.Services.AddScoped<IAuditActorAccessor, AuditActorAccessor>();

// ── Admin-only concerns: image upload, invite lifecycle, background jobs, seeding ─────────────
builder.Services.AddAkeroImageStorage(builder.Configuration);
builder.Services.AddAkeroAdminInvites();
builder.Services.AddAkeroScheduledJobs(builder.Configuration);
builder.Services.AddAkeroSeeding();

// ── Blazor (Interactive Server) ────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
// Forwarded headers first, so RemoteIpAddress/scheme reflect the real client before any downstream
// middleware reads them (NFR-04).
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/admin/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// The admin host has no public home; its root sends the admin to the dashboard, which in turn
// redirects to /login via the auth cookie's LoginPath when they are not signed in. (Before the
// Phase 10b split, "/" served the public home — that page now lives only on the public host.)
app.MapGet("/", () => Results.LocalRedirect("/admin"));

// Logout runs in a real HTTP request (not the interactive circuit), so the auth cookie can actually
// be cleared. POST-only; antiforgery is validated explicitly because minimal APIs only auto-validate
// it for [FromForm]-bound endpoints — without this, a cross-site POST could force-logout the admin.
app.MapPost("/logout", async (
    HttpContext httpContext,
    IAntiforgery antiforgery,
    SignInManager<ApplicationUser> signInManager) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(httpContext);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.BadRequest();
    }

    await signInManager.SignOutAsync();
    return Results.LocalRedirect("/login");
});

// ── E2E test seam (Development only) ──────────────────────────────────────────
// Returns a pending invite's real setup link so the browser-driven activation test can reach the
// /aterstall-losenord flow, which is otherwise only delivered by email (no SMTP in E2E). Gated on
// BOTH Development AND an explicit config flag (E2ETestHooks) the E2E fixture sets — it is never
// mapped in staging or production. Exposes no data beyond a link the inviting admin already sees.
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("E2ETestHooks"))
{
    app.MapGet("/e2e/setup-link", async (
        string email, UserManager<ApplicationUser> users, AdminInviteService invites) =>
    {
        var user = await users.FindByEmailAsync(email);
        return user is null ? Results.NotFound() : Results.Text(await invites.GenerateSetupLinkAsync(user));
    });
}

// ── Admin list downloads (FR-44/45) ───────────────────────────────────────────
// PDF + Excel participant and food-preference lists, streamed on demand from the booking detail
// page. Auth-required; the booking id in the path is a Guid, not PII (NFR-05). Lists are only
// available once details are submitted (DetailsComplete or later).
const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

var lists = app.MapGroup("/admin/bokningar/{id:guid}").RequireAuthorization();
lists.MapGet("/deltagarlista.pdf", (Guid id, BookingService bookings) =>
    BuildListAsync(id, bookings, "deltagarlista.pdf", "application/pdf", ListPdfGenerator.ParticipantList));
lists.MapGet("/deltagarlista.xlsx", (Guid id, BookingService bookings) =>
    BuildListAsync(id, bookings, "deltagarlista.xlsx", XlsxContentType, ListExcelGenerator.ParticipantList));
lists.MapGet("/matlista.pdf", (Guid id, BookingService bookings) =>
    BuildListAsync(id, bookings, "matlista.pdf", "application/pdf", ListPdfGenerator.FoodPreferenceList));
lists.MapGet("/matlista.xlsx", (Guid id, BookingService bookings) =>
    BuildListAsync(id, bookings, "matlista.xlsx", XlsxContentType, ListExcelGenerator.FoodPreferenceList));

static async Task<IResult> BuildListAsync(
    Guid id, BookingService bookings, string fileName, string contentType, Func<BookingListData, byte[]> render)
{
    var booking = await bookings.GetWithParticipantsAsync(id);
    if (booking is null || !ListShaper.IsAvailableFor(booking.Status))
        return Results.NotFound();

    var bytes = render(ListShaper.Shape(booking));
    return Results.File(bytes, contentType, fileName);
}

// The admin host is the single migrating/seeding process (the public host never migrates).
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync();

    // Phase 8 demo/UAT: wipe all app data (admin preserved) and reseed synthetic demo data.
    // Gated by DemoSeed:Reset — never set in production; applied by hand to staging then removed.
    if (app.Configuration.GetValue<bool>("DemoSeed:Reset"))
        await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().ResetAndSeedAsync();
}

app.Run();
