using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the Phase 4 completion-flow data foundation against real PostgreSQL:
/// Participant/MealPreference persistence (string enums, the multi-select dietary converter,
/// snapshot fields — NFR-15/16), and the three hard-delete guards now that their referents exist —
/// MealOption ← MealPreference (FR-26), Room ← Participant.RoomId (FR-20), Building's transitive
/// check (FR-16). Tests use unique names because the fixture's database is shared across the class.
/// </summary>
public class ParticipantIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public ParticipantIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private (BuildingService buildings, RoomService rooms, MealOptionService meals, ApplicationDbContext db)
        NewScope(out IServiceScope scope)
    {
        var services = _fixture.BuildServices(s =>
        {
            s.AddScoped<BuildingService>();
            s.AddScoped<RoomService>();
            s.AddScoped<MealOptionService>();
        });
        scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (
            scope.ServiceProvider.GetRequiredService<BuildingService>(),
            scope.ServiceProvider.GetRequiredService<RoomService>(),
            scope.ServiceProvider.GetRequiredService<MealOptionService>(),
            db);
    }

    // BookingReference (FR-92) carries a unique index; the class shares one database across every
    // [Fact], so each booking needs a distinct reference even though these tests don't exercise
    // the reference itself. A Guid-suffixed placeholder is enough (unlike BookingService, this
    // doesn't need to wait for the DB-assigned SequenceNumber to produce the real AG-YYYY-NNNN form).
    private static Booking NewStayBooking() => new()
    {
        Id = Guid.NewGuid(),
        BookingReference = $"AG-TEST-{Guid.NewGuid():N}"[..20],
        BookingType = BookingType.StayRequest,
        Status = BookingStatus.Approved,
        ContactName = "Anna Andersson",
        ContactEmail = $"anna-{Guid.NewGuid():N}@example.com",
        CheckIn = new DateOnly(2026, 8, 10),
        CheckOut = new DateOnly(2026, 8, 12),
        OvernightGuestCount = 2,
        MealPlan = MealPlan.FullBoardAll,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    // ── Persistence ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Participant_PersistsEnumsAsStrings_AndRoundTripsDietaryList()
    {
        var (_, _, _, db) = NewScope(out var scope);
        using var _s = scope;

        var booking = NewStayBooking();
        db.Bookings.Add(booking);
        var participant = new Participant
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            FullName = "Bertil Berg",
            ParticipantType = ParticipantType.Overnight,
            AgeCategory = AgeCategory.Child,
            IsFullBoard = true,
            AttendsBreakfast = true,
            DietaryRequirements = [DietaryRequirement.Vegetarian, DietaryRequirement.NutAllergy],
            DietaryNotes = "Inga jordnötter tack",
        };
        db.Participants.Add(participant);
        await db.SaveChangesAsync();

        // Reload in a fresh context to prove it survives the round trip, not the tracker.
        var (_, _, _, db2) = NewScope(out var scope2);
        using var _s2 = scope2;
        var reloaded = await db2.Participants.SingleAsync(p => p.Id == participant.Id);

        Assert.Equal(ParticipantType.Overnight, reloaded.ParticipantType);
        Assert.Equal(AgeCategory.Child, reloaded.AgeCategory);
        Assert.Equal(
            new[] { DietaryRequirement.Vegetarian, DietaryRequirement.NutAllergy },
            reloaded.DietaryRequirements);
        Assert.Equal("Inga jordnötter tack", reloaded.DietaryNotes);
    }

    [Fact]
    public async Task Participant_StoresEnumsAndDietaryAsStrings_InDatabase()
    {
        var (_, _, _, db) = NewScope(out var scope);
        using var _s = scope;

        var booking = NewStayBooking();
        db.Bookings.Add(booking);
        var participant = new Participant
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            FullName = "Cecilia Cederberg",
            ParticipantType = ParticipantType.DayGuest,
            AgeCategory = AgeCategory.Adult,
            AttendanceDate = new DateOnly(2026, 8, 11),
            DietaryRequirements = [DietaryRequirement.Vegan],
        };
        db.Participants.Add(participant);
        await db.SaveChangesAsync();

        var raw = await db.Database
            .SqlQuery<ParticipantRaw>($"""
                SELECT "ParticipantType" AS "Type", "AgeCategory" AS "Age", "DietaryRequirements" AS "Diet"
                FROM "Participants" WHERE "Id" = {participant.Id}
                """)
            .SingleAsync();

        Assert.Equal("DayGuest", raw.Type);
        Assert.Equal("Adult", raw.Age);
        Assert.Equal("Vegan", raw.Diet);
    }

    [Fact]
    public async Task MealPreference_PersistsSnapshotAndMealTypeAsString()
    {
        var (_, _, meals, db) = NewScope(out var scope);
        using var _s = scope;

        var booking = NewStayBooking();
        db.Bookings.Add(booking);
        var option = new MealOption { Name = $"Fisk-{Guid.NewGuid():N}", MealType = MealType.Lunch };
        await meals.CreateAsync(option);

        var participant = new Participant
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            FullName = "David Dahl",
            ParticipantType = ParticipantType.Overnight,
            AgeCategory = AgeCategory.Adult,
            IsFullBoard = true,
            AttendsBreakfast = true,
        };
        participant.MealPreferences.Add(new MealPreference
        {
            Id = Guid.NewGuid(),
            Date = new DateOnly(2026, 8, 11),
            MealType = MealType.Lunch,
            MealOptionId = option.Id,
            MealOptionSnapshot = option.Name,
        });
        db.Participants.Add(participant);
        await db.SaveChangesAsync();

        var reloaded = await db.MealPreferences.SingleAsync(m => m.ParticipantId == participant.Id);
        Assert.Equal(MealType.Lunch, reloaded.MealType);
        Assert.Equal(option.Name, reloaded.MealOptionSnapshot);
    }

    // ── Guards (FR-16 / FR-20 / FR-26) ───────────────────────────────────────────

    [Fact]
    public async Task MealOption_HardDelete_Blocked_WhenReferencedByMealPreference()
    {
        var (_, _, meals, db) = NewScope(out var scope);
        using var _s = scope;

        var booking = NewStayBooking();
        db.Bookings.Add(booking);
        var option = new MealOption { Name = $"Korv-{Guid.NewGuid():N}", MealType = MealType.Dinner };
        await meals.CreateAsync(option);

        var participant = new Participant
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            FullName = "Eva Ek",
            ParticipantType = ParticipantType.Overnight,
            AgeCategory = AgeCategory.Adult,
            IsFullBoard = true,
        };
        participant.MealPreferences.Add(new MealPreference
        {
            Id = Guid.NewGuid(),
            Date = new DateOnly(2026, 8, 10),
            MealType = MealType.Dinner,
            MealOptionId = option.Id,
            MealOptionSnapshot = option.Name,
        });
        db.Participants.Add(participant);
        await db.SaveChangesAsync();

        Assert.Equal(HardDeleteResult.Blocked, await meals.TryHardDeleteAsync(option.Id));

        // Removing the reference frees the option for deletion.
        db.MealPreferences.RemoveRange(db.MealPreferences.Where(m => m.MealOptionId == option.Id));
        await db.SaveChangesAsync();
        Assert.Equal(HardDeleteResult.Deleted, await meals.TryHardDeleteAsync(option.Id));
    }

    [Fact]
    public async Task Room_HardDelete_Blocked_WhenReferencedByParticipant()
    {
        var (buildings, rooms, _, db) = NewScope(out var scope);
        using var _s = scope;

        var pb = new PhysicalBuilding { Id = Guid.NewGuid(), Name = $"Lilla-{Guid.NewGuid():N}" };
        db.PhysicalBuildings.Add(pb);
        await db.SaveChangesAsync();

        var building = new Building { Name = $"Blidö-{Guid.NewGuid():N}", PhysicalBuildingId = pb.Id, DisplayOrder = 1 };
        await buildings.TryCreateAsync(building);
        var room = new Room { Name = "Rum 1", BedCount = 4, BuildingId = building.Id, DisplayOrder = 1 };
        await rooms.CreateAsync(room);

        var booking = NewStayBooking();
        db.Bookings.Add(booking);
        db.Participants.Add(new Participant
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            FullName = "Fredrik Falk",
            ParticipantType = ParticipantType.Overnight,
            AgeCategory = AgeCategory.Adult,
            RoomId = room.Id,
            RoomAssignmentSnapshot = $"{room.Name} — {building.Name}",
            IsFullBoard = true,
        });
        await db.SaveChangesAsync();

        Assert.Equal(HardDeleteResult.Blocked, await rooms.TryHardDeleteAsync(room.Id));
    }

    [Fact]
    public async Task Building_HardDelete_Blocked_WhenRoomHasAssignedParticipant()
    {
        var (buildings, rooms, _, db) = NewScope(out var scope);
        using var _s = scope;

        var pb = new PhysicalBuilding { Id = Guid.NewGuid(), Name = $"Lilla-{Guid.NewGuid():N}" };
        db.PhysicalBuildings.Add(pb);
        await db.SaveChangesAsync();

        var building = new Building { Name = $"Ljusterö-{Guid.NewGuid():N}", PhysicalBuildingId = pb.Id, DisplayOrder = 1 };
        await buildings.TryCreateAsync(building);
        var room = new Room { Name = "Rum 1", BedCount = 3, BuildingId = building.Id, DisplayOrder = 1 };
        await rooms.CreateAsync(room);

        var booking = NewStayBooking();
        db.Bookings.Add(booking);
        db.Participants.Add(new Participant
        {
            Id = Guid.NewGuid(),
            BookingId = booking.Id,
            FullName = "Gustav Grön",
            ParticipantType = ParticipantType.Overnight,
            AgeCategory = AgeCategory.Adult,
            RoomId = room.Id,
            IsFullBoard = true,
        });
        await db.SaveChangesAsync();

        Assert.Equal(HardDeleteResult.Blocked, await buildings.TryHardDeleteAsync(building.Id));
    }

    private record ParticipantRaw(string Type, string Age, string Diet);
}
