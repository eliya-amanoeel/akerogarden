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

        var occasions = MealOccasionResolver.Resolve((await db.Bookings.FindAsync(booking.Id))!);
        var form = new CompletionFormModel
        {
            ArrivalDinner = true,
            DepartureBreakfast = true,
            MealChoices = occasions.AllSittings
                .Select(s => (string?)(s.MealType == MealType.Lunch ? lunch.Id : dinner.Id).ToString())
                .ToList(),
        };
        for (var i = 0; i < 2; i++)
            form.Overnight.Add(new OvernightParticipantEntry
            {
                FullName = $"Deltagare {i + 1}",
                AgeCategory = AgeCategory.Adult,
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

    /// <summary>Reads the roster into edit models, exactly as the admin page does.</summary>
    private static List<ParticipantEdit> ToParticipantEdits(Booking b) =>
        b.Participants.Select(p => new ParticipantEdit
        {
            Id = p.Id,
            FullName = p.FullName,
            AgeCategory = p.AgeCategory,
            Allergies = [.. p.Allergies],
            AllergyNotes = p.AllergyNotes,
        }).ToList();

    /// <summary>Reads the group's meals into an edit model, exactly as the admin page does: one
    /// entry per meal the group is having, whether or not a dish has been chosen for it.</summary>
    private static GroupMealEdit ToMealEdit(Booking b) => new()
    {
        Meals = MealOccasionResolver.Resolve(b).SelectionSlots
            .Select(slot => new MealChoiceEdit
            {
                Date = slot.Date,
                MealType = slot.MealType,
                MealOptionId = b.MealSelections
                    .FirstOrDefault(m => m.Date == slot.Date && m.MealType == slot.MealType)?.MealOptionId,
            })
            .ToList(),
        ArrivalBreakfast = b.ArrivalBreakfast,
        ArrivalLunch = b.ArrivalLunch,
        ArrivalDinner = b.ArrivalDinner,
        DepartureBreakfast = b.DepartureBreakfast,
        DepartureLunch = b.DepartureLunch,
        DepartureDinner = b.DepartureDinner,
        DayGuestBreakfast = b.DayGuestBreakfast,
        DayGuestLunch = b.DayGuestLunch,
        DayGuestDinner = b.DayGuestDinner,
        VegetarianCount = b.VegetarianCount,
        VeganCount = b.VeganCount,
    };

    [Fact]
    public async Task SaveRosterAndMeals_UpdatesBoth_NoStatusChange()
    {
        var provider = NewProvider();
        var setup = await SetupAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            var booking = await service.GetWithParticipantsAsync(setup.BookingId);
            var target = booking!.Participants.OrderBy(p => p.FullName).First();

            var participantEdits = ToParticipantEdits(booking);
            var participantEdit = participantEdits.First(e => e.Id == target.Id);
            participantEdit.FullName = "Ändrat Namn";
            participantEdit.AgeCategory = AgeCategory.Child;
            participantEdit.Allergies = [Allergy.Lactose];
            await service.SaveParticipantRosterAsync(setup.BookingId, participantEdits);

            // Change the group's dinner on one day to the alternative dinner option.
            var mealEdit = ToMealEdit(booking);
            var dinner = booking.MealSelections.First(m => m.MealType == MealType.Dinner);
            mealEdit.Meals.First(m => m.Date == dinner.Date && m.MealType == dinner.MealType).MealOptionId = setup.DinnerId;
            await service.SaveGroupMealsAsync(setup.BookingId, mealEdit);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings
                .Include(b => b.Participants)
                .Include(b => b.MealSelections)
                .SingleAsync(b => b.Id == setup.BookingId);

            Assert.Equal(BookingStatus.DetailsComplete, booking.Status); // unchanged (C10)
            var edited = booking.Participants.Single(p => p.FullName == "Ändrat Namn");
            Assert.Equal(AgeCategory.Child, edited.AgeCategory);
            Assert.Equal([Allergy.Lactose], edited.Allergies);
            Assert.Contains(booking.MealSelections,
                m => m.MealOptionId == setup.DinnerId && m.MealOptionSnapshot == "Burgare");
        }
    }

    [Fact]
    public async Task SaveGroupMeals_DroppingAnEdgeDayMeal_RemovesItsSelection()
    {
        var provider = NewProvider();
        var setup = await SetupAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            var booking = await service.GetWithParticipantsAsync(setup.BookingId);

            // The group no longer eats dinner on the arrival day. Its chosen dish must go with it —
            // otherwise the Matlista keeps cooking for a sitting nobody attends.
            var edit = ToMealEdit(booking!);
            edit.ArrivalDinner = false;

            await service.SaveGroupMealsAsync(setup.BookingId, edit);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings
                .Include(b => b.MealSelections)
                .SingleAsync(b => b.Id == setup.BookingId);

            Assert.False(booking.ArrivalDinner);
            Assert.DoesNotContain(booking.MealSelections, m => m.Date == new DateOnly(2026, 8, 10));
            // The middle-day sittings are untouched.
            Assert.Equal(2, booking.MealSelections.Count);
        }
    }

    [Fact]
    public async Task SaveGroupMeals_AddingAnEdgeDayMeal_LeavesItUnchosen_ThenTheDishCanBeSet()
    {
        var provider = NewProvider();
        var setup = await SetupAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            var booking = await service.GetWithParticipantsAsync(setup.BookingId);

            var edit = ToMealEdit(booking!);
            edit.DepartureLunch = true;

            await service.SaveGroupMealsAsync(setup.BookingId, edit);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings
                .Include(b => b.MealSelections)
                .SingleAsync(b => b.Id == setup.BookingId);

            Assert.True(booking.DepartureLunch);
            // The new meal exists with no dish — nothing is invented on the group's behalf.
            Assert.DoesNotContain(booking.MealSelections, m => m.Date == new DateOnly(2026, 8, 12));
            Assert.Equal(3, booking.MealSelections.Count);
        }

        // …and the admin can then choose its dish, which is the whole point: keying the edit by row
        // id meant a meal added this way could never be given one.
        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            var booking = await service.GetWithParticipantsAsync(setup.BookingId);

            var edit = ToMealEdit(booking!);
            var newLunch = Assert.Single(edit.Meals, m => m.Date == new DateOnly(2026, 8, 12) && m.MealType == MealType.Lunch);
            Assert.Null(newLunch.MealOptionId); // rendered as an empty dropdown, not omitted
            newLunch.MealOptionId = setup.LunchId;

            await service.SaveGroupMealsAsync(setup.BookingId, edit);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings
                .Include(b => b.MealSelections)
                .SingleAsync(b => b.Id == setup.BookingId);

            var lunch = Assert.Single(booking.MealSelections, m => m.Date == new DateOnly(2026, 8, 12));
            Assert.Equal(setup.LunchId, lunch.MealOptionId);
            Assert.False(string.IsNullOrEmpty(lunch.MealOptionSnapshot)); // snapshot captured (NFR-15)
            Assert.Equal(4, booking.MealSelections.Count);
        }
    }

    [Fact]
    public async Task SaveGroupMeals_AnUnchosenDropdown_DoesNotWipeADishAlreadyAgreed()
    {
        // The admin opens the section, changes something else, and saves without touching a meal
        // whose dropdown happens to post empty. That must not silently delete what the guest chose.
        var provider = NewProvider();
        var setup = await SetupAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            var booking = await service.GetWithParticipantsAsync(setup.BookingId);

            var edit = ToMealEdit(booking!);
            foreach (var meal in edit.Meals) meal.MealOptionId = null;
            edit.VegetarianCount = 2;

            await service.SaveGroupMealsAsync(setup.BookingId, edit);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings
                .Include(b => b.MealSelections)
                .SingleAsync(b => b.Id == setup.BookingId);

            Assert.Equal(2, booking.VegetarianCount);
            Assert.Equal(3, booking.MealSelections.Count); // every agreed dish survives
            Assert.All(booking.MealSelections, m => Assert.False(string.IsNullOrEmpty(m.MealOptionSnapshot)));
        }
    }

    [Fact]
    public async Task SaveStatedTimes_UpdatesOnlyWhatWasPosted()
    {
        var provider = NewProvider();
        var setup = await SetupAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            // Only the arrival time is posted; the departure one is left out entirely.
            await service.SaveStatedTimesAsync(setup.BookingId, "09:30", null);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings.SingleAsync(b => b.Id == setup.BookingId);

            Assert.Equal(new TimeOnly(9, 30), booking.CheckInTime);
            // An omitted value leaves the stored one alone rather than resetting it to the standard.
            Assert.Equal(MealOccasionResolver.StandardCheckOutTime, booking.CheckOutTime);
        }
    }
}
