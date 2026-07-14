using AkeroGarden.Web.Data;
using AkeroGarden.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        configure?.Invoke(services);

        return services.BuildServiceProvider();
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
