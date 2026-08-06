using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;
using AkeroGarden.Web.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for completion-form submission against real PostgreSQL (Phase 4 slice 3,
/// FR-55/57/61/62, §2.5): a valid submission persists overnight participants + per-day meal
/// preferences with snapshots, advances the booking to DetailsComplete, and notifies the admin;
/// a second submission is rejected (single-use); validation failures and expired links persist
/// nothing.
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
        IReadOnlyList<MealSlot> Slots);

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

        var token = (await db.Bookings.FindAsync(booking.Id))!.CompletionToken!;
        var slots = MealsByDayCalculator.Slots(new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 12));
        return new Seed(booking.Id, token, lunch.Id, dinner.Id, slots);
    }

    private static CompletionFormModel FullBoardForm(Seed seed, int count = 1)
    {
        var form = new CompletionFormModel();
        for (var i = 0; i < count; i++)
        {
            form.Overnight.Add(new OvernightParticipantEntry
            {
                FullName = $"Deltagare {i + 1}",
                AgeCategory = AgeCategory.Adult,
                IsFullBoard = true,
                MealChoices = seed.Slots
                    .Select(s => (string?)(s.MealType == MealType.Lunch ? seed.LunchId : seed.DinnerId).ToString())
                    .ToList(),
                Dietary = [DietaryRequirement.Vegetarian],
            });
        }
        return form;
    }

    [Fact]
    public async Task Submit_Valid_PersistsParticipantsAndMeals_AdvancesStatus_NotifiesAdmin()
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
                .Include(b => b.Participants).ThenInclude(p => p.MealPreferences)
                .SingleAsync(b => b.Id == seed.BookingId);

            Assert.Equal(BookingStatus.DetailsComplete, booking.Status);
            var participant = Assert.Single(booking.Participants);
            Assert.Equal("Deltagare 1", participant.FullName);
            Assert.True(participant.AttendsBreakfast);
            Assert.Equal([DietaryRequirement.Vegetarian], participant.DietaryRequirements);

            // 2-night stay → 3 meal slots (dinner, lunch, dinner), each snapshotted.
            Assert.Equal(3, participant.MealPreferences.Count);
            Assert.All(participant.MealPreferences, mp => Assert.False(string.IsNullOrEmpty(mp.MealOptionSnapshot)));
        }

        Assert.Contains(email.Sent, e => e.ToAddress == "admin@test.se" && e.Subject.Contains("inskickade"));
    }

    private static DayGuestEntry DayGuest(Seed seed, string date = "2026-08-11") => new()
    {
        FullName = "Dagsgäst D",
        AgeCategory = AgeCategory.Child,
        AttendanceDate = date,
        AttendsBreakfast = true,
        LunchChoice = seed.LunchId.ToString(),
        DinnerChoice = seed.DinnerId.ToString(),
    };

    [Fact]
    public async Task Submit_WithDayGuest_PersistsDayGuestWithDateAndMeals()
    {
        var provider = NewProvider(out _);
        var seed = await SeedAsync(provider); // FullBoardAll

        var form = FullBoardForm(seed);
        form.DayGuests.Add(DayGuest(seed));

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
                .Include(b => b.Participants).ThenInclude(p => p.MealPreferences)
                .SingleAsync(b => b.Id == seed.BookingId);

            var dayGuest = Assert.Single(booking.Participants, p => p.ParticipantType == ParticipantType.DayGuest);
            Assert.Equal(new DateOnly(2026, 8, 11), dayGuest.AttendanceDate);
            Assert.True(dayGuest.AttendsBreakfast);
            Assert.Equal(2, dayGuest.MealPreferences.Count); // lunch + dinner for the attended date
        }
    }

    [Fact]
    public async Task Submit_DayGuestOnDepartureDay_DropsLunchAndDinner()
    {
        var provider = NewProvider(out _);
        var seed = await SeedAsync(provider); // stay 2026-08-10 → 2026-08-12

        var form = FullBoardForm(seed);
        form.DayGuests.Add(DayGuest(seed, date: "2026-08-12")); // departure day — breakfast only

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
                .Include(b => b.Participants).ThenInclude(p => p.MealPreferences)
                .SingleAsync(b => b.Id == seed.BookingId);

            var dayGuest = Assert.Single(booking.Participants, p => p.ParticipantType == ParticipantType.DayGuest);
            Assert.True(dayGuest.AttendsBreakfast);       // breakfast is served on the departure day
            Assert.Empty(dayGuest.MealPreferences);       // lunch + dinner dropped (§4.1)
        }
    }

    [Fact]
    public async Task Submit_SelfCateringAll_DropsDayGuests()
    {
        var provider = NewProvider(out _);
        var seed = await SeedAsync(provider, mealPlan: MealPlan.SelfCateringAll);

        // A self-catering overnight participant + a posted day guest that must be dropped (§4.2).
        var form = new CompletionFormModel();
        form.Overnight.Add(new OvernightParticipantEntry
        {
            FullName = "Självhushåll",
            AgeCategory = AgeCategory.Adult,
            IsFullBoard = false,
        });
        form.DayGuests.Add(DayGuest(seed));

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            Assert.Equal(CompletionSubmitOutcome.Success,
                (await service.SubmitCompletionAsync(seed.Token, form, DateTime.UtcNow)).Outcome);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings.Include(b => b.Participants).SingleAsync(b => b.Id == seed.BookingId);
            Assert.DoesNotContain(booking.Participants, p => p.ParticipantType == ParticipantType.DayGuest);
            Assert.Single(booking.Participants); // only the overnight participant
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
