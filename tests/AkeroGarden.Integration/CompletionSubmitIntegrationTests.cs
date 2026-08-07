using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;
using AkeroGarden.Web.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for completion-form submission against real PostgreSQL (FR-55/57/61/62/105,
/// §2.5): a valid submission persists the participant roster, the group's meal selections with
/// snapshots, the edge-day meal occasions and the dietary-preference counts, advances the booking to
/// DetailsComplete, and notifies the admin; a second submission is rejected (single-use); validation
/// failures and expired links persist nothing.
/// </summary>
public class CompletionSubmitIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public CompletionSubmitIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private IServiceProvider NewProvider(out FakeEmailService email)
    {
        var fake = new FakeEmailService();
        email = fake;
        return _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(fake);
            s.AddScoped<BookingEmailService>();
            s.AddScoped<BookingService>();
            s.AddScoped<CleaningZoneService>();
            s.Configure<BookingEmailOptions>(o =>
            {
                o.PublicBaseUrl = "https://akerogarden.se";
                o.AdminBaseUrl = "https://admin.akerogarden.se";
                o.AdminNotificationEmail = "admin@test.se";
                o.ContactEmail = "info@test.se";
            });
        });
    }

    private sealed record Seed(Guid BookingId, string Token, Guid LunchId, Guid DinnerId,
        ResolvedMealOccasions Occasions);

    /// <summary>Seeds active lunch/dinner options + an approved 2-night booking; returns its token and ids.</summary>
    private static async Task<Seed> SeedAsync(
        IServiceProvider provider, int overnightCount = 1, MealPlan mealPlan = MealPlan.FullBoardAll)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<BookingService>();

        var lunch = new MealOption { Id = Guid.NewGuid(), Name = $"Fisk-{Guid.NewGuid():N}", MealType = MealType.Lunch, IsActive = true };
        var dinner = new MealOption { Id = Guid.NewGuid(), Name = $"Kyckling-{Guid.NewGuid():N}", MealType = MealType.Dinner, IsActive = true };
        db.MealOptions.AddRange(lunch, dinner);
        await db.SaveChangesAsync();

        var booking = await service.CreateStayRequestAsync(new StayRequestInput
        {
            ContactName = "Anna",
            ContactEmail = $"anna-{Guid.NewGuid():N}@test.se",
            CheckIn = new DateOnly(2026, 8, 10),
            CheckOut = new DateOnly(2026, 8, 12), // 2 nights
            OvernightGuestCount = overnightCount,
            MealPlan = mealPlan,
            AcceptPrivacyPolicy = true
        }, createdByAdmin: true);
        // Approval requires a lodging assignment (FR-103/D1).
        await BuildingAssignmentSeed.AssignBuildingAsync(db, booking.Id);
        await service.ApproveAsync(booking.Id);

        var stored = (await db.Bookings.FindAsync(booking.Id))!;
        // Resolved off the stored booking — which carries the standard-schedule edge defaults
        // applied at creation (arrival dinner, departure breakfast).
        return new Seed(booking.Id, stored.CompletionToken!, lunch.Id, dinner.Id,
            MealOccasionResolver.Resolve(stored));
    }

    /// <summary>A form on the standard schedule: a dish chosen for every sitting in the posted grid
    /// (the server keeps only the ones the group takes), one allergy, and one vegetarian portion.</summary>
    private static CompletionFormModel FullBoardForm(Seed seed, int count = 1)
    {
        var form = new CompletionFormModel
        {
            ArrivalDinner = true,
            DepartureBreakfast = true,
            MealChoices = seed.Occasions.AllSittings
                .Select(s => (string?)(s.MealType == MealType.Lunch ? seed.LunchId : seed.DinnerId).ToString())
                .ToList(),
            VegetarianCount = 1,
        };
        for (var i = 0; i < count; i++)
        {
            form.Overnight.Add(new OvernightParticipantEntry
            {
                FullName = $"Deltagare {i + 1}",
                AgeCategory = AgeCategory.Adult,
                Allergies = [Allergy.Nut],
            });
        }
        return form;
    }

    [Fact]
    public async Task Submit_Valid_PersistsRosterAndGroupMeals_AdvancesStatus_NotifiesAdmin()
    {
        var provider = NewProvider(out var email);
        var seed = await SeedAsync(provider);

        CompletionSubmitResult result;
        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            result = await service.SubmitCompletionAsync(seed.Token, FullBoardForm(seed), DateTime.UtcNow);
        }

        Assert.Equal(CompletionSubmitOutcome.Success, result.Outcome);

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings
                .Include(b => b.Participants)
                .Include(b => b.MealSelections)
                .SingleAsync(b => b.Id == seed.BookingId);

            Assert.Equal(BookingStatus.DetailsComplete, booking.Status);
            var participant = Assert.Single(booking.Participants);
            Assert.Equal("Deltagare 1", participant.FullName);
            Assert.Equal([Allergy.Nut], participant.Allergies);

            // The group's choices, on the booking — not on the participant (FR-105). A 2-night stay
            // on the standard schedule takes 3 sittings: arrival dinner, middle lunch, middle dinner.
            Assert.Equal(3, booking.MealSelections.Count);
            Assert.All(booking.MealSelections, m => Assert.False(string.IsNullOrEmpty(m.MealOptionSnapshot)));
            Assert.Contains(booking.MealSelections,
                m => m.Date == new DateOnly(2026, 8, 10) && m.MealType == MealType.Dinner);
            // The departure day is breakfast-only, so no dish is stored for it.
            Assert.DoesNotContain(booking.MealSelections, m => m.Date == new DateOnly(2026, 8, 12));

            // The edge-day occasions the selections were built against are persisted with them.
            Assert.True(booking.ArrivalDinner);
            Assert.True(booking.DepartureBreakfast);
            Assert.False(booking.DepartureLunch);

            // Preferences are counts on the booking, not flags on a person (§4.6).
            Assert.Equal(1, booking.VegetarianCount);
        }

        Assert.Contains(email.Sent, e => e.ToAddress == "admin@test.se" && e.Subject.Contains("inskickade"));
    }

    private static DayGuestEntry DayGuest(string date = "2026-08-11") => new()
    {
        FullName = "Dagsgäst D",
        AgeCategory = AgeCategory.Child,
        AttendanceDate = date,
    };

    [Fact]
    public async Task Submit_WithDayGuest_PersistsDayGuestWithDate_AndBookingLevelAttendance()
    {
        var provider = NewProvider(out _);
        var seed = await SeedAsync(provider); // FullBoardAll

        var form = FullBoardForm(seed);
        form.DayGuests.Add(DayGuest());
        form.DayGuestLunch = true;
        form.DayGuestDinner = true;

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            Assert.Equal(CompletionSubmitOutcome.Success,
                (await service.SubmitCompletionAsync(seed.Token, form, DateTime.UtcNow)).Outcome);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings
                .Include(b => b.Participants)
                .SingleAsync(b => b.Id == seed.BookingId);

            var dayGuest = Assert.Single(booking.Participants, p => p.ParticipantType == ParticipantType.DayGuest);
            Assert.Equal(new DateOnly(2026, 8, 11), dayGuest.AttendanceDate);

            // Which meals day guests attend is one setting on the booking, not per person (§4.2).
            Assert.False(booking.DayGuestBreakfast);
            Assert.True(booking.DayGuestLunch);
            Assert.True(booking.DayGuestDinner);
        }
    }

    [Fact]
    public async Task Submit_EdgeDayMealsChosenByTheGroup_AreStoredAndDriveTheSelections()
    {
        var provider = NewProvider(out _);
        var seed = await SeedAsync(provider); // stay 2026-08-10 → 2026-08-12

        // The group arrives early and wants lunch on arrival, and stays for lunch on departure —
        // both free choices, unconstrained by the stated times (§4.1, Q4).
        var form = FullBoardForm(seed);
        form.ArrivalLunch = true;
        form.DepartureLunch = true;

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            Assert.Equal(CompletionSubmitOutcome.Success,
                (await service.SubmitCompletionAsync(seed.Token, form, DateTime.UtcNow)).Outcome);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings
                .Include(b => b.MealSelections)
                .SingleAsync(b => b.Id == seed.BookingId);

            Assert.True(booking.ArrivalLunch);
            Assert.True(booking.DepartureLunch);

            // 5 sittings now: arrival lunch + dinner, middle lunch + dinner, departure lunch.
            Assert.Equal(5, booking.MealSelections.Count);
            Assert.Contains(booking.MealSelections,
                m => m.Date == new DateOnly(2026, 8, 10) && m.MealType == MealType.Lunch);
            Assert.Contains(booking.MealSelections,
                m => m.Date == new DateOnly(2026, 8, 12) && m.MealType == MealType.Lunch);
            // Departure dinner was never chosen, so it must not have been stored.
            Assert.DoesNotContain(booking.MealSelections,
                m => m.Date == new DateOnly(2026, 8, 12) && m.MealType == MealType.Dinner);
        }
    }

    [Fact]
    public async Task Submit_SelfCateringAll_DropsDayGuests()
    {
        var provider = NewProvider(out _);
        var seed = await SeedAsync(provider, mealPlan: MealPlan.SelfCateringAll);

        // A self-catering booking with a posted day guest, edge-day meals and preference counts —
        // all of which must be dropped server-side (§4.1/§4.2), whatever the client sent.
        var form = new CompletionFormModel
        {
            ArrivalDinner = true,
            DepartureBreakfast = true,
            VegetarianCount = 3,
        };
        form.Overnight.Add(new OvernightParticipantEntry
        {
            FullName = "Självhushåll",
            AgeCategory = AgeCategory.Adult,
        });
        form.DayGuests.Add(DayGuest());

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            Assert.Equal(CompletionSubmitOutcome.Success,
                (await service.SubmitCompletionAsync(seed.Token, form, DateTime.UtcNow)).Outcome);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings
                .Include(b => b.Participants)
                .Include(b => b.MealSelections)
                .SingleAsync(b => b.Id == seed.BookingId);

            Assert.DoesNotContain(booking.Participants, p => p.ParticipantType == ParticipantType.DayGuest);
            Assert.Single(booking.Participants); // only the overnight participant
            Assert.Empty(booking.MealSelections);
            Assert.False(booking.ArrivalDinner);
            Assert.False(booking.DepartureBreakfast);
            Assert.Equal(0, booking.VegetarianCount);
        }
    }

    [Fact]
    public async Task Submit_Twice_SecondIsRejectedAsAlreadyCompleted()
    {
        var provider = NewProvider(out _);
        var seed = await SeedAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            Assert.Equal(CompletionSubmitOutcome.Success,
                (await service.SubmitCompletionAsync(seed.Token, FullBoardForm(seed), DateTime.UtcNow)).Outcome);
        }

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            var second = await service.SubmitCompletionAsync(seed.Token, FullBoardForm(seed), DateTime.UtcNow);
            Assert.Equal(CompletionSubmitOutcome.LinkNotValid, second.Outcome);
            Assert.Equal(CompletionAccess.AlreadyCompleted, second.Access);
        }
    }

    [Fact]
    public async Task Submit_MissingName_FailsValidation_PersistsNothing()
    {
        var provider = NewProvider(out _);
        var seed = await SeedAsync(provider);

        var form = FullBoardForm(seed);
        form.Overnight[0].FullName = "   ";

        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BookingService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var result = await service.SubmitCompletionAsync(seed.Token, form, DateTime.UtcNow);

        Assert.Equal(CompletionSubmitOutcome.ValidationFailed, result.Outcome);
        Assert.NotEmpty(result.Errors);
        var booking = await db.Bookings.Include(b => b.Participants).SingleAsync(b => b.Id == seed.BookingId);
        Assert.Equal(BookingStatus.Approved, booking.Status); // unchanged
        Assert.Empty(booking.Participants);
    }

    [Fact]
    public async Task Submit_ExpiredLink_IsRejected_PersistsNothing()
    {
        var provider = NewProvider(out _);
        var seed = await SeedAsync(provider);

        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BookingService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Evaluate/submit well past the 30-day ceiling.
        var result = await service.SubmitCompletionAsync(seed.Token, FullBoardForm(seed), DateTime.UtcNow.AddDays(60));

        Assert.Equal(CompletionSubmitOutcome.LinkNotValid, result.Outcome);
        Assert.Equal(CompletionAccess.Expired, result.Access);
        var booking = await db.Bookings.Include(b => b.Participants).SingleAsync(b => b.Id == seed.BookingId);
        Assert.Equal(BookingStatus.Approved, booking.Status);
        Assert.Empty(booking.Participants);
    }
}
