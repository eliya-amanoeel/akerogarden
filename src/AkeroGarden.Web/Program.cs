using AkeroGarden.Web.Components;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Services;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ── Diagnostics (Phase 11) — structured logging, correlation IDs, failure diagnosis ──────────
// Config-gated like Data Protection below: local dev (no connection string) exports nothing;
// staging/prod send traces/logs/metrics to Application Insights via OpenTelemetry. Request
// correlation (an operation ID tying together logs + dependency calls — SMTP, Postgres, Redis,
// Blob — for a single request) comes from the ASP.NET Core instrumentation this package wires
// in automatically; no hand-rolled correlation middleware needed.
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
// pipeline) rewrites RemoteIpAddress/scheme from X-Forwarded-For/-Proto. On App Service the single
// front end is the only reachable proxy and it appends the real client IP as the rightmost entry,
// so ForwardLimit = 1 + cleared known proxies takes that entry and is not spoofable by a client-set
// header. Clearing KnownNetworks/KnownProxies is the documented App Service pattern (the platform,
// not an in-VNet address range, is the trusted proxy).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

// ── Identity ─────────────────────────────────────────────────────────────────
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

// ── Data Protection key persistence (Phase-7 hardening — keys survive restarts / shared across instances) ─
// Without durable keys, an App Service recycle/redeploy/scale-out regenerates the key ring and
// invalidates every antiforgery token and auth cookie (a guest mid-`/boka` then hits "A valid
// antiforgery token was not provided" on submit). Config-gated: local dev (no blob conn) keeps the
// default filesystem key ring; staging/prod persist to Blob and wrap the master key with Key Vault.
var dataProtectionOptions = new DataProtectionOptions();
builder.Configuration.GetSection("DataProtection").Bind(dataProtectionOptions);
builder.Services.AddAkeroDataProtection(dataProtectionOptions);

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
    // top-level GET navigations (the email-link case) while still withholding it on cross-site POSTs;
    // admin actions remain antiforgery-protected, so this does not weaken CSRF protection.
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
});

// ── Email ─────────────────────────────────────────────────────────────────────
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddScoped<IEmailService, SmtpEmailService>();

// Booking-lifecycle emails (§9) — composes Swedish templates and dispatches via IEmailService.
builder.Services.Configure<BookingEmailOptions>(builder.Configuration.GetSection("BookingEmail"));
builder.Services.AddScoped<BookingEmailService>();

// ── Redis (distributed rate limiting — NFR-04) ────────────────────────────────
// The Redis multiplexer is built at startup so a bad/unresolved connection string (e.g. an
// unresolved Key Vault reference) degrades to "no rate limiting" (fail open, logged) rather than
// crashing the public inquiry page at request time. AbortOnConnectFail=false means a valid string
// with Redis merely unreachable still yields a real multiplexer that reconnects — only an
// unparseable string falls back to the no-op limiter.
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection("RateLimit"));
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? builder.Configuration["Redis:ConnectionString"]
    ?? "localhost:6379";
try
{
    var redisConfig = ConfigurationOptions.Parse(redisConnectionString);
    redisConfig.AbortOnConnectFail = false;
    builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConfig));
    builder.Services.AddScoped<IInquiryRateLimiter, RedisInquiryRateLimiter>();
}
catch (Exception ex)
{
    // Console at bootstrap (the logging pipeline isn't built yet); surfaces in the container log stream.
    Console.Error.WriteLine(
        $"[RateLimit] Redis could not be initialised — rate limiting disabled (fail open): {ex.Message}");
    builder.Services.AddSingleton<IInquiryRateLimiter, NoOpInquiryRateLimiter>();
}

// ── Booking engine (Phase 3) ──────────────────────────────────────────────────
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<BlockedPeriodService>();
builder.Services.AddScoped<OccupancyProvider>();
builder.Services.AddScoped<RoomAvailabilityService>();

// ── Audit trail (Phase 11, §6.9 / §8.15) ──────────────────────────────────────
builder.Services.AddScoped<IAuditActorAccessor, AuditActorAccessor>();
builder.Services.AddScoped<IAuditWriter, AuditWriter>();

// ── Scheduled background jobs (Phase 5) ───────────────────────────────────────
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<JobOptions>(builder.Configuration.GetSection("Jobs"));
builder.Services.AddScoped<IScheduledJob, RetentionJob>();
builder.Services.AddScoped<IScheduledJob, ReminderJob>();
builder.Services.AddHostedService<DailyJobRunner>();

// ── Reference data services (Phase 1) ─────────────────────────────────────────
builder.Services.AddScoped<BuildingService>();
builder.Services.AddScoped<RoomService>();
builder.Services.AddScoped<MealOptionService>();
builder.Services.AddScoped<PriceRuleService>();
builder.Services.AddScoped<CleaningZoneService>();
builder.Services.AddScoped<FacilityService>();

// ── Reference data services (Phase 8: cleaning rework, §8.14) ────────────────
builder.Services.AddScoped<PhysicalBuildingService>();

// ── Admin invite lifecycle (Phase 9, FR-74) ───────────────────────────────────
builder.Services.AddScoped<AdminInviteService>();

// ── Blob storage (facility images, FR-27) ─────────────────────────────────────
builder.Services.Configure<BlobOptions>(builder.Configuration.GetSection("Blob"));
builder.Services.AddScoped<IImageStorage, BlobImageStorage>();

// ── Seeding ───────────────────────────────────────────────────────────────────
builder.Services.AddScoped<AdminSeeder>();
// Demo/UAT tooling — only wipes + reseeds when DemoSeed:Reset=true (never set in prod).
builder.Services.AddScoped<DemoDataSeeder>();

// ── Blazor ───────────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
// Forwarded headers first, so RemoteIpAddress/scheme reflect the real client before any
// downstream middleware (HTTPS redirect, rate limiter) reads them (NFR-04).
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Logout runs in a real HTTP request (not the interactive circuit), so the auth
// cookie can actually be cleared. POST-only; antiforgery is validated explicitly
// because minimal APIs only auto-validate it for [FromForm]-bound endpoints — without
// this, a cross-site POST could force-logout the admin.
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
