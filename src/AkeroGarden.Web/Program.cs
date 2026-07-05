using AkeroGarden.Web.Components;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

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
    options.Cookie.SameSite = SameSiteMode.Strict;
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

// ── Scheduled background jobs (Phase 5) ───────────────────────────────────────
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<JobOptions>(builder.Configuration.GetSection("Jobs"));
builder.Services.AddScoped<IScheduledJob, RetentionJob>();
builder.Services.AddHostedService<DailyJobRunner>();

// ── Reference data services (Phase 1) ─────────────────────────────────────────
builder.Services.AddScoped<BuildingService>();
builder.Services.AddScoped<RoomService>();
builder.Services.AddScoped<MealOptionService>();
builder.Services.AddScoped<PriceRuleService>();
builder.Services.AddScoped<CleaningZoneService>();
builder.Services.AddScoped<FacilityService>();

// ── Blob storage (facility images, FR-27) ─────────────────────────────────────
builder.Services.Configure<BlobOptions>(builder.Configuration.GetSection("Blob"));
builder.Services.AddScoped<IImageStorage, BlobImageStorage>();

// ── Seeding ───────────────────────────────────────────────────────────────────
builder.Services.AddScoped<AdminSeeder>();

// ── Blazor ───────────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
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

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<AdminSeeder>().SeedAsync();
}

app.Run();
