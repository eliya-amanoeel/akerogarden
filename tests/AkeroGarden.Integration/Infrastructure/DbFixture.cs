using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        // Audit trail (§6.9): registered in the base so any service that now depends on IAuditWriter
        // (BookingService, ReminderJob) resolves. Defaults to a System actor; audit tests override
        // the accessor via `configure` to assert admin attribution. TimeProvider backs the audit
        // timestamp; a test may replace it (last registration wins).
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IAuditActorAccessor, TestAuditActorAccessor>();
        services.AddScoped<IAuditWriter, AuditWriter>();
    }

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
