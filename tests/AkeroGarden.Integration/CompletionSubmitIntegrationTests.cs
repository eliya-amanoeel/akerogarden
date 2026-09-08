using System.Net;
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
        return NewProvider(fake);
    }

    private IServiceProvider NewProvider(IEmailService sender)
    {
        return _fixture.BuildServices(s =>
        {
            s.AddSingleton(sender);
            s.AddScoped<BookingEmailService>();
            s.AddScoped<BookingService>();
            s.AddScoped<CleaningZoneService>();
            s.Configure<BookingEmailOptions>(o =>
            {
                o.PublicBaseUrl = "https://akerogarden.se";
                o.AdminBaseUrl = "https://admin.akerogarden.se";
                o.AdminNotificationEmail = "admin@test.se";
                o.ContactEmail = "info@test.se";
                o.PropertyAddress = "Åkerögården, Norrtälje";
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
            AcceptTerms = true
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
            // Every participant this helper builds declares an allergy (below), and FR-110 requires
            // explicit consent whenever the form declares one. Not a workaround for the new rule —
            // the rejection path is asserted directly in the two consent tests further down, so
            // setting it here cannot hide a regression in it.
            AllergyConsent = true,
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

    /// <summary>FR-110: a form declaring an allergy without consent is rejected, and nothing is
    /// persisted. Until FR-110 this page collected art. 9 health data with no consent control at all
    /// while the privacy policy stated the basis was explicit consent — the statement was untrue.</summary>
    [Fact]
    public async Task Submit_AllergyDeclaredWithoutConsent_IsRejected_AndPersistsNothing()
    {
        var provider = NewProvider(out _);
        var seed = await SeedAsync(provider);
        var form = FullBoardForm(seed);
        form.AllergyConsent = false;   // the helper's participants each declare an allergy

        CompletionSubmitResult result;
        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            result = await service.SubmitCompletionAsync(seed.Token, form, DateTime.UtcNow);
        }

        Assert.Equal(CompletionSubmitOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(result.Errors, e => e.Contains("samtycke"));

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings
                .Include(b => b.Participants)
                .SingleAsync(b => b.Id == seed.BookingId);

            // The health data must not land, and the booking must not advance.
            Assert.Empty(booking.Participants);
            Assert.Null(booking.AllergyConsentAt);
            Assert.Equal(BookingStatus.Approved, booking.Status);
        }
    }

    /// <summary>FR-110 / GDPR art. 7.1: consent must be demonstrable after the fact, so the moment it
    /// was given is stored rather than the checkbox being validated and discarded. Stamped only when
    /// an allergy was actually declared — a ticked box on a form declaring none records no consent,
    /// because no special-category processing is taking place.</summary>
    [Fact]
    public async Task Submit_StampsAllergyConsent_OnlyWhenAnAllergyWasDeclared()
    {
        var withAllergy = NewProvider(out _);
        var seedA = await SeedAsync(withAllergy);
        var submittedAt = new DateTime(2026, 8, 11, 9, 30, 0, DateTimeKind.Utc);

        using (var scope = withAllergy.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            Assert.Equal(CompletionSubmitOutcome.Success,
                (await service.SubmitCompletionAsync(seedA.Token, FullBoardForm(seedA), submittedAt)).Outcome);
        }

        using (var scope = withAllergy.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings.SingleAsync(b => b.Id == seedA.BookingId);
            // The caller's clock, not DateTime.UtcNow read inside the service.
            Assert.Equal(submittedAt, booking.AllergyConsentAt);
        }

        var noAllergy = NewProvider(out _);
        var seedB = await SeedAsync(noAllergy);
        var clean = FullBoardForm(seedB);
        foreach (var entry in clean.Overnight)
        {
            entry.Allergies = [];
            entry.AllergyNotes = null;
        }

        using (var scope = noAllergy.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            Assert.Equal(CompletionSubmitOutcome.Success,
                (await service.SubmitCompletionAsync(seedB.Token, clean, submittedAt)).Outcome);
        }

        using (var scope = noAllergy.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings.SingleAsync(b => b.Id == seedB.BookingId);
            Assert.Null(booking.AllergyConsentAt);
        }
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

    /// <summary>FR-107 — submitting sends the guest their own confirmation of what they entered,
    /// <b>in addition to</b> the admin notification (FR-61), which is unchanged. Asserts the guest
    /// mail actually carries their data: the participant they typed, that person's allergy, every
    /// dish snapshot stored for the booking, and the assigned lodging — the lodging especially,
    /// because the booking is loaded by token with only Participants included, so a missing
    /// explicit load would drop the building silently rather than fail.</summary>
    [Fact]
    public async Task Submit_SendsTheGuestTheirOwnConfirmation_AlongsideTheAdminNotification()
    {
        var provider = NewProvider(out var email);
        var seed = await SeedAsync(provider);
        var form = FullBoardForm(seed);

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            Assert.Equal(CompletionSubmitOutcome.Success,
                (await service.SubmitCompletionAsync(seed.Token, form, DateTime.UtcNow)).Outcome);
        }

        string guestAddress;
        List<string> dishes;
        string buildingName;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings
                .Include(b => b.MealSelections)
                .Include(b => b.BuildingAssignments)
                .SingleAsync(b => b.Id == seed.BookingId);

            guestAddress = booking.ContactEmail;
            dishes = [.. booking.MealSelections.Select(m => m.MealOptionSnapshot)];
            buildingName = booking.BuildingAssignments.Single().BuildingNameSnapshot;
        }

        // FR-61 still fires — the guest copy is an addition, not a replacement.
        Assert.Contains(email.Sent, e => e.ToAddress == "admin@test.se" && e.Subject.Contains("inskickade"));

        // Matched on subject, not just recipient: the guest already received the approval email at
        // the same address during seeding, so "an email went to the guest" would prove nothing.
        var guestMail = Assert.Single(
            email.Sent, e => e.ToAddress == guestAddress && e.Subject.Contains("tagit emot"));

        Assert.Contains("Referensnummer", guestMail.HtmlBody);
        Assert.Contains("Deltagare 1", guestMail.HtmlBody);
        // Guest-supplied values are HTML-encoded into the body and every non-ASCII character
        // becomes a numeric entity ("Nötallergi" → "N&#246;tallergi"), so the expectation is
        // encoded the same way rather than truncated to its ASCII prefix.
        Assert.Contains(WebUtility.HtmlEncode("Nötallergi"), guestMail.HtmlBody);
        Assert.Contains(buildingName, guestMail.HtmlBody);
        Assert.Contains(WebUtility.HtmlEncode("Norrtälje"), guestMail.HtmlBody);
        Assert.Contains("Kontakta oss", guestMail.HtmlBody);

        Assert.Equal(3, dishes.Count);
        Assert.All(dishes, dish => Assert.Contains(dish, guestMail.HtmlBody));

        // The admin's panel URL belongs in the admin's email only.
        Assert.DoesNotContain("admin.akerogarden.se", guestMail.HtmlBody);
    }

    /// <summary>An email service that can be switched to failing mid-test, standing in for the SMTP
    /// auth outage that took out all email on staging in July 2026. Armed only after seeding, so the
    /// approval email that the seed depends on still goes through.</summary>
    private sealed class ArmableFailingEmailService : IEmailService
    {
        public bool Armed { get; set; }

        public Task<bool> SendAsync(
            string toAddress, string toName, string subject, string htmlBody,
            string? replyToAddress = null, string? replyToName = null) =>
            Armed
                // Deliberately THROWS rather than returning false. SmtpEmailService swallows its own
                // failures and reports them via the return value, so a false is the ordinary path; an
                // exception escaping the mail layer is the harsher case, and this test exists to prove
                // the guest's submission survives even that.
                ? throw new InvalidOperationException("SMTP 535 — simulerat sändningsfel")
                : Task.FromResult(true);
    }

    /// <summary>A failing mail server must not cost the guest their submission. Both notifications
    /// are sent after the commit and the completion link is single-use, so surfacing the SMTP error
    /// would show an error page for a submission that succeeded and leave the guest with a spent
    /// link and nothing to retry. The submission reports success and the data is persisted; the
    /// send failure goes to the log.</summary>
    [Fact]
    public async Task Submit_Succeeds_AndPersists_EvenWhenTheNotificationEmailsFail()
    {
        var sender = new ArmableFailingEmailService();
        var provider = NewProvider(sender);
        var seed = await SeedAsync(provider);
        var form = FullBoardForm(seed);

        sender.Armed = true;

        using (var scope = provider.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<BookingService>();
            var result = await service.SubmitCompletionAsync(seed.Token, form, DateTime.UtcNow);
            Assert.Equal(CompletionSubmitOutcome.Success, result.Outcome);
        }

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var booking = await db.Bookings
                .Include(b => b.Participants)
                .Include(b => b.MealSelections)
                .SingleAsync(b => b.Id == seed.BookingId);

            // The whole submission survived, not merely the status flag.
            Assert.Equal(BookingStatus.DetailsComplete, booking.Status);
            Assert.Single(booking.Participants);
            Assert.Equal(3, booking.MealSelections.Count);
        }
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
