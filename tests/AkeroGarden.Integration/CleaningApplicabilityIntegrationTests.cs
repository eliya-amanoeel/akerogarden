using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Models;
using AkeroGarden.Web.Services;
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
            s.AddScoped<RoomAvailabilityService>();
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
            Id = Guid.NewGuid(), Name = "Huvudbyggnad", Kind = CleaningZoneKind.MainBuilding,
            PhysicalBuildingId = huvud.Id, AmountExVat = 2000m, VatRate = 0.25m, IsActive = true,
        };
        var lillaZone = new CleaningZone
        {
            Id = Guid.NewGuid(), Name = "Lilla bostadslängan", Kind = CleaningZoneKind.SleepingBuilding,
            PhysicalBuildingId = lilla.Id, AmountExVat = 2000m, VatRate = 0.25m, IsActive = true,
        };
        var storaZone = new CleaningZone
        {
            Id = Guid.NewGuid(), Name = "Stora bostadslängan", Kind = CleaningZoneKind.SleepingBuilding,
            PhysicalBuildingId = stora.Id, AmountExVat = 2000m, VatRate = 0.25m, IsActive = true,
        };
        var konferensZone = new CleaningZone
        {
            Id = Guid.NewGuid(), Name = "Konferensbyggnad", Kind = CleaningZoneKind.Conference,
            PhysicalBuildingId = konferens.Id, AmountExVat = 2000m, VatRate = 0.25m, IsActive = true,
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
            Id = Guid.NewGuid(), Name = "Konferensrum", PhysicalBuildingId = konferens.Id,
            AmountExVat = 1200m, VatRate = 0.25m, IsActive = true,
        };
        db.Facilities.Add(konferensrum);

        await db.SaveChangesAsync();

        return new Ref(huvud, lilla, stora, konferens, huvudZone, lillaZone, storaZone, konferensZone,
            blido, tjocko, vato1, blidoRum1, tjockoRum1, vato1Rum1, konferensrum);
    }

    private static async Task<(Guid BookingId, string Token)> SeedApprovedBookingAsync(
        IServiceProvider provider, ApplicationDbContext db, BookingService bookings,
        bool guestChoosesRooms, Ref? withFacility = null, int overnightCount = 2)
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
            AcceptPrivacyPolicy = true,
        };
        var booking = await bookings.CreateStayRequestAsync(input, createdByAdmin: true);
        await bookings.ApproveAsync(booking.Id);

        var persisted = await db.Bookings.FindAsync(booking.Id);
        persisted!.GuestChoosesRooms = guestChoosesRooms;
        await db.SaveChangesAsync();

        return (booking.Id, persisted.CompletionToken!);
    }

    private static CompletionFormModel Form(
        bool main, bool sleeping, bool conference, params string?[] roomChoices)
    {
        var form = new CompletionFormModel
        {
            MainBuildingCleaning = main,
            SleepingCleaning = sleeping,
            ConferenceCleaning = conference,
        };
        foreach (var choice in roomChoices)
        {
            form.Overnight.Add(new OvernightParticipantEntry
            {
                FullName = $"Deltagare {form.Overnight.Count + 1}",
                AgeCategory = AgeCategory.Adult,
                IsFullBoard = false,
                RoomChoice = choice,
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
        var (bookingId, token) = await SeedApprovedBookingAsync(provider, db, bookings, guestChoosesRooms: false);

        var result = await bookings.SubmitCompletionAsync(
            token, Form(main: true, sleeping: false, conference: false, roomChoices: [null, null]), DateTime.UtcNow);
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
        await SeedReferenceDataAsync(db);
        // No Konferensrum facility selected on this booking.
        var (bookingId, token) = await SeedApprovedBookingAsync(provider, db, bookings, guestChoosesRooms: false);

        var result = await bookings.SubmitCompletionAsync(
            token, Form(main: false, sleeping: false, conference: true, roomChoices: [null, null]), DateTime.UtcNow);
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
        var (bookingId, token) = await SeedApprovedBookingAsync(provider, db, bookings, guestChoosesRooms: false, withFacility: r);

        var result = await bookings.SubmitCompletionAsync(
            token, Form(main: false, sleeping: false, conference: true, roomChoices: [null, null]), DateTime.UtcNow);
        Assert.Equal(CompletionSubmitOutcome.Success, result.Outcome);

        var reloaded = await db.Bookings.Include(b => b.CleaningSelections).SingleAsync(b => b.Id == bookingId);
        Assert.True(reloaded.ConferenceCleaningPurchased);
        var selection = Assert.Single(reloaded.CleaningSelections);
        Assert.Equal(r.KonferensZone.Id, selection.CleaningZoneId);
    }

    [Fact]
    public async Task Submit_SleepingChosen_WithRoomAtCompletion_ResolvesZoneImmediately()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var r = await SeedReferenceDataAsync(db);
        var (bookingId, token) = await SeedApprovedBookingAsync(provider, db, bookings, guestChoosesRooms: true);

        var result = await bookings.SubmitCompletionAsync(
            token, Form(main: false, sleeping: true, conference: false, roomChoices: [r.BlidoRum1.Id.ToString(), null]),
            DateTime.UtcNow);
        Assert.Equal(CompletionSubmitOutcome.Success, result.Outcome);

        var reloaded = await db.Bookings.Include(b => b.CleaningSelections).SingleAsync(b => b.Id == bookingId);
        Assert.True(reloaded.SleepingCleaningPurchased);
        var selection = Assert.Single(reloaded.CleaningSelections);
        Assert.Equal(r.LillaZone.Id, selection.CleaningZoneId);
    }

    [Fact]
    public async Task Submit_SleepingNotChosen_NoZoneEvenWithRoomAssigned()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var r = await SeedReferenceDataAsync(db);
        var (bookingId, token) = await SeedApprovedBookingAsync(provider, db, bookings, guestChoosesRooms: true);

        var result = await bookings.SubmitCompletionAsync(
            token, Form(main: false, sleeping: false, conference: false, roomChoices: [r.BlidoRum1.Id.ToString(), null]),
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
        // "one row per physical building, never per room/unit" rule (§5.5, §8.12).
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var r = await SeedReferenceDataAsync(db);
        var (bookingId, token) = await SeedApprovedBookingAsync(provider, db, bookings, guestChoosesRooms: true);

        var result = await bookings.SubmitCompletionAsync(
            token,
            Form(main: false, sleeping: true, conference: false,
                roomChoices: [r.BlidoRum1.Id.ToString(), r.TjockoRum1.Id.ToString()]),
            DateTime.UtcNow);
        Assert.Equal(CompletionSubmitOutcome.Success, result.Outcome);

        var reloaded = await db.Bookings.Include(b => b.CleaningSelections).SingleAsync(b => b.Id == bookingId);
        var selection = Assert.Single(reloaded.CleaningSelections); // one row, not two
        Assert.Equal(r.LillaZone.Id, selection.CleaningZoneId);
    }

    [Fact]
    public async Task AdminAssignsRoomAfterCompletion_ResolvesSleepingZone()
    {
        // GuestChoosesRooms=false: the guest completes without a room, the admin assigns it
        // afterward — the sleeping-zone selection must appear only once that assignment happens.
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var r = await SeedReferenceDataAsync(db);
        var (bookingId, token) = await SeedApprovedBookingAsync(provider, db, bookings, guestChoosesRooms: false, overnightCount: 1);

        await bookings.SubmitCompletionAsync(
            token, Form(main: false, sleeping: true, conference: false, roomChoices: [null]), DateTime.UtcNow);

        var afterSubmit = await db.Bookings.Include(b => b.CleaningSelections).SingleAsync(b => b.Id == bookingId);
        Assert.Empty(afterSubmit.CleaningSelections); // no room yet → nothing to resolve

        var participant = await db.Participants.SingleAsync(p => p.BookingId == bookingId);
        await bookings.SaveParticipantEditsAsync(bookingId, [new ParticipantEdit
        {
            Id = participant.Id,
            FullName = participant.FullName,
            AgeCategory = participant.AgeCategory,
            RoomId = r.Vato1Rum1.Id,
        }]);

        var afterAssign = await db.Bookings.Include(b => b.CleaningSelections).SingleAsync(b => b.Id == bookingId);
        var selection = Assert.Single(afterAssign.CleaningSelections);
        Assert.Equal(r.StoraZone.Id, selection.CleaningZoneId);
    }

    [Fact]
    public async Task AdminReassignsRoomToDifferentPhysicalBuilding_SwapsTheZoneSelection()
    {
        var provider = NewProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var r = await SeedReferenceDataAsync(db);
        var (bookingId, token) = await SeedApprovedBookingAsync(provider, db, bookings, guestChoosesRooms: true, overnightCount: 1);

        await bookings.SubmitCompletionAsync(
            token, Form(main: false, sleeping: true, conference: false, roomChoices: [r.BlidoRum1.Id.ToString()]),
            DateTime.UtcNow);

        var afterSubmit = await db.Bookings.Include(b => b.CleaningSelections).SingleAsync(b => b.Id == bookingId);
        Assert.Equal(r.LillaZone.Id, Assert.Single(afterSubmit.CleaningSelections).CleaningZoneId);

        var participant = await db.Participants.SingleAsync(p => p.BookingId == bookingId);
        await bookings.SaveParticipantEditsAsync(bookingId, [new ParticipantEdit
        {
            Id = participant.Id,
            FullName = participant.FullName,
            AgeCategory = participant.AgeCategory,
            RoomId = r.Vato1Rum1.Id, // move from Lilla (Blidö) to Stora (Vätö 1)
        }]);

        var afterMove = await db.Bookings.Include(b => b.CleaningSelections).SingleAsync(b => b.Id == bookingId);
        var selection = Assert.Single(afterMove.CleaningSelections);
        Assert.Equal(r.StoraZone.Id, selection.CleaningZoneId); // old Lilla row is gone, Stora row replaced it
    }
}
