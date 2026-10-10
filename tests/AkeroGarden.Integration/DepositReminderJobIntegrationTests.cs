using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the deposit reminder (FR-116) against real PostgreSQL: one reminder,
/// only once the due date has passed, only while the booking still waits, never cancelling
/// anything, and retried the next night when the send fails.
///
/// <para>Every test runs on <b>a database of its own</b>: the subject is a whole run, and a run acts
/// on every due booking — on the shared database another test's overdue booking would change the
/// count (the PR 95 lesson).</para>
/// </summary>
public class DepositReminderJobIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public DepositReminderJobIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private async Task<(IServiceProvider Provider, FakeEmailService Email)> NewProviderAsync()
    {
        var fake = new FakeEmailService();
        var provider = await _fixture.BuildIsolatedServicesAsync(s =>
        {
            s.AddSingleton<IEmailService>(fake);
            // Pinned: the subject is a distance — past the due date or not. Every date below comes
            // from this clock (DbFixture.TodayIn), never from the wall clock.
            DbFixture.PinClock(s, DbFixture.FixedNow);
            s.AddScoped<BookingEmailService>();
            s.AddScoped<DepositReminderJob>();
            s.Configure<BookingEmailOptions>(o =>
            {
                o.PublicBaseUrl = "https://akerogarden.se";
                o.AdminBaseUrl = "https://admin.akerogarden.se";
                o.AdminNotificationEmail = "admin@test.se";
                o.ContactEmail = "info@test.se";
            });
        });
        return (provider, fake);
    }

    private static async Task<Guid> SeedAsync(
        IServiceProvider provider, BookingStatus status, DateOnly dueDate, DateTime? reminderSentAt = null,
        decimal? estimatedPrice = null)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var checkIn = DbFixture.TodayIn(scope.ServiceProvider).AddDays(60);
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
            DepositDueDate = dueDate,
            DepositReminderSentAt = reminderSentAt,
            EstimatedPriceInclVat = estimatedPrice,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        booking.BookingReference = BookingReferencePolicy.Format(booking.SequenceNumber, booking.CreatedAt.Year);
        await db.SaveChangesAsync();
        return booking.Id;
    }

    private static async Task<int> RunAsync(IServiceProvider provider, DateOnly today)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<DepositReminderJob>().RunAsync(today);
    }

    private static async Task<Booking> ReloadAsync(IServiceProvider provider, Guid id)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Bookings.SingleAsync(b => b.Id == id);
    }

    private static async Task<List<AuditAction>> AuditActionsAsync(IServiceProvider provider, Guid id)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuditLogs
            .Where(a => a.BookingId == id).Select(a => a.Action).ToListAsync();
    }

    [Fact]
    public async Task Overdue_WaitingBooking_GetsOneReminder_AndStaysWaiting()
    {
        var (provider, email) = await NewProviderAsync();
        var today = DbFixture.TodayIn(provider);
        var id = await SeedAsync(provider, BookingStatus.AwaitingDeposit, dueDate: today.AddDays(-1));

        Assert.Equal(1, await RunAsync(provider, today));

        var booking = await ReloadAsync(provider, id);
        Assert.NotNull(booking.DepositReminderSentAt);
        Assert.Equal(BookingStatus.AwaitingDeposit, booking.Status); // never cancels: the admin decides
        var mail = Assert.Single(email.Sent);
        Assert.Contains("handpenningen har inte kommit in", mail.Subject);
        Assert.Contains(booking.BookingReference, mail.HtmlBody);
        Assert.Contains(AuditAction.DepositReminderSent, await AuditActionsAsync(provider, id));

        // Once only: the next night sends nothing more.
        Assert.Equal(0, await RunAsync(provider, today.AddDays(1)));
        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task TheReminder_RestatesTheDeposit_FromTheStoredPrice()
    {
        // The job reaches the template through BookingEmailService — the wiring is what this pins: a
        // reminder sent without the stored price would silently drop the amount (FR-116, §5.6).
        var (provider, email) = await NewProviderAsync();
        var today = DbFixture.TodayIn(provider);
        await SeedAsync(provider, BookingStatus.AwaitingDeposit, dueDate: today.AddDays(-1), estimatedPrice: 13_500m);

        Assert.Equal(1, await RunAsync(provider, today));

        var mail = Assert.Single(email.Sent);
        Assert.Contains("handpenningen på <strong>2\u00A0700\u00A0kr</strong>", mail.HtmlBody);
    }

    [Fact]
    public async Task OnTheDueDateItself_NoReminder()
    {
        var (provider, email) = await NewProviderAsync();
        var today = DbFixture.TodayIn(provider);
        await SeedAsync(provider, BookingStatus.AwaitingDeposit, dueDate: today);

        Assert.Equal(0, await RunAsync(provider, today));
        Assert.Empty(email.Sent);
    }

    /// <summary>The due date stays on a booking after it moves on (it records that a deposit was
    /// asked for), so the job must key on the status, not the date.</summary>
    [Theory]
    [InlineData(BookingStatus.Approved)]
    [InlineData(BookingStatus.Cancelled)]
    public async Task ABookingNoLongerWaiting_IsNeverReminded(BookingStatus status)
    {
        var (provider, email) = await NewProviderAsync();
        var today = DbFixture.TodayIn(provider);
        await SeedAsync(provider, status, dueDate: today.AddDays(-5));

        Assert.Equal(0, await RunAsync(provider, today));
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task AFailedSend_LeavesTheBookingDue_AndTheNextNightRetries()
    {
        var (provider, email) = await NewProviderAsync();
        var today = DbFixture.TodayIn(provider);
        var id = await SeedAsync(provider, BookingStatus.AwaitingDeposit, dueDate: today.AddDays(-1));

        email.Delivers = false;
        Assert.Equal(0, await RunAsync(provider, today));
        Assert.Null((await ReloadAsync(provider, id)).DepositReminderSentAt);
        Assert.Contains(AuditAction.EmailSendFailed, await AuditActionsAsync(provider, id));

        email.Delivers = true;
        Assert.Equal(1, await RunAsync(provider, today.AddDays(1)));
        Assert.NotNull((await ReloadAsync(provider, id)).DepositReminderSentAt);
    }
}
