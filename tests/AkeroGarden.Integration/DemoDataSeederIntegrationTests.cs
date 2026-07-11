using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the Phase 8 <see cref="DemoDataSeeder"/> demo/UAT tooling against real
/// PostgreSQL: it produces a valid dataset covering every booking state with faithful snapshots and
/// retention dates, it preserves admin users while wiping app data, and it is idempotent (a second
/// run re-wipes rather than accumulating or violating the Restrict foreign keys).
/// </summary>
public class DemoDataSeederIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public DemoDataSeederIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private DemoDataSeeder NewSeeder(out IServiceScope scope, out ApplicationDbContext db)
    {
        var services = _fixture.BuildServices(s => s.AddScoped<DemoDataSeeder>());
        scope = services.CreateScope();
        db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
    }

    [Fact]
    public async Task ResetAndSeed_ProducesValidDatasetCoveringEveryState()
    {
        var seeder = NewSeeder(out var scope, out var db);
        using var _s = scope;

        await seeder.ResetAndSeedAsync();

        // Reference data present.
        Assert.NotEmpty(await db.Buildings.ToListAsync());
        Assert.NotEmpty(await db.Rooms.ToListAsync());
        Assert.NotEmpty(await db.MealOptions.ToListAsync());
        Assert.NotEmpty(await db.PriceRules.ToListAsync());
        Assert.NotEmpty(await db.Facilities.ToListAsync());

        // The "Hela lägret" bundle references the four 2 000 kr zones (§5.5).
        var bundle = await db.CleaningZones.SingleAsync(z => z.Name == "Hela lägret");
        Assert.Equal(4, bundle.IncludesZoneIds.Count);
        var zoneIds = await db.CleaningZones.Select(z => z.Id).ToListAsync();
        Assert.All(bundle.IncludesZoneIds, id => Assert.Contains(id, zoneIds));

        // Every lifecycle state is represented.
        var statuses = await db.Bookings.Select(b => b.Status).Distinct().ToListAsync();
        foreach (var expected in new[]
                 {
                     BookingStatus.Pending, BookingStatus.Approved, BookingStatus.DetailsComplete,
                     BookingStatus.Active, BookingStatus.Completed, BookingStatus.Denied,
                     BookingStatus.Cancelled, BookingStatus.Closed,
                 })
            Assert.Contains(expected, statuses);

        // Live in-progress states carry no retention date; terminal states do (FR-91).
        var approved = await db.Bookings.SingleAsync(b => b.Status == BookingStatus.Approved);
        Assert.Null(approved.DeleteAfter);
        Assert.NotNull(approved.CompletionToken);
        Assert.True(approved.CompletionTokenExpiry > DateTime.UtcNow);

        var completed = await db.Bookings.SingleAsync(b => b.Status == BookingStatus.Completed);
        Assert.NotNull(completed.DeleteAfter);
        Assert.Equal(completed.CheckOut!.Value.AddYears(2), completed.DeleteAfter);

        var denied = await db.Bookings.SingleAsync(b => b.Status == BookingStatus.Denied);
        Assert.False(string.IsNullOrWhiteSpace(denied.DenialReason));

        var cancelled = await db.Bookings.SingleAsync(b => b.Status == BookingStatus.Cancelled);
        Assert.NotNull(cancelled.CancelledAt);
        Assert.NotNull(cancelled.CancellationInitiator);

        // Participants + meal snapshots exist and reference live meal options.
        var participants = await db.Participants
            .Include(p => p.MealPreferences)
            .ToListAsync();
        Assert.NotEmpty(participants);

        var meals = participants.SelectMany(p => p.MealPreferences).ToList();
        Assert.NotEmpty(meals);
        Assert.All(meals, m => Assert.False(string.IsNullOrWhiteSpace(m.MealOptionSnapshot)));
        var mealOptionIds = await db.MealOptions.Select(o => o.Id).ToListAsync();
        Assert.All(meals, m => Assert.Contains(m.MealOptionId, mealOptionIds));

        // Overnight participants with a room carry a room snapshot (NFR-16).
        var assigned = participants.Where(p => p.RoomId is not null).ToList();
        Assert.NotEmpty(assigned);
        Assert.All(assigned, p => Assert.False(string.IsNullOrWhiteSpace(p.RoomAssignmentSnapshot)));

        // Blocks at all three scopes.
        var scopes = await db.BlockedPeriods.Select(b => b.Scope).Distinct().ToListAsync();
        Assert.Contains(BlockScope.WholeProperty, scopes);
        Assert.Contains(BlockScope.Building, scopes);
        Assert.Contains(BlockScope.Room, scopes);
    }

    [Fact]
    public async Task ResetAndSeed_IsIdempotent_AndPreservesAdminUsers()
    {
        var seeder = NewSeeder(out var scope, out var db);
        using var _s = scope;

        // Seed an admin user that must survive the wipe.
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"admin-{Guid.NewGuid():N}@example.com";
        var createResult = await users.CreateAsync(
            new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, IsActive = true },
            "Str0ng-Passw0rd!");
        Assert.True(createResult.Succeeded);

        await seeder.ResetAndSeedAsync();
        var firstCount = await db.Bookings.CountAsync();

        // A second run re-wipes cleanly (Restrict FKs not violated) and does not accumulate.
        await seeder.ResetAndSeedAsync();
        var secondCount = await db.Bookings.CountAsync();

        Assert.Equal(firstCount, secondCount);
        Assert.True(firstCount >= 8);

        // The admin user is untouched by the wipe.
        Assert.NotNull(await users.FindByEmailAsync(email));
    }
}
