using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;
using AkeroGarden.Web.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the Phase 8 cleaning rework against real PostgreSQL (§5.5, §8.11/8.12,
/// FR-93): the completion form's Huvudbyggnad/Konferensbyggnad choices snapshot immediately and the
/// Konferensbyggnad choice is only honored when the Konferensrum facility was actually booked
/// (server-authoritative, never trusted from the posted form); the sleeping-building choice is
/// resolved from room assignment — at completion when the guest chooses rooms, and re-resolved
/// whenever the admin sets or changes a room assignment — one row per distinct occupied physical
/// building's zone, never per room.
/// </summary>
public class CleaningApplicabilityIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public CleaningApplicabilityIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private IServiceProvider NewProvider() =>
        _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(new FakeEmailService());
            s.AddScoped<BookingEmailService>();
            s.AddScoped<BookingService>();
            s.AddScoped<CleaningZoneService>();
            s.AddScoped<BuildingService>();
            s.AddScoped<RoomService>();
            s.AddScoped<FacilityService>();
            s.Configure<BookingEmailOptions>(o =>
            {
                o.PublicBaseUrl = "https://akerogarden.se";
                o.AdminBaseUrl = "https://admin.akerogarden.se";
                o.AdminNotificationEmail = "admin@test.se";
                o.ContactEmail = "info@test.se";
            });
        });

    private sealed record Ref(
        PhysicalBuilding Huvud, PhysicalBuilding Lilla, PhysicalBuilding Stora, PhysicalBuilding Konferens,
        CleaningZone HuvudZone, CleaningZone LillaZone, CleaningZone StoraZone, CleaningZone KonferensZone,
        Building Blido, Building Tjocko, Building Vato1, Room BlidoRum1, Room TjockoRum1, Room Vato1Rum1,
        Facility Konferensrum);

    /// <summary>Seeds the real §5.5 shape at a small scale: Huvudbyggnad + two sleeping physical
    /// buildings each with two lodging units, a Konferensbyggnad reachable only via the Konferensrum
    /// facility, and one room per lodging unit.</summary>
    private static async Task<Ref> SeedReferenceDataAsync(ApplicationDbContext db)
    {
        // This class shares one Postgres database across every [Fact] (IClassFixture<DbFixture>).
        // GetActiveByKindAsync/IsConferenceCleaningApplicableAsync assume exactly one active zone
        // per MainBuilding/Conference Kind — true in real admin usage — so an earlier test's zone
        // left active would make this test's lookup ambiguous. Deactivate those first; unlike
        // MainBuilding/Conference, SleepingBuilding zones are matched by PhysicalBuildingId (unique
        // per test), so they never collide and don't need this.
        var staleSingletons = await db.CleaningZones
            .Where(z => z.IsActive && (z.Kind == CleaningZoneKind.MainBuilding || z.Kind == CleaningZoneKind.Conference))
            .ToListAsync();
        foreach (var stale in staleSingletons) stale.IsActive = false;

        var huvud = new PhysicalBuilding { Id = Guid.NewGuid(), Name = $"Huvudbyggnad-{Guid.NewGuid():N}" };
        var lilla = new PhysicalBuilding { Id = Guid.NewGuid(), Name = $"Lilla-{Guid.NewGuid():N}" };
        var stora = new PhysicalBuilding { Id = Guid.NewGuid(), Name = $"Stora-{Guid.NewGuid():N}" };
        var konferens = new PhysicalBuilding { Id = Guid.NewGuid(), Name = $"Konferensbyggnad-{Guid.NewGuid():N}" };
        db.PhysicalBuildings.AddRange(huvud, lilla, stora, konferens);

        var huvudZone = new CleaningZone
        {
            Id = Guid.NewGuid(),
            Name = "Huvudbyggnad",
            Kind = CleaningZoneKind.MainBuilding,
            PhysicalBuildingId = huvud.Id,
            AmountExVat = 2000m,
            VatRate = 0.25m,
            IsActive = true,
        };
        var lillaZone = new CleaningZone
        {
            Id = Guid.NewGuid(),
            Name = "Lilla bostadslängan",
            Kind = CleaningZoneKind.SleepingBuilding,
            PhysicalBuildingId = lilla.Id,
            AmountExVat = 2000m,
            VatRate = 0.25m,
            IsActive = true,
        };
        var storaZone = new CleaningZone
        {
            Id = Guid.NewGuid(),
            Name = "Stora bostadslängan",
            Kind = CleaningZoneKind.SleepingBuilding,
            PhysicalBuildingId = stora.Id,
            AmountExVat = 2000m,
            VatRate = 0.25m,
            IsActive = true,
        };
        var konferensZone = new CleaningZone
        {
            Id = Guid.NewGuid(),
            Name = "Konferensbyggnad",
            Kind = CleaningZoneKind.Conference,
            PhysicalBuildingId = konferens.Id,
            AmountExVat = 2000m,
            VatRate = 0.25m,
            IsActive = true,
        };
        db.CleaningZones.AddRange(huvudZone, lillaZone, storaZone, konferensZone);

        var blido = new Building { Id = Guid.NewGuid(), Name = $"Blidö-{Guid.NewGuid():N}", PhysicalBuildingId = lilla.Id, IsActive = true };
        var tjocko = new Building { Id = Guid.NewGuid(), Name = $"Tjockö-{Guid.NewGuid():N}", PhysicalBuildingId = lilla.Id, IsActive = true };
        var vato1 = new Building { Id = Guid.NewGuid(), Name = $"Vätö1-{Guid.NewGuid():N}", PhysicalBuildingId = stora.Id, IsActive = true };
        db.Buildings.AddRange(blido, tjocko, vato1);

        var blidoRum1 = new Room { Id = Guid.NewGuid(), BuildingId = blido.Id, Name = "Rum 1", BedCount = 4, IsActive = true };
        var tjockoRum1 = new Room { Id = Guid.NewGuid(), BuildingId = tjocko.Id, Name = "Rum 1", BedCount = 3, IsActive = true };
        var vato1Rum1 = new Room { Id = Guid.NewGuid(), BuildingId = vato1.Id, Name = "Rum 1", BedCount = 2, IsActive = true };
        db.Rooms.AddRange(blidoRum1, tjockoRum1, vato1Rum1);

        var konferensrum = new Facility
        {
            Id = Guid.NewGuid(),
            Name = "Konferensrum",
            PhysicalBuildingId = konferens.Id,
            AmountExVat = 1200m,
            VatRate = 0.25m,
            IsActive = true,
        };
        db.Facilities.Add(konferensrum);

        await db.SaveChangesAsync();

        return new Ref(huvud, lilla, stora, konferens, huvudZone, lillaZone, storaZone, konferensZone,
            blido, tjocko, vato1, blidoRum1, tjockoRum1, vato1Rum1, konferensrum);
    }

    private static async Task<(Guid BookingId, string Token)> SeedApprovedBookingAsync(
        IServiceProvider provider, ApplicationDbContext db, BookingService bookings,
        Building[] assign, Ref? withFacility = null, int overnightCount = 2)
    {
        var input = new StayRequestInput
        {
            ContactName = "Grupp",
            ContactEmail = $"grupp-{Guid.NewGuid():N}@test.se",
            CheckIn = new DateOnly(2026, 8, 10),
            CheckOut = new DateOnly(2026, 8, 12),
            OvernightGuestCount = overnightCount,
            MealPlan = MealPlan.SelfCateringAll,
            SelectedFacilityIds = withFacility is null ? null : [withFacility.Konferensrum.Id],
            AcceptTerms = true,
        };
        var booking = await bookings.CreateStayRequestAsync(input, createdByAdmin: true);

        // Sleeping-cleaning zones resolve from the assigned buildings (FR-93), and assignment is a
        // precondition of approval (FR-103/D1) — so the buildings are chosen here, before approving.
        await AssignAsync(db, booking.Id, assign);
        await bookings.ApproveAsync(booking.Id);

        var persisted = await db.Bookings.FindAsync(booking.Id);
        return (booking.Id, persisted!.CompletionToken!);
    }

    /// <summary>Assigns specific lodging buildings, replacing any existing assignment.</summary>
    private static async Task AssignAsync(ApplicationDbContext db, Guid bookingId, params Building[] buildings)
    {
        var existing = await db.BookingBuildingAssignments.Where(a => a.BookingId == bookingId).ToListAsync();
        db.BookingBuildingAssignments.RemoveRange(existing);
        foreach (var b in buildings)
        {
            var physicalName = await db.PhysicalBuildings
                .Where(p => p.Id == b.PhysicalBuildingId).Select(p => p.Name).FirstAsync();
            db.BookingBuildingAssignments.Add(new BookingBuildingAssignment
            {
                Id = Guid.NewGuid(),
                BookingId = bookingId,
                BuildingId = b.Id,
                BuildingNameSnapshot = b.Name,
                PhysicalBuildingNameSnapshot = physicalName,
                AssignedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
    }

    private static CompletionFormModel Form(
        bool main, bool sleeping, bool conference, int participants = 2)
    {
        var form = new CompletionFormModel
        {
            MainBuildingCleaning = main,
            SleepingCleaning = sleeping,
            ConferenceCleaning = conference,
        };
        for (var i = 0; i < participants; i++)
        {
            form.Overnight.Add(new OvernightParticipantEntry
            {
                FullName = $"Deltagare {form.Overnight.Count + 1}",
                AgeCategory = AgeCategory.Adult,
            });
        }
        return form;
    }

    [Fact]
    public async Task Submit_MainBuildingChosen_SetsFlagAndSnapshotsZone()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var r = await SeedReferenceDataAsync(db);
        var (bookingId, token) = await SeedApprovedBookingAsync(provider, db, bookings, [r.Blido]);

        var result = await bookings.SubmitCompletionAsync(
            token, Form(main: true, sleeping: false, conference: false), DateTime.UtcNow);
        Assert.Equal(CompletionSubmitOutcome.Success, result.Outcome);

        var reloaded = await db.Bookings.Include(b => b.CleaningSelections).SingleAsync(b => b.Id == bookingId);
        Assert.True(reloaded.MainBuildingCleaningPurchased);
        var selection = Assert.Single(reloaded.CleaningSelections);
        Assert.Equal(r.HuvudZone.Id, selection.CleaningZoneId);
        Assert.Equal("Huvudbyggnad", selection.ZoneNameSnapshot);
        Assert.Equal(2000m, selection.AmountExVatSnapshot);
    }

    [Fact]
    public async Task Submit_ConferenceChosen_WithoutConferenceFacility_IsIgnored()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var r = await SeedReferenceDataAsync(db);
        // No Konferensrum facility selected on this booking.
        var (bookingId, token) = await SeedApprovedBookingAsync(provider, db, bookings, [r.Blido]);

        var result = await bookings.SubmitCompletionAsync(
            token, Form(main: false, sleeping: false, conference: true), DateTime.UtcNow);
        Assert.Equal(CompletionSubmitOutcome.Success, result.Outcome);

        var reloaded = await db.Bookings.Include(b => b.CleaningSelections).SingleAsync(b => b.Id == bookingId);
        Assert.Null(reloaded.ConferenceCleaningPurchased); // not applicable → never honored, even though posted true
        Assert.Empty(reloaded.CleaningSelections);
    }

    [Fact]
    public async Task Submit_ConferenceChosen_WithConferenceFacilityBooked_SetsFlagAndSnapshotsZone()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var r = await SeedReferenceDataAsync(db);
        var (bookingId, token) = await SeedApprovedBookingAsync(provider, db, bookings, [r.Blido], withFacility: r);

        var result = await bookings.SubmitCompletionAsync(
            token, Form(main: false, sleeping: false, conference: true), DateTime.UtcNow);
        Assert.Equal(CompletionSubmitOutcome.Success, result.Outcome);

        var reloaded = await db.Bookings.Include(b => b.CleaningSelections).SingleAsync(b => b.Id == bookingId);
        Assert.True(reloaded.ConferenceCleaningPurchased);
        var selection = Assert.Single(reloaded.CleaningSelections);
        Assert.Equal(r.KonferensZone.Id, selection.CleaningZoneId);
    }

    [Fact]
    public async Task Submit_SleepingChosen_ResolvesZoneFromTheAssignedBuilding()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var r = await SeedReferenceDataAsync(db);
        var (bookingId, token) = await SeedApprovedBookingAsync(provider, db, bookings, [r.Blido]);

        var result = await bookings.SubmitCompletionAsync(
            token, Form(main: false, sleeping: true, conference: false),
            DateTime.UtcNow);
        Assert.Equal(CompletionSubmitOutcome.Success, result.Outcome);

        var reloaded = await db.Bookings.Include(b => b.CleaningSelections).SingleAsync(b => b.Id == bookingId);
        Assert.True(reloaded.SleepingCleaningPurchased);
        var selection = Assert.Single(reloaded.CleaningSelections);
        Assert.Equal(r.LillaZone.Id, selection.CleaningZoneId);
    }

    [Fact]
    public async Task Submit_SleepingNotChosen_NoZoneEvenWithABuildingAssigned()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var r = await SeedReferenceDataAsync(db);
        var (bookingId, token) = await SeedApprovedBookingAsync(provider, db, bookings, [r.Blido]);

        var result = await bookings.SubmitCompletionAsync(
            token, Form(main: false, sleeping: false, conference: false),
            DateTime.UtcNow);
        Assert.Equal(CompletionSubmitOutcome.Success, result.Outcome);

        var reloaded = await db.Bookings.Include(b => b.CleaningSelections).SingleAsync(b => b.Id == bookingId);
        Assert.False(reloaded.SleepingCleaningPurchased);
        Assert.Empty(reloaded.CleaningSelections);
    }

    [Fact]
    public async Task Submit_TwoUnitsInSamePhysicalBuilding_ResolvesToOneZoneRow()
    {
        // Blidö and Tjockö are two different lodging Buildings but share Lilla bostadslängan — the
        // "one row per physical building, never per lodging unit" rule (§5.5, §8.12). Both must be
        // assigned or this asserts nothing.
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var r = await SeedReferenceDataAsync(db);
        var (bookingId, token) = await SeedApprovedBookingAsync(provider, db, bookings, [r.Blido, r.Tjocko]);

        var result = await bookings.SubmitCompletionAsync(
            token,
            Form(main: false, sleeping: true, conference: false), DateTime.UtcNow);
        Assert.Equal(CompletionSubmitOutcome.Success, result.Outcome);

        var reloaded = await db.Bookings.Include(b => b.CleaningSelections).SingleAsync(b => b.Id == bookingId);
        var selection = Assert.Single(reloaded.CleaningSelections); // one row, not two
        Assert.Equal(r.LillaZone.Id, selection.CleaningZoneId);
    }

    [Fact]
    public async Task RemovingTheAssignmentAfterCompletion_RemovesTheSleepingZone()
    {
        // Replaces AdminAssignsRoomAfterCompletion_ResolvesSleepingZone, whose premise no longer
        // exists: rooms were assigned after the guest submitted, so a booking could sit completed
        // with nothing to resolve from. Buildings are assigned before approval (FR-103/D1), so the
        // zone is known at submission. The behaviour still worth pinning is the inverse — the
        // resolution follows the assignment when it *changes* (D5), including down to nothing.
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var r = await SeedReferenceDataAsync(db);
        var (bookingId, token) = await SeedApprovedBookingAsync(provider, db, bookings, [r.Blido], overnightCount: 1);

        await bookings.SubmitCompletionAsync(
            token, Form(main: false, sleeping: true, conference: false, participants: 1), DateTime.UtcNow);

        var afterSubmit = await db.Bookings.Include(b => b.CleaningSelections).SingleAsync(b => b.Id == bookingId);
        Assert.Equal(r.LillaZone.Id, Assert.Single(afterSubmit.CleaningSelections).CleaningZoneId);

        var cleared = await bookings.SetBuildingAssignmentsAsync(bookingId, []);
        Assert.True(cleared.Succeeded);

        db.ChangeTracker.Clear();
        var afterClear = await db.Bookings.Include(b => b.CleaningSelections).SingleAsync(b => b.Id == bookingId);
        Assert.Empty(afterClear.CleaningSelections);
    }

    [Fact]
    public async Task ReassigningToADifferentPhysicalBuilding_SwapsTheZoneSelection()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var r = await SeedReferenceDataAsync(db);
        var (bookingId, token) = await SeedApprovedBookingAsync(provider, db, bookings, [r.Blido], overnightCount: 1);

        await bookings.SubmitCompletionAsync(
            token, Form(main: false, sleeping: true, conference: false, participants: 1), DateTime.UtcNow);

        var afterSubmit = await db.Bookings.Include(b => b.CleaningSelections).SingleAsync(b => b.Id == bookingId);
        Assert.Equal(r.LillaZone.Id, Assert.Single(afterSubmit.CleaningSelections).CleaningZoneId);

        // Move from Lilla (Blidö) to Stora (Vätö 1) — the old row must go, not accumulate.
        var moved = await bookings.SetBuildingAssignmentsAsync(bookingId, [r.Vato1.Id]);
        Assert.True(moved.Succeeded);

        db.ChangeTracker.Clear();
        var afterMove = await db.Bookings.Include(b => b.CleaningSelections).SingleAsync(b => b.Id == bookingId);
        Assert.Equal(r.StoraZone.Id, Assert.Single(afterMove.CleaningSelections).CleaningZoneId);
    }
}
