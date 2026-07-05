using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for the GDPR retention job against real PostgreSQL (§2.7, FR-66/69/91):
/// records past <c>DeleteAfter</c> are deleted (cascading to participants), the 30-day pre-deletion
/// admin warning fires exactly once (de-duplicated via <c>DeletionWarningEmailSentAt</c>), and live
/// in-progress bookings (null <c>DeleteAfter</c>) are never touched.
/// </summary>
public class RetentionJobIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public RetentionJobIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private IServiceProvider NewProvider(out FakeEmailService email)
    {
        var fake = new FakeEmailService();
        email = fake;
        return _fixture.BuildServices(s =>
        {
            s.AddSingleton<IEmailService>(fake);
            s.AddSingleton(TimeProvider.System);
            s.AddScoped<BookingEmailService>();
            s.AddScoped<RetentionJob>();
            s.Configure<BookingEmailOptions>(o =>
            {
                o.PublicBaseUrl = "https://akerogarden.se";
                o.AdminBaseUrl = "https://admin.akerogarden.se";
                o.AdminNotificationEmail = "admin@test.se";
                o.ContactEmail = "info@test.se";
            });
        });
    }

    private static async Task<Guid> SeedAsync(
        IServiceProvider provider,
        BookingStatus status,
        DateOnly? deleteAfter,
        DateTime? warningSentAt = null,
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
            CheckIn = new DateOnly(2026, 1, 1),
            CheckOut = new DateOnly(2026, 1, 4),
            DeleteAfter = deleteAfter,
            DeletionWarningEmailSentAt = warningSentAt,
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
        return booking.Id;
    }

    private async Task<RetentionRunSummary> RunAsync(IServiceProvider provider, DateOnly today)
    {
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RetentionJob>().RunAsync(today);
    }

    private async Task<bool> ExistsAsync(IServiceProvider provider, Guid id)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Bookings.AnyAsync(b => b.Id == id);
    }

    [Fact]
    public async Task Deletes_RecordsPastDeleteAfter_CascadingParticipants()
    {
        var provider = NewProvider(out _);
        var today = new DateOnly(2026, 7, 5);
        var due = await SeedAsync(provider, BookingStatus.Completed, deleteAfter: today.AddDays(-1), withParticipant: true);
        var future = await SeedAsync(provider, BookingStatus.Completed, deleteAfter: today.AddDays(400));

        var summary = await RunAsync(provider, today);

        Assert.Equal(1, summary.Deleted);
        Assert.False(await ExistsAsync(provider, due));
        Assert.True(await ExistsAsync(provider, future));

        // Participant cascade-deleted with its booking.
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Participants.AnyAsync(p => p.BookingId == due));
    }

    [Fact]
    public async Task Deletes_OnExactDeleteAfterDate()
    {
        var provider = NewProvider(out _);
        var today = new DateOnly(2026, 7, 5);
        var id = await SeedAsync(provider, BookingStatus.Cancelled, deleteAfter: today);

        Assert.Equal(1, (await RunAsync(provider, today)).Deleted);
        Assert.False(await ExistsAsync(provider, id));
    }

    [Fact]
    public async Task LiveInProgressBooking_WithNullDeleteAfter_IsNeverTouched()
    {
        var provider = NewProvider(out var email);
        var today = new DateOnly(2026, 7, 5);
        var id = await SeedAsync(provider, BookingStatus.Active, deleteAfter: null);

        var summary = await RunAsync(provider, today);

        Assert.Equal(0, summary.Deleted);
        Assert.Equal(0, summary.Warned);
        Assert.True(await ExistsAsync(provider, id));
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task Warns_Once_WithinThirtyDayWindow_AndSetsSentMarker()
    {
        var provider = NewProvider(out var email);
        var today = new DateOnly(2026, 7, 5);
        var id = await SeedAsync(provider, BookingStatus.Completed, deleteAfter: today.AddDays(20));

        var first = await RunAsync(provider, today);
        Assert.Equal(1, first.Warned);
        Assert.Single(email.Sent);
        Assert.Contains(email.Sent, e => e.ToAddress == "admin@test.se" && e.Subject.Contains("gallring", StringComparison.OrdinalIgnoreCase));

        // Marker persisted.
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.NotNull((await db.Bookings.SingleAsync(b => b.Id == id)).DeletionWarningEmailSentAt);
        }

        // Second run does not re-warn (dedupe).
        var second = await RunAsync(provider, today);
        Assert.Equal(0, second.Warned);
        Assert.Single(email.Sent); // still just the one
    }

    [Fact]
    public async Task DoesNotWarn_OutsideWindow_OrWhenAlreadyWarned()
    {
        var provider = NewProvider(out var email);
        var today = new DateOnly(2026, 7, 5);
        await SeedAsync(provider, BookingStatus.Completed, deleteAfter: today.AddDays(31));                 // too far out
        await SeedAsync(provider, BookingStatus.Completed, deleteAfter: today.AddDays(10), warningSentAt: DateTime.UtcNow); // already warned

        var summary = await RunAsync(provider, today);

        Assert.Equal(0, summary.Warned);
        Assert.Empty(email.Sent);
    }
}
