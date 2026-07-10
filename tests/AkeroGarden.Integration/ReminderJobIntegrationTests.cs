using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the 14-day reminder job against real PostgreSQL (§9, FR-63/64): the
/// standard reminder for DetailsComplete+, the regenerate-link + nudge + admin-alert variant for
/// still-Approved bookings, once-only firing (ReminderEmailSentAt), and correct exclusion of
/// out-of-window / ineligible / past-check-in bookings.
/// </summary>
public class ReminderJobIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public ReminderJobIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private IServiceProvider NewProvider(out FakeEmailService email)
    {
        var fake = new FakeEmailService();
        email = fake;
        return _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(fake);
            s.AddSingleton(TimeProvider.System);
            s.AddScoped<BookingEmailService>();
            s.AddScoped<RoomAvailabilityService>();
            s.AddScoped<BookingService>();
            s.AddScoped<ReminderJob>();
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

    private static async Task<Guid> SeedAsync(
        IServiceProvider provider,
        BookingStatus status,
        DateOnly checkIn,
        bool withToken = false,
        bool withParticipant = false)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var booking = new Booking
        {
            Id = Guid.NewGuid(),
            BookingType = BookingType.StayRequest,
            Status = status,
            ContactName = "Gäst",
            ContactEmail = $"g-{Guid.NewGuid():N}@test.se",
            CheckIn = checkIn,
            CheckOut = checkIn.AddDays(3),
            OvernightGuestCount = 4,
            MealPlan = MealPlan.FullBoardAll,
            CompletionToken = withToken ? "old-token" : null,
            CompletionTokenExpiry = withToken ? DateTime.UtcNow.AddDays(1) : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        if (withParticipant)
            booking.Participants.Add(new Participant
            {
                Id = Guid.NewGuid(),
                FullName = "Deltagare",
                ParticipantType = ParticipantType.Overnight,
                AgeCategory = AgeCategory.Adult,
            });
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        booking.BookingReference = BookingReferencePolicy.Format(booking.SequenceNumber, booking.CreatedAt.Year);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private async Task<ReminderRunSummary> RunAsync(IServiceProvider provider, DateOnly today)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ReminderJob>().RunAsync(today);
    }

    private async Task<Booking> ReloadAsync(IServiceProvider provider, Guid id)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Bookings.SingleAsync(b => b.Id == id);
    }

    [Fact]
    public async Task DetailsComplete_WithinWindow_SendsStandardReminder_Once()
    {
        var provider = NewProvider(out var email);
        var today = new DateOnly(2026, 7, 5);
        var id = await SeedAsync(provider, BookingStatus.DetailsComplete, checkIn: today.AddDays(14), withParticipant: true);

        var summary = await RunAsync(provider, today);

        Assert.Equal(1, summary.StandardReminders);
        Assert.Equal(0, summary.Nudges);
        Assert.Contains(email.Sent, e => e.ToAddress != "admin@test.se" && e.Subject.Contains("Påminnelse inför"));
        Assert.NotNull((await ReloadAsync(provider, id)).ReminderEmailSentAt);

        // Second run does not re-send.
        var second = await RunAsync(provider, today);
        Assert.Equal(0, second.StandardReminders);
    }

    [Fact]
    public async Task Approved_WithinWindow_RegeneratesLink_NudgesGuest_AndAlertsAdmin()
    {
        var provider = NewProvider(out var email);
        var today = new DateOnly(2026, 7, 5);
        var id = await SeedAsync(provider, BookingStatus.Approved, checkIn: today.AddDays(10), withToken: true);

        var summary = await RunAsync(provider, today);

        Assert.Equal(1, summary.Nudges);
        Assert.Equal(0, summary.StandardReminders);

        var booking = await ReloadAsync(provider, id);
        Assert.NotEqual("old-token", booking.CompletionToken); // regenerated (I13)
        Assert.NotNull(booking.ReminderEmailSentAt);

        Assert.Contains(email.Sent, e => e.ToAddress != "admin@test.se" && e.Subject.Contains("slutför dina"));       // guest nudge
        Assert.Contains(email.Sent, e => e.ToAddress == "admin@test.se" && e.Subject.Contains("inte slutfört"));       // admin alert
    }

    [Fact]
    public async Task OutsideWindow_Ineligible_OrPastCheckIn_AreSkipped()
    {
        var provider = NewProvider(out var email);
        var today = new DateOnly(2026, 7, 5);
        await SeedAsync(provider, BookingStatus.DetailsComplete, checkIn: today.AddDays(20));  // too far out
        await SeedAsync(provider, BookingStatus.Pending, checkIn: today.AddDays(7));           // ineligible status
        await SeedAsync(provider, BookingStatus.Completed, checkIn: today.AddDays(7));         // terminal
        await SeedAsync(provider, BookingStatus.DetailsComplete, checkIn: today.AddDays(-1));  // check-in already passed

        var summary = await RunAsync(provider, today);

        Assert.Equal(0, summary.StandardReminders);
        Assert.Equal(0, summary.Nudges);
        Assert.Empty(email.Sent);
    }
}
