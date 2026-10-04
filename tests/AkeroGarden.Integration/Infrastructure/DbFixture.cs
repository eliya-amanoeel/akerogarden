using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace AkeroGarden.Integration.Infrastructure;

public sealed class DbFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public IServiceProvider BuildServices(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddDbContext<ApplicationDbContext>(opts =>
            opts.UseNpgsql(ConnectionString));

        AddSharedServices(services);
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A <see cref="FakeTimeProvider"/> registration for tests whose outcome depends on <i>when</i>
    /// they run. Pass it to <see cref="BuildServices"/>: it is applied after the shared registrations,
    /// so it replaces the real clock.
    ///
    /// <para><b>Use this whenever the assertion involves a date.</b> <c>BookingService</c> measures
    /// three things from now against the booking's check-in — whether a completion link is issued at
    /// all (FR-108), how long it lives (FR-53), and whether the short-expiry warning fires (FR-75).
    /// A test seeding a fixed check-in against the real clock therefore asks a slightly different
    /// question every day it runs, and eventually a different one than its name claims. Three files
    /// broke that way at once in demo-feedback slice 7.</para>
    ///
    /// <para>Seeding dates relative to <c>DateTime.UtcNow</c> also works and is fine for a stay that
    /// merely needs to be "in the future". Pin the clock instead when the test is <i>about</i> a
    /// boundary — 40 days, 14 days, expiry — because then the distance is the thing under test and
    /// it should not be able to drift.</para>
    /// </summary>
    public static void PinClock(IServiceCollection services, DateTimeOffset instant) =>
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(instant));

    /// <summary>"Today" according to whatever clock this container holds — pinned or real. Seed
    /// helpers must derive dates from <i>this</i>, never from <c>DateTime.UtcNow</c>: a test that
    /// pins the clock and then seeds against the wall clock has two different "now"s, which is
    /// worse than not pinning at all. Swedish date, as the services judge it — a UTC date here would
    /// disagree with them between 22:00 and 24:00 UTC.</summary>
    public static DateOnly TodayIn(IServiceProvider provider) =>
        StockholmTime.Today(provider.GetRequiredService<TimeProvider>());

    /// <summary>The instant integration tests pin to unless they need a specific one. Chosen with no
    /// significance beyond being fixed: a test that depends on the actual value is over-fitted.</summary>
    public static readonly DateTimeOffset FixedNow = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Everything except the DbContext, so <see cref="BuildServices"/> and
    /// <see cref="BuildIsolatedServicesAsync"/> can never drift into registering different things.</summary>
    private static void AddSharedServices(IServiceCollection services)
    {
        services.AddIdentity<ApplicationUser, IdentityRole>(opts =>
            {
                opts.Password.RequiredLength = 12;
                opts.Password.RequireNonAlphanumeric = true;
                opts.Lockout.MaxFailedAccessAttempts = 5;
                opts.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                opts.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<AdminSeeder>();

        // Availability (FR-10): registered in the base because BookingService now refuses to approve
        // a booking over dates another group holds, and every container that resolves BookingService
        // therefore needs these two. Re-registering OccupancyProvider in a test's own `configure` is
        // harmless — the last registration of a type wins.
        services.AddScoped<OccupancyProvider>();
        services.AddScoped<AvailabilityService>();

        // Audit trail (§6.9): registered in the base so any service that now depends on IAuditWriter
        // (BookingService, ReminderJob) resolves. Defaults to a System actor; audit tests override
        // the accessor via `configure` to assert admin attribution. TimeProvider backs the audit
        // timestamp; a test may replace it (last registration wins).
        services.AddSingleton(TimeProvider.System);

        // FacilityService cleans up superseded facility images, so it needs the storage seam.
        // A singleton so a test can reach the same instance it asserts against, via
        // BuildServices(s => s.AddSingleton<IImageStorage>(myFake)) or by resolving it back out.
        services.AddSingleton<FakeImageStorage>();
        services.AddSingleton<IImageStorage>(sp => sp.GetRequiredService<FakeImageStorage>());
        services.AddScoped<IAuditActorAccessor, TestAuditActorAccessor>();
        // The hosts' own registration (writer + JobRunContext), so the fixture cannot drift from it.
        services.AddAkeroAudit();
    }

    /// <summary>
    /// A check-in date for a seeded stay, spaced so no two stays in a class overlap.
    ///
    /// <para><b>Why this is needed.</b> Approval refuses to put two groups on the same dates
    /// (FR-10) — the property is let to one group at a time. Tests in a class share one database,
    /// so several bookings seeded at one fixed date used to be independent and now collide: every
    /// approval after the first legitimately fails.</para>
    ///
    /// <para><paramref name="sequence"/> is the caller's <b>own</b> counter, not a shared one, so a
    /// class can reason about how far out its last stay lands. That matters wherever the distance
    /// itself carries meaning — a completion link is only issued within 40 days of check-in
    /// (FR-108), so a class relying on one must keep every window inside that. <paramref
    /// name="stepDays"/> must clear a stay and its two turnover days (FR-106); it may be negative to
    /// walk backwards for stays in the past.</para>
    /// </summary>
    public static DateOnly StayWindow(IServiceProvider provider, int sequence, int firstDaysOut, int stepDays = 7) =>
        TodayIn(provider).AddDays(firstDaysOut + (sequence * stepDays));

    /// <summary>
    /// Services pointed at a brand-new, freshly migrated database inside the same container.
    /// For tests whose subject is the state of the whole table rather than one row — seeding is the
    /// case that forced this: <c>AdminSeeder</c> only creates the first admin when *no* user exists,
    /// so two such tests sharing <see cref="BuildServices"/>' database would pass or fail depending
    /// on which xUnit happened to run first. Costs one migration per call; use it only where
    /// emptiness is the precondition.
    /// </summary>
    public async Task<IServiceProvider> BuildIsolatedServicesAsync(Action<IServiceCollection>? configure = null)
    {
        var name = $"test_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ToString();

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(opts => opts.UseNpgsql(connectionString));
        AddSharedServices(services);
        configure?.Invoke(services);
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
        return provider;
    }

    /// <summary>
    /// Puts both FR-104 roles in AspNetRoles. Any test that invites or assigns a role needs this
    /// first: <c>UserManager.AddToRoleAsync</c> throws when the role is not in the store, so an
    /// invite fails outright on an empty role table. In the running app <c>AdminSeeder</c> does it
    /// on startup; these tests construct services directly, so they do it themselves. Idempotent.
    /// </summary>
    public static async Task EnsureAdminRolesAsync(IServiceProvider scopedProvider)
    {
        var roleManager = scopedProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in AdminRoles.All)
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var services = BuildServices();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.StopAsync();
}
