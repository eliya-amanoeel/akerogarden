using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Paging of the admin lists that grow (design.md, "Pager"): Bokningar (FR-36), Deltagar- och
/// matlistor (FR-104) and Blockeringar (FR-40). Each test runs on its own database — the subject is
/// how the whole table orders and pages, which another class's rows in the shared database would
/// change.
/// </summary>
public class AdminListPagingIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public AdminListPagingIntegrationTests(DbFixture fixture) => _fixture = fixture;

    private Task<IServiceProvider> IsolatedProviderAsync() =>
        _fixture.BuildIsolatedServicesAsync(s =>
        {
            s.AddSingleton<IEmailService>(new FakeEmailService());
            s.AddScoped<BookingEmailService>();
            s.AddScoped<CleaningZoneService>();
            s.AddScoped<BookingService>();
            s.AddScoped<BlockedPeriodService>();
            s.Configure<SiteOptions>(o =>
            {
                o.PublicBaseUrl = "https://akerogarden.se";
                o.AdminBaseUrl = "https://admin.akerogarden.se";
            });
            s.Configure<BookingEmailOptions>(o => o.AdminNotificationEmail = "admin@test.se");
        });

    private static Booking NewBooking(int n, BookingStatus status, DateTime createdAt, DateOnly? checkIn = null) => new()
    {
        Id = Guid.NewGuid(),
        BookingReference = $"AG-P-{n:D4}",
        BookingType = checkIn is null ? BookingType.GeneralInquiry : BookingType.StayRequest,
        Status = status,
        ContactName = $"Gäst {n}",
        ContactEmail = $"gast-{n}@test.se",
        CheckIn = checkIn,
        CheckOut = checkIn?.AddDays(2),
        OvernightGuestCount = checkIn is null ? null : 10,
        CreatedAt = createdAt,
        UpdatedAt = createdAt,
    };

    private static IEnumerable<string> References(ListPage<Booking> page) => page.Items.Select(b => b.BookingReference);

    private static IEnumerable<string> Expected(params int[] ns) => ns.Select(n => $"AG-P-{n:D4}");

    private static int[] Down(int from, int to) => [.. Enumerable.Range(to, from - to + 1).Reverse()];

    [Fact]
    public async Task Booking_list_is_newest_first_25_to_a_page_with_ties_in_one_order_and_filters_reset_the_page_count()
    {
        var provider = await IsolatedProviderAsync();
        var start = DateTime.UtcNow.AddDays(-30);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // 27 bookings, one minute apart, newest = 27. 26 and 27 arrive in the same instant: the
            // reference decides between them, the same way every time. 21–27 are denied, for the filter.
            for (var n = 1; n <= 27; n++)
            {
                var createdAt = start.AddMinutes(n == 27 ? 26 : n);
                db.Bookings.Add(NewBooking(n, n > 20 ? BookingStatus.Denied : BookingStatus.Pending, createdAt));
            }
            await db.SaveChangesAsync();
        }

        await using var read = provider.CreateAsyncScope();
        var bookings = read.ServiceProvider.GetRequiredService<BookingService>();

        var first = await bookings.GetBookingsPageAsync();
        Assert.Equal(1, first.Page);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(ListPage.RowsPerPage, first.Items.Count);
        Assert.Equal(Expected(Down(27, 3)), References(first));

        var second = await bookings.GetBookingsPageAsync(page: 2);
        Assert.Equal(2, second.Page);
        Assert.Equal(Expected(2, 1), References(second));

        // A page past the end is kept inside the pages that exist, never an empty page.
        var beyond = await bookings.GetBookingsPageAsync(page: 9);
        Assert.Equal(2, beyond.Page);
        Assert.Equal(References(second), References(beyond));

        // A filter that leaves one page puts the reader on it, whatever page they were on.
        var denied = await bookings.GetBookingsPageAsync(status: BookingStatus.Denied, page: 2);
        Assert.Equal(1, denied.Page);
        Assert.Equal(1, denied.TotalPages);
        Assert.Equal(Expected(Down(27, 21)), References(denied));

        // Nothing matching is still one (empty) page, so the list shows its "no match" message.
        var none = await bookings.GetBookingsPageAsync(reference: "AG-9999-9999");
        Assert.Empty(none.Items);
        Assert.Equal(1, none.Page);
        Assert.Equal(1, none.TotalPages);
    }

    [Fact]
    public async Task List_view_pages_only_bookings_with_lists_latest_check_in_first()
    {
        var provider = await IsolatedProviderAsync();
        var today = DbFixture.TodayIn(provider);
        var createdAt = DateTime.UtcNow.AddDays(-30);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // 26 bookings with lists, a week apart, latest check-in = 26. 25 and 26 start the same day:
            // the reference decides between them.
            for (var n = 1; n <= 26; n++)
            {
                var status = (n % 3) switch
                {
                    0 => BookingStatus.DetailsComplete,
                    1 => BookingStatus.Active,
                    _ => BookingStatus.Completed,
                };
                db.Bookings.Add(NewBooking(n, status, createdAt, today.AddDays(7 * (n == 26 ? 25 : n))));
            }
            // A pending and an approved stay with a later check-in than any of them: neither has lists
            // yet, so neither is on any page (FR-104).
            db.Bookings.Add(NewBooking(98, BookingStatus.Pending, createdAt, today.AddYears(1)));
            db.Bookings.Add(NewBooking(99, BookingStatus.Approved, createdAt, today.AddYears(1)));
            await db.SaveChangesAsync();
        }

        await using var read = provider.CreateAsyncScope();
        var bookings = read.ServiceProvider.GetRequiredService<BookingService>();

        var first = await bookings.GetBookingsWithListsPageAsync();
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(Expected(Down(26, 2)), References(first));

        var second = await bookings.GetBookingsWithListsPageAsync(page: 2);
        Assert.Equal(Expected(1), References(second));
    }

    [Fact]
    public async Task Block_list_is_latest_start_first_25_to_a_page_and_knows_which_page_holds_a_block()
    {
        var provider = await IsolatedProviderAsync();
        var today = DbFixture.TodayIn(provider);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // 26 whole-property blocks, a month apart, the oldest in the past.
            for (var n = 1; n <= 26; n++)
            {
                var startDate = today.AddMonths(n - 13);
                db.BlockedPeriods.Add(new BlockedPeriod
                {
                    Id = Guid.NewGuid(),
                    Scope = BlockScope.WholeProperty,
                    StartDate = startDate,
                    EndDate = startDate.AddDays(2),
                    Reason = $"Block {n}",
                });
            }
            await db.SaveChangesAsync();
        }

        await using var read = provider.CreateAsyncScope();
        var blocks = read.ServiceProvider.GetRequiredService<BlockedPeriodService>();

        var first = await blocks.GetPageAsync();
        Assert.Equal(1, first.Page);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(Enumerable.Range(2, 25).Reverse().Select(n => $"Block {n}"), first.Items.Select(p => p.Reason));

        // The finished blocks are what falls to the last page, not what fills the first.
        var second = await blocks.GetPageAsync(page: 2);
        var oldest = Assert.Single(second.Items);
        Assert.Equal("Block 1", oldest.Reason);

        // Where the list goes after a save: the page that holds the block, in the same order.
        Assert.Equal(2, await blocks.PageOfAsync(oldest.Id));
        Assert.Equal(1, await blocks.PageOfAsync(first.Items[0].Id));
        Assert.Equal(1, await blocks.PageOfAsync(first.Items[^1].Id));
        Assert.Equal(1, await blocks.PageOfAsync(Guid.NewGuid())); // gone: the first page
    }
}
