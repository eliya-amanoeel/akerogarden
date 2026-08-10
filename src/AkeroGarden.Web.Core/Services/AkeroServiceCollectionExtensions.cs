using AkeroGarden.Web.Core.Data;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace AkeroGarden.Web.Core.Services;

/// <summary>
/// Composition-root helpers grouping the Core service registrations by concern, so the two host
/// apps (Phase 10b) compose only the subset each surface needs without duplicating wiring:
/// <list type="bullet">
///   <item><see cref="AddAkeroCommon"/>, <see cref="AddAkeroDatabase"/>, <see cref="AddAkeroReferenceDataServices"/>,
///     <see cref="AddAkeroBookingEngine"/>, <see cref="AddAkeroEmail"/>, <see cref="AddAkeroAudit"/> — both hosts.</item>
///   <item><see cref="AddAkeroInquiryRateLimiting"/> — public only (only the inquiry form is throttled).</item>
///   <item><see cref="AddAkeroImageStorage"/>, <see cref="AddAkeroAdminInvites"/>, <see cref="AddAkeroScheduledJobs"/>,
///     <see cref="AddAkeroSeeding"/> — admin only.</item>
/// </list>
/// The Identity/auth-cookie, antiforgery, forwarded-headers, Data-Protection, telemetry and
/// Razor-render-mode wiring stays in each host's Program.cs — it is host-shaped, not shared.
/// The audit <em>actor accessor</em> is likewise host-specific (admin resolves the signed-in admin;
/// public uses a system/guest actor), so only the writer is registered here.
/// </summary>
public static class AkeroServiceCollectionExtensions
{
    /// <summary>Cross-cutting singletons both hosts need (the clock backs audit timestamps and jobs).</summary>
    public static IServiceCollection AddAkeroCommon(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>EF Core + PostgreSQL. Migrations live in this assembly; only the admin host applies them.</summary>
    public static IServiceCollection AddAkeroDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));
        return services;
    }

    /// <summary>Reference-data services (Phase 1 + the Phase 8 cleaning rework §8.14). Both surfaces read them.</summary>
    public static IServiceCollection AddAkeroReferenceDataServices(this IServiceCollection services)
    {
        services.AddScoped<BuildingService>();
        services.AddScoped<RoomService>();
        services.AddScoped<MealOptionService>();
        services.AddScoped<PriceRuleService>();
        services.AddScoped<CleaningZoneService>();
        services.AddScoped<FacilityService>();
        services.AddScoped<PhysicalBuildingService>();
        return services;
    }

    /// <summary>Booking engine (Phase 3): creation, availability, blocks, occupancy.</summary>
    public static IServiceCollection AddAkeroBookingEngine(this IServiceCollection services)
    {
        services.AddScoped<BookingService>();
        services.AddScoped<BlockedPeriodService>();
        services.AddScoped<OccupancyProvider>();
        return services;
    }

    /// <summary>SMTP + the Swedish booking-lifecycle email templates (§9). Guest mail on public, admin mail on admin.</summary>
    public static IServiceCollection AddAkeroEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SmtpOptions>(configuration.GetSection("Smtp"));
        services.AddScoped<IEmailService, SmtpEmailService>();
        services.Configure<BookingEmailOptions>(configuration.GetSection("BookingEmail"));
        services.AddScoped<BookingEmailService>();
        return services;
    }

    /// <summary>Append-only audit writer (§6.9 / §8.15). The actor accessor is registered per host.</summary>
    public static IServiceCollection AddAkeroAudit(this IServiceCollection services)
    {
        services.AddScoped<IAuditWriter, AuditWriter>();
        return services;
    }

    /// <summary>Facility-image blob storage (FR-27) — admin upload side only.</summary>
    public static IServiceCollection AddAkeroImageStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BlobOptions>(configuration.GetSection("Blob"));
        services.AddScoped<IImageStorage, BlobImageStorage>();
        return services;
    }

    /// <summary>Admin invite lifecycle (Phase 9, FR-74) — admin only.</summary>
    public static IServiceCollection AddAkeroAdminInvites(this IServiceCollection services)
    {
        services.AddScoped<AdminInviteService>();
        return services;
    }

    /// <summary>Admin + demo seeding — admin only (the migrating/seeding process).</summary>
    public static IServiceCollection AddAkeroSeeding(this IServiceCollection services)
    {
        services.AddScoped<AdminSeeder>();
        // Demo/UAT tooling — only wipes + reseeds when DemoSeed:Reset=true (never set in prod).
        services.AddScoped<DemoDataSeeder>();
        return services;
    }

    /// <summary>In-process daily job runner (Phase 5) — admin only, so it runs in exactly one process.</summary>
    public static IServiceCollection AddAkeroScheduledJobs(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JobOptions>(configuration.GetSection("Jobs"));
        services.AddScoped<IScheduledJob, RetentionJob>();
        // Issuance runs before the reminder sweep. The two normally fire 26 days apart, but if the
        // issuance job has been down and a link-less booking reaches 14 days, this ordering gives it
        // the link first — the nudge then sees a link sent seconds ago and defers on its grace period
        // rather than telling the same guest, in the same minute, both "here is your link" and "you
        // have not filled in your details".
        services.AddScoped<IScheduledJob, CompletionLinkIssuanceJob>();
        services.AddScoped<IScheduledJob, ReminderJob>();
        services.AddHostedService<DailyJobRunner>();
        return services;
    }

    /// <summary>
    /// Distributed inquiry rate limiting (NFR-04) — public only. The multiplexer is built at startup so a
    /// bad/unresolved connection string degrades to "no rate limiting" (fail open, logged) rather than
    /// crashing the inquiry page at request time; only an unparseable string falls back to the no-op limiter.
    /// </summary>
    public static IServiceCollection AddAkeroInquiryRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RateLimitOptions>(configuration.GetSection("RateLimit"));
        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? configuration["Redis:ConnectionString"]
            ?? "localhost:6379";
        try
        {
            var redisConfig = ConfigurationOptions.Parse(redisConnectionString);
            redisConfig.AbortOnConnectFail = false;
            services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConfig));
            services.AddScoped<IInquiryRateLimiter, RedisInquiryRateLimiter>();
        }
        catch (Exception ex)
        {
            // Console at bootstrap (the logging pipeline isn't built yet); surfaces in the container log stream.
            Console.Error.WriteLine(
                $"[RateLimit] Redis could not be initialised — rate limiting disabled (fail open): {ex.Message}");
            services.AddSingleton<IInquiryRateLimiter, NoOpInquiryRateLimiter>();
        }

        return services;
    }
}
