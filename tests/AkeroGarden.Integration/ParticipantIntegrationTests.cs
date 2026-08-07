using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the completion-flow data foundation against real PostgreSQL:
/// Participant/BookingMealSelection persistence (string enums, the multi-select allergy converter,
/// snapshot fields — NFR-15/16), and the MealOption ← BookingMealSelection hard-delete guard
/// (FR-26), which survived meal selection moving from participants to bookings. The Room/Building guards moved to
/// <c>ReferenceDataDeleteGuardIntegrationTests</c> when they were repointed away from participant
/// room assignment. Tests use unique names because the fixture's database is shared across the class.
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
    public async Task Participant_PersistsEnumsAsStrings_AndRoundTripsAllergyList()
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
            Allergies = [Allergy.Lactose, Allergy.Nut],
            AllergyNotes = "Inga jordnötter tack",
        };
        db.Participants.Add(participant);
        await db.SaveChangesAsync();

        // Reload in a fresh context to prove it survives the round trip, not the tracker.
        var (_, _, _, db2) = NewScope(out var scope2);
        using var _s2 = scope2;
        var reloaded = await db2.Participants.SingleAsync(p => p.Id == participant.Id);

        Assert.Equal(ParticipantType.Overnight, reloaded.ParticipantType);
        Assert.Equal(AgeCategory.Child, reloaded.AgeCategory);
        Assert.Equal(new[] { Allergy.Lactose, Allergy.Nut }, reloaded.Allergies);
        Assert.Equal("Inga jordnötter tack", reloaded.AllergyNotes);
    }

    [Fact]
    public async Task Participant_StoresEnumsAndAllergiesAsStrings_InDatabase()
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
            Allergies = [Allergy.Gluten],
        };
        db.Participants.Add(participant);
        await db.SaveChangesAsync();

        var raw = await db.Database
            .SqlQuery<ParticipantRaw>($"""
                SELECT "ParticipantType" AS "Type", "AgeCategory" AS "Age", "Allergies" AS "Diet"
                FROM "Participants" WHERE "Id" = {participant.Id}
                """)
            .SingleAsync();

        Assert.Equal("DayGuest", raw.Type);
        Assert.Equal("Adult", raw.Age);
        Assert.Equal("Gluten", raw.Diet);
    }

    [Fact]
    public async Task BookingMealSelection_PersistsSnapshotAndMealTypeAsString()
    {
        var (_, _, meals, db) = NewScope(out var scope);
        using var _s = scope;

        var booking = NewStayBooking();
        db.Bookings.Add(booking);
        var option = new MealOption { Name = $"Fisk-{Guid.NewGuid():N}", MealType = MealType.Lunch };
        await meals.CreateAsync(option);

        booking.MealSelections.Add(new BookingMealSelection
        {
            Date = new DateOnly(2026, 8, 11),
            MealType = MealType.Lunch,
            MealOptionId = option.Id,
            MealOptionSnapshot = option.Name,
        });
        await db.SaveChangesAsync();

        var reloaded = await db.BookingMealSelections.SingleAsync(m => m.BookingId == booking.Id);
        Assert.Equal(MealType.Lunch, reloaded.MealType);
        Assert.Equal(option.Name, reloaded.MealOptionSnapshot);
    }

    [Fact]
    public async Task BookingMealSelection_RejectsASecondChoiceForTheSameSitting()
    {
        // One dish per sitting for the whole group (FR-105) — enforced by a unique index, not only
        // by the normalizer, so a duplicate cannot exist however it was written.
        var (_, _, meals, db) = NewScope(out var scope);
        using var _s = scope;

        var booking = NewStayBooking();
        db.Bookings.Add(booking);
        var option = new MealOption { Name = $"Fisk-{Guid.NewGuid():N}", MealType = MealType.Lunch };
        await meals.CreateAsync(option);

        var date = new DateOnly(2026, 8, 11);
        for (var i = 0; i < 2; i++)
            booking.MealSelections.Add(new BookingMealSelection
            {
                Date = date,
                MealType = MealType.Lunch,
                MealOptionId = option.Id,
                MealOptionSnapshot = option.Name,
            });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ── Guards (FR-16 / FR-20 / FR-26) ───────────────────────────────────────────

    [Fact]
    public async Task MealOption_HardDelete_Blocked_WhenReferencedByABookingMealSelection()
    {
        var (_, _, meals, db) = NewScope(out var scope);
        using var _s = scope;

        var booking = NewStayBooking();
        db.Bookings.Add(booking);
        var option = new MealOption { Name = $"Korv-{Guid.NewGuid():N}", MealType = MealType.Dinner };
        await meals.CreateAsync(option);

        booking.MealSelections.Add(new BookingMealSelection
        {
            Date = new DateOnly(2026, 8, 10),
            MealType = MealType.Dinner,
            MealOptionId = option.Id,
            MealOptionSnapshot = option.Name,
        });
        await db.SaveChangesAsync();

        Assert.Equal(HardDeleteResult.Blocked, await meals.TryHardDeleteAsync(option.Id));

        // Removing the reference frees the option for deletion.
        db.BookingMealSelections.RemoveRange(
            db.BookingMealSelections.Where(m => m.MealOptionId == option.Id));
        await db.SaveChangesAsync();
        Assert.Equal(HardDeleteResult.Deleted, await meals.TryHardDeleteAsync(option.Id));
    }

    // REMOVED 2026-08-06 (demo-feedback slice 2): Room_HardDelete_Blocked_WhenReferencedByParticipant
    // and Building_HardDelete_Blocked_WhenRoomHasAssignedParticipant. Both asserted that a
    // participant's room assignment blocks a hard-delete — behaviour that no longer exists, since
    // participants no longer reference rooms (FR-56 tombstoned). Deleted rather than adjusted: there
    // is no weaker version of these that would still be true.
    //
    // Coverage is not lost. The guards they covered were repointed, not dropped (FR-20 at
    // blocked-period integrity, FR-16 additionally at booking building assignments), and are covered
    // against real referencing rows in ReferenceDataDeleteGuardIntegrationTests.

    private record ParticipantRaw(string Type, string Age, string Diet);
}
