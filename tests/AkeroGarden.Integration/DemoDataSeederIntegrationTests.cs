using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
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
        // Pinned, and the seeder now takes the same TimeProvider. This file asserts where the seeded
        // bookings sit *relative to today* — one of them with check-in exactly at the 40-day issuance
        // lead time, an exact boundary. While the seeder read DateTime.UtcNow and the assertions
        // recomputed "today" separately, a run straddling midnight put those one day apart and
        // flipped ShouldIssueOn. Pinning the test alone could not have fixed it; the clock had to
        // become an input to the seeder as well.
        var services = _fixture.BuildServices(s =>
        {
            DbFixture.PinClock(s, DbFixture.FixedNow);
            s.AddScoped<DemoDataSeeder>();
        });
        scope = services.CreateScope();
        Today = DbFixture.TodayIn(services);
        Now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
    }

    /// <summary>"Today" per the pinned clock the seeder also uses — set by <see cref="NewSeeder"/>.</summary>
    private DateOnly Today { get; set; }

    /// <summary>The pinned instant, for comparisons against seeded timestamps.</summary>
    private DateTime Now { get; set; }

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
                     BookingStatus.Pending, BookingStatus.AwaitingDeposit, BookingStatus.Approved, BookingStatus.DetailsComplete,
                     BookingStatus.Active, BookingStatus.Completed, BookingStatus.Denied,
                     BookingStatus.Cancelled, BookingStatus.Closed,
                 })
            Assert.Contains(expected, statuses);

        // Live in-progress states carry no retention date; terminal states do (FR-91). Three bookings
        // are seeded Approved, one per stage of link issuance (FR-108), so each is picked by contact
        // rather than by status.
        var approvedAll = await db.Bookings.Where(b => b.Status == BookingStatus.Approved).ToListAsync();
        Assert.All(approvedAll, b => Assert.Null(b.DeleteAfter));

        // 1. Link already issued and sent.
        var approved = Assert.Single(approvedAll, b => b.ContactEmail == "johan.bergstrom@example.com");
        Assert.NotNull(approved.CompletionToken);
        Assert.True(approved.CompletionTokenExpiry > Now);
        Assert.NotNull(approved.CompletionLinkSentAt); // dates the link for the nudge grace (FR-63)

        // 2. Check-in exactly at the lead time and no link yet — what the issuance job picks up.
        var awaitingLink = Assert.Single(approvedAll, b => b.ContactEmail == "elsa.wikander@example.com");
        Assert.Null(awaitingLink.CompletionToken);
        Assert.Null(awaitingLink.CompletionLinkSentAt);
        Assert.True(CompletionTokenPolicy.ShouldIssueOn(
            Today, awaitingLink.CheckIn!.Value));

        // 3. Far beyond the window — no link, and none due yet.
        var farOut = Assert.Single(approvedAll, b => b.ContactEmail == "nils.akerlund@example.com");
        Assert.Null(farOut.CompletionToken);
        Assert.False(CompletionTokenPolicy.ShouldIssueOn(
            Today, farOut.CheckIn!.Value));

        // Waiting for a deposit (FR-115/116): one on time, two overdue and not yet reminded — the ones
        // DepositReminderJob acts on, one with a price and one without (asked before prices were
        // entered, §5.6), so both reminder wordings can be seen. Retention is kept, like Pending (FR-91).
        var waiting = await db.Bookings.Where(b => b.Status == BookingStatus.AwaitingDeposit).ToListAsync();
        Assert.Equal(3, waiting.Count);
        Assert.All(waiting, b => Assert.NotNull(b.DeleteAfter));
        Assert.Single(waiting, b => !DepositPolicy.IsOverdue(b.DepositDueDate, Today));
        var reminderDue = waiting.Where(b => DepositPolicy.IsReminderDue(b.Status, b.DepositDueDate, b.DepositReminderSentAt, Today)).ToList();
        Assert.Equal(2, reminderDue.Count);
        Assert.Single(reminderDue, b => b.EstimatedPriceInclVat is null);

        // Both ways of saying yes can be tried: two Pending stays, one with a building already assigned.
        var pendingStays = await db.Bookings
            .Where(b => b.Status == BookingStatus.Pending && b.BookingType == BookingType.StayRequest)
            .Include(b => b.BuildingAssignments)
            .ToListAsync();
        Assert.Equal(2, pendingStays.Count);
        Assert.Single(pendingStays, b => b.BuildingAssignments.Count > 0);

        var completed = await db.Bookings.SingleAsync(b => b.Status == BookingStatus.Completed);
        Assert.NotNull(completed.DeleteAfter);
        Assert.Equal(completed.CheckOut!.Value.AddYears(2), completed.DeleteAfter);

        var denied = await db.Bookings.SingleAsync(b => b.Status == BookingStatus.Denied);
        Assert.False(string.IsNullOrWhiteSpace(denied.DenialReason));

        var cancelled = await db.Bookings.SingleAsync(b => b.Status == BookingStatus.Cancelled);
        Assert.NotNull(cancelled.CancelledAt);
        Assert.NotNull(cancelled.CancellationInitiator);

        // Participants exist as a roster; the group's meal snapshots hang off the booking (FR-105)
        // and reference live meal options.
        var participants = await db.Participants.ToListAsync();
        Assert.NotEmpty(participants);

        var meals = await db.BookingMealSelections.ToListAsync();
        Assert.NotEmpty(meals);
        Assert.All(meals, m => Assert.False(string.IsNullOrWhiteSpace(m.MealOptionSnapshot)));
        var mealOptionIds = await db.MealOptions.Select(o => o.Id).ToListAsync();
        Assert.All(meals, m => Assert.Contains(m.MealOptionId, mealOptionIds));

        // One choice per sitting for the whole group — never one per participant.
        Assert.All(meals.GroupBy(m => (m.BookingId, m.Date, m.MealType)), g => Assert.Single(g));

        // Approved-and-beyond bookings carry their lodging assignment with both name snapshots
        // (§8.16, NFR-16). Participants no longer carry any room link (FR-56 tombstoned).
        var assignments = await db.BookingBuildingAssignments.ToListAsync();
        Assert.NotEmpty(assignments);
        Assert.All(assignments, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.BuildingNameSnapshot));
            Assert.False(string.IsNullOrWhiteSpace(a.PhysicalBuildingNameSnapshot));
        });

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

    [Fact]
    public async Task Seed_IncludesABookingTheReminderJobWillActuallyPickUp()
    {
        // The two-week reminder (FR-63/64) only fires for a booking that is DetailsComplete or later
        // with check-in inside the next 14 days. Without one seeded, seeing that email in a dev inbox
        // means hand-editing dates in the database — so this asserts the affordance stays, rather
        // than leaving it to be quietly broken by a later change to the seed dates.
        var seeder = NewSeeder(out var scope, out var db);
        using var _s = scope;

        await seeder.ResetAndSeedAsync();

        var today = Today;
        var windowEnd = today.AddDays(ReminderSchedule.LeadDays);

        var due = await db.Bookings
            .Where(b => b.ReminderEmailSentAt == null
                        && b.CheckIn != null
                        && b.CheckIn >= today
                        && b.CheckIn <= windowEnd
                        && (b.Status == BookingStatus.Approved
                            || b.Status == BookingStatus.DetailsComplete
                            || b.Status == BookingStatus.Active))
            .ToListAsync();

        var booking = Assert.Single(due, b => b.Status == BookingStatus.DetailsComplete);

        // Non-standard times on purpose: the email must demonstrably carry the booking's own times
        // rather than the 14:00/12:00 standard (FR-64).
        Assert.NotEqual(MealOccasionResolver.StandardCheckInTime, booking.CheckInTime);
        Assert.NotEqual(MealOccasionResolver.StandardCheckOutTime, booking.CheckOutTime);

        // And it has to have content worth reading: participants and the group's chosen dishes.
        await db.Entry(booking).Collection(b => b.Participants).LoadAsync();
        await db.Entry(booking).Collection(b => b.MealSelections).LoadAsync();
        Assert.NotEmpty(booking.Participants);
        Assert.NotEmpty(booking.MealSelections);
    }
}
