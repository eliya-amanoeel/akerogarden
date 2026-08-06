using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;
using AkeroGarden.Web.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the admin's post-submission participant editing (Phase 4 slice 6,
/// FR-48): editing participant fields + meal choices with no status change (C10).
/// </summary>
public class AdminParticipantEditIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public AdminParticipantEditIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private IServiceProvider NewProvider() =>
        _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(new FakeEmailService());
            s.AddScoped<BookingEmailService>();
            s.AddScoped<BookingService>();
            s.AddScoped<CleaningZoneService>();
            s.AddScoped<BuildingService>();
            s.AddScoped<RoomService>();
            s.AddScoped<MealOptionService>();
            s.Configure<BookingEmailOptions>(o =>
            {
                o.PublicBaseUrl = "https://akerogarden.se";
                o.AdminBaseUrl = "https://admin.akerogarden.se";
                o.AdminNotificationEmail = "admin@test.se";
                o.ContactEmail = "info@test.se";
            });
        });

    private sealed record Setup(Guid BookingId, Guid RoomId, Guid LunchId, Guid DinnerId);

    /// <summary>Seeds a building + 1-bed room + options, and a DetailsComplete booking with 2 full-board overnight participants.</summary>
    private static async Task<Setup> SetupAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var buildings = scope.ServiceProvider.GetRequiredService<BuildingService>();
        var roomsSvc = scope.ServiceProvider.GetRequiredService<RoomService>();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();

        var physicalBuilding = new PhysicalBuilding { Id = Guid.NewGuid(), Name = $"Lilla-{Guid.NewGuid():N}" };
        db.PhysicalBuildings.Add(physicalBuilding);
        await db.SaveChangesAsync();

        var building = new Building { Name = $"Blidö-{Guid.NewGuid():N}", PhysicalBuildingId = physicalBuilding.Id, DisplayOrder = 1 };
        await buildings.TryCreateAsync(building);
        var room = new Room { Name = "Rum 1", BedCount = 1, BuildingId = building.Id, DisplayOrder = 1 };
        await roomsSvc.CreateAsync(room);

        var lunch = new MealOption { Id = Guid.NewGuid(), Name = "Fisk", MealType = MealType.Lunch, IsActive = true };
        var dinner = new MealOption { Id = Guid.NewGuid(), Name = "Kyckling", MealType = MealType.Dinner, IsActive = true };
        var dinner2 = new MealOption { Id = Guid.NewGuid(), Name = "Burgare", MealType = MealType.Dinner, IsActive = true };
        db.MealOptions.AddRange(lunch, dinner, dinner2);
        await db.SaveChangesAsync();

        var booking = await bookings.CreateStayRequestAsync(new StayRequestInput
        {
            ContactName = "Grupp",
            ContactEmail = $"g-{Guid.NewGuid():N}@test.se",
            CheckIn = new DateOnly(2026, 8, 10),
            CheckOut = new DateOnly(2026, 8, 12),
            OvernightGuestCount = 2,
            MealPlan = MealPlan.FullBoardAll,
            AcceptPrivacyPolicy = true
        }, createdByAdmin: true);
        // Approval requires a lodging assignment (FR-103/D1).
        await BuildingAssignmentSeed.AssignBuildingAsync(db, booking.Id);
        await bookings.ApproveAsync(booking.Id);
        var token = (await db.Bookings.FindAsync(booking.Id))!.CompletionToken!;

        var slots = MealsByDayCalculator.Slots(new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 12));
        var form = new CompletionFormModel();
        for (var i = 0; i < 2; i++)
            form.Overnight.Add(new OvernightParticipantEntry
            {
                FullName = $"Deltagare {i + 1}",
                AgeCategory = AgeCategory.Adult,
                IsFullBoard = true,
                MealChoices = slots.Select(s => (string?)(s.MealType == MealType.Lunch ? lunch.Id : dinner.Id).ToString()).ToList(),
            });
        await bookings.SubmitCompletionAsync(token, form, DateTime.UtcNow);

        return new Setup(booking.Id, room.Id, lunch.Id, dinner2.Id);
    }

    // REMOVED 2026-08-06 (demo-feedback slice 2): SetGuestChoosesRooms_TogglesFlag and
    // GetConflicts_FlagsOverAssignedRoom. The room-choice toggle and room-conflict detection were
    // both deleted with per-participant room assignment (FR-56 tombstoned) — the system cannot
    // over-assign a room it never assigns. Nothing weaker remains to assert.
    //
    // What replaced them is covered elsewhere: the admin now assigns buildings before approval
    // (BuildingAssignmentIntegrationTests), and over-capacity is a non-blocking approval warning
    // measured against the assigned buildings' beds rather than a per-room conflict.

    [Fact]
    public async Task SaveParticipantEdits_UpdatesFields_ChangesMeal_NoStatusChange()
    {
        var provider = NewProvider();
        var setup = await SetupAsync(provider);

        List<ParticipantEdit> edits;
        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            var booking = await service.GetWithParticipantsAsync(setup.BookingId);
            var target = booking!.Participants.OrderBy(p => p.FullName).First();

            edits = booking.Participants.Select(p => new ParticipantEdit
            {
                Id = p.Id,
                FullName = p.FullName,
                AgeCategory = p.AgeCategory,
                Dietary = [.. p.DietaryRequirements],
                Meals = p.MealPreferences.Select(m => new MealEdit { Id = m.Id, MealOptionId = m.MealOptionId }).ToList(),
            }).ToList();

            var edit = edits.First(e => e.Id == target.Id);
            edit.FullName = "Ändrat Namn";
            edit.AgeCategory = AgeCategory.Child;
            edit.Dietary = [DietaryRequirement.Vegan];
            // change one dinner meal to the alternative dinner option
            var dinnerMeal = target.MealPreferences.First(m => m.MealType == MealType.Dinner);
            edit.Meals.First(m => m.Id == dinnerMeal.Id).MealOptionId = setup.DinnerId;

            await service.SaveParticipantEditsAsync(setup.BookingId, edits);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings
                .Include(b => b.Participants).ThenInclude(p => p.MealPreferences)
                .SingleAsync(b => b.Id == setup.BookingId);

            Assert.Equal(BookingStatus.DetailsComplete, booking.Status); // unchanged (C10)
            var edited = booking.Participants.Single(p => p.FullName == "Ändrat Namn");
            Assert.Equal(AgeCategory.Child, edited.AgeCategory);
            Assert.Equal([DietaryRequirement.Vegan], edited.DietaryRequirements);
            Assert.Contains(edited.MealPreferences, m => m.MealOptionId == setup.DinnerId && m.MealOptionSnapshot == "Burgare");
        }
    }
}
