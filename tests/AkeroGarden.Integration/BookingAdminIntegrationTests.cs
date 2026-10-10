using System.Globalization;
using AkeroGarden.Integration.Infrastructure;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;
using AkeroGarden.Web.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AkeroGarden.Integration;

/// <summary>
/// Integration coverage for admin booking management against real PostgreSQL (FR-38/50/76–80,
/// §2.5): approve/deny/close/cancel behave per spec with token generation and the one-way state
/// machine enforced (illegal transitions and wrong-type actions are blocked). Emails are captured
/// via <see cref="FakeEmailService"/>.
/// </summary>
public class BookingAdminIntegrationTests : IClassFixture<DbFixture>
{
    private readonly DbFixture _fixture;

    public BookingAdminIntegrationTests(DbFixture fixture) => _fixture = fixture;

    /// <param name="pinnedNow">Pin the service's clock. Pass this for any test whose subject is a
    /// <i>distance</i> — the 40-day issue window, the 14-day short expiry. Those seed a check-in by
    /// offsetting today and then assert on what the service computes from its own clock; against the
    /// real clock the two reads can land either side of midnight, and one day is exactly what flips
    /// the outcome at a boundary. Pinning makes the distance the test's own statement rather than
    /// something the run has to agree with.</param>
    private IServiceProvider NewProvider(out FakeEmailService email, DateTimeOffset? pinnedNow = null)
    {
        var fake = new FakeEmailService();
        email = fake;
        return _fixture.BuildServices(s =>
        {
            if (pinnedNow is { } instant) DbFixture.PinClock(s, instant);
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

    /// <summary>Default check-in for tests that are not about timing at all — deny, close, cancel,
    /// filters. Far enough out that no completion link is issued at approval (FR-108), and measured
    /// from the container's own clock so it means the same thing whether that clock is pinned or
    /// real.</summary>
    private static int _window;

    private const int FarOutDays = 90;

    private static async Task<Guid> CreatePendingStayAsync(IServiceProvider provider, DateOnly? checkIn = null)
    {
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BookingService>();
        // Each defaulted stay gets its own window: approval refuses to double-book (FR-10), and
        // these tests share one database, so a fixed "90 days out" would make every approval after
        // the first fail for a reason the test is not about.
        var effectiveCheckIn = checkIn ?? DbFixture.StayWindow(
            scope.ServiceProvider, Interlocked.Increment(ref _window), FarOutDays);
        var b = await service.CreateStayRequestAsync(new StayRequestInput
        {
            ContactName = "Gäst",
            ContactEmail = $"guest-{Guid.NewGuid():N}@test.se",
            // Relative, never a literal. A fixed calendar date drifts across the FR-108 boundary as
            // real time passes: 2026-12-01 sat 113 days out when it was written (no link issued at
            // approval) and would have crossed inside 40 days around 2026-10-22, silently flipping
            // that behaviour for every test using the default. 90 days keeps it clear of both the
            // 40-day trigger and the 44-day point where the expiry rules change, for good.
            CheckIn = effectiveCheckIn,
            CheckOut = effectiveCheckIn.AddDays(3),
            OvernightGuestCount = 5,
            MealPlan = MealPlan.FullBoardAll,
            AcceptTerms = true
        }, createdByAdmin: true); // suppress creation emails so we can assert transition emails

        // Approval now requires a lodging assignment (FR-103/D1), so a "pending stay ready to be
        // approved" is not realistic without one. See BuildingAssignmentSeed for why this is
        // arranged rather than the precondition being relaxed.
        await BuildingAssignmentSeed.AssignBuildingAsync(
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), b.Id);
        return b.Id;
    }

    private static async Task<Guid> CreatePendingGeneralAsync(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BookingService>();
        var b = await service.CreateGeneralInquiryAsync(new GeneralInquiryInput
        {
            ContactName = "Fråga",
            ContactEmail = $"ask-{Guid.NewGuid():N}@test.se",
            Message = "Hej",
            AcceptPrivacyPolicy = true
        }, createdByAdmin: true);
        return b.Id;
    }

    private static async Task<Booking> ReloadAsync(IServiceProvider provider, Guid id)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Bookings.SingleAsync(b => b.Id == id);
    }

    [Fact]
    public async Task Approve_WithinIssueWindow_GeneratesToken_SetsApproved_AndSendsApprovalEmail()
    {
        var provider = NewProvider(out var email, pinnedNow: DbFixture.FixedNow);
        // Inside the 40-day issue window but outside the 14-day short-expiry one (FR-51/75/108).
        var id = await CreatePendingStayAsync(provider, checkIn: DbFixture.TodayIn(provider).AddDays(30));

        ApproveResult result;
        using (var scope = provider.CreateScope())
            result = await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(id, DbFixture.EstimatedPrice);

        Assert.True(result.Succeeded);
        Assert.False(result.ShortExpiryWarning);
        Assert.NotNull(result.TokenExpiry);

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.Approved, booking.Status);
        Assert.False(string.IsNullOrEmpty(booking.CompletionToken));
        Assert.NotNull(booking.CompletionTokenExpiry);
        Assert.NotNull(booking.CompletionLinkSentAt);
        Assert.Null(booking.DeleteAfter); // live state

        var approval = Assert.Single(email.Sent, e => e.Subject.Contains("godkänd"));
        Assert.Contains("/slutfor/", approval.HtmlBody);
    }

    [Fact]
    public async Task Approve_BeyondIssueWindow_IssuesNoLink_AndTheEmailPromisesOneLater()
    {
        // FR-51/52/108: a group booking six months out cannot name its participants yet, so no token
        // is stored and the approval email carries no link — it says one follows nearer the stay.
        var provider = NewProvider(out var email, pinnedNow: DbFixture.FixedNow);
        var id = await CreatePendingStayAsync(provider, checkIn: DbFixture.TodayIn(provider).AddDays(180));

        ApproveResult result;
        using (var scope = provider.CreateScope())
            result = await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(id, DbFixture.EstimatedPrice);

        Assert.True(result.Succeeded);
        Assert.Null(result.TokenExpiry);

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.Approved, booking.Status);
        Assert.Null(booking.CompletionToken);
        Assert.Null(booking.CompletionTokenExpiry);
        Assert.Null(booking.CompletionLinkSentAt); // what the issuance job looks for

        // The guest is still told they are approved — but with no link and no expiry to miss.
        var approval = Assert.Single(email.Sent, e => e.Subject.Contains("godkänd"));
        Assert.DoesNotContain("/slutfor/", approval.HtmlBody);
        Assert.Contains("sex veckor före ankomst", approval.HtmlBody);
    }

    [Fact]
    public async Task Approve_WithNearCheckIn_FlagsShortExpiryWarning()
    {
        var provider = NewProvider(out _, pinnedNow: DbFixture.FixedNow);
        var id = await CreatePendingStayAsync(provider, checkIn: DbFixture.TodayIn(provider).AddDays(5));

        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(id, DbFixture.EstimatedPrice);

        Assert.True(result.Succeeded);
        Assert.True(result.ShortExpiryWarning); // FR-75
    }

    [Fact]
    public async Task Approve_RejectsGeneralInquiry()
    {
        var provider = NewProvider(out _);
        var id = await CreatePendingGeneralAsync(provider);

        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(id, DbFixture.EstimatedPrice);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Deny_RequiresReason_ThenSetsDenied_AndSendsEmail()
    {
        // Pinned: the retention date below is computed by the service from *its* clock (FR-89), so
        // reading the wall clock here to build the expected value is a second, independent "now".
        var provider = NewProvider(out var email, pinnedNow: DbFixture.FixedNow);
        var id = await CreatePendingStayAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<BookingService>();
            Assert.False((await svc.DenyAsync(id, "  ")).Succeeded); // blank reason rejected
            Assert.True((await svc.DenyAsync(id, "Fullbokat")).Succeeded);
        }

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.Denied, booking.Status);
        Assert.Equal("Fullbokat", booking.DenialReason);
        Assert.Equal(DbFixture.TodayIn(provider).AddMonths(12), booking.DeleteAfter);
        Assert.Contains(email.Sent, e => e.Subject.Contains("bokningsförfrågan"));
    }

    [Fact]
    public async Task Close_GeneralInquiry_SetsClosed_NoEmail()
    {
        var provider = NewProvider(out var email);
        var id = await CreatePendingGeneralAsync(provider);

        using (var scope = provider.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<BookingService>().CloseAsync(id)).Succeeded);

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.Closed, booking.Status);
        Assert.Empty(email.Sent); // FR-80: admin replies outside the system
    }

    [Fact]
    public async Task Close_RejectsStayRequest()
    {
        var provider = NewProvider(out _);
        var id = await CreatePendingStayAsync(provider);

        using var scope = provider.CreateScope();
        Assert.False((await scope.ServiceProvider.GetRequiredService<BookingService>().CloseAsync(id)).Succeeded);
    }

    /// <summary>
    /// FR-108 counts the 40 days on the <b>Swedish</b> date. Approved at 00:30 Swedish time — still
    /// the previous day in UTC — a check-in 40 days out used to count as 41 and get no link. Also
    /// asserts the email states the expiry as 14:00 Swedish time (§2.6), end to end. A far-off year
    /// so the pinned stay cannot collide with other tests' bookings in the shared database.
    /// </summary>
    [Fact]
    public async Task Approve_JustAfterSwedishMidnight_CountsTheIssueWindowOnTheSwedishDate()
    {
        var approvedOn = new DateOnly(2036, 7, 15);
        var instant = StockholmTime.ToUtc(approvedOn, new TimeOnly(0, 30));
        var provider = NewProvider(out var email, pinnedNow: new DateTimeOffset(instant, TimeSpan.Zero));
        var checkIn = approvedOn.AddDays(CompletionTokenPolicy.IssueLeadDays); // 2036-08-24
        var id = await CreatePendingStayAsync(provider, checkIn);

        using (var scope = provider.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(id, DbFixture.EstimatedPrice)).Succeeded);

        var booking = await ReloadAsync(provider, id);
        Assert.False(string.IsNullOrEmpty(booking.CompletionToken));
        var approval = Assert.Single(email.Sent, e => e.Subject.Contains("godkänd"));
        Assert.Contains("giltig till och med <strong>10 augusti 2036 kl. 14:00</strong>", approval.HtmlBody);
    }

    [Fact]
    public async Task Cancel_FromApproved_SetsCancelled_AndSendsEmail()
    {
        var provider = NewProvider(out var email);
        var id = await CreatePendingStayAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<BookingService>();
            await svc.ApproveAsync(id, DbFixture.EstimatedPrice);
            Assert.True((await svc.CancelAsync(id, CancellationInitiator.PropertyInitiated, "Renovering", depositPaid: false)).Succeeded);
        }

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.NotNull(booking.CancelledAt);
        Assert.Equal(CancellationInitiator.PropertyInitiated, booking.CancellationInitiator);
        Assert.Equal("Renovering", booking.CancellationReason);
        Assert.Contains(email.Sent, e => e.Subject.Contains("avbokats"));
    }

    [Fact]
    public async Task Cancel_FromPending_IsBlocked()
    {
        var provider = NewProvider(out _);
        var id = await CreatePendingStayAsync(provider);

        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<BookingService>()
            .CancelAsync(id, CancellationInitiator.GuestInitiated, null, depositPaid: false);
        Assert.False(result.Succeeded); // §2.5: Pending is denied, not cancelled
    }

    // ── Deposit before confirmation (FR-115/116) ─────────────────────────────────

    [Fact]
    public async Task RequestDeposit_HoldsTheDates_SetsTheDueDate_AndSendsWhereToPay()
    {
        // Pinned: the due date is a distance from today (DepositPolicy.DueDate).
        var pinned = DateTimeOffset.UtcNow;
        var provider = NewProvider(out var email, pinnedNow: pinned);
        var id = await CreatePendingStayAsync(provider);

        DepositRequestResult result;
        using (var scope = provider.CreateScope())
            result = await scope.ServiceProvider.GetRequiredService<BookingService>().RequestDepositAsync(id, DbFixture.EstimatedPrice);
        Assert.True(result.Succeeded, result.Error);

        var booking = await ReloadAsync(provider, id);
        var expectedDue = DepositPolicy.DueDate(StockholmTime.DateOf(pinned.UtcDateTime), booking.CheckIn!.Value);
        Assert.Equal(BookingStatus.AwaitingDeposit, booking.Status);
        Assert.Equal(expectedDue, booking.DepositDueDate);
        Assert.Equal(expectedDue, result.DueDate);
        Assert.Null(booking.CompletionToken);  // no link before the booking is confirmed
        Assert.NotNull(booking.DeleteAfter);   // retention kept, like Pending (FR-91)

        var mail = Assert.Single(email.Sent);
        Assert.Contains("väntar på handpenning", mail.Subject);
        Assert.Contains("5713-3399", mail.HtmlBody);                         // the code-default payment details
        Assert.Contains(booking.BookingReference, mail.HtmlBody);
        Assert.Contains("återbetalas inte vid avbokning", mail.HtmlBody);

        // The price is stored as sent, and the email states it with the deposit (FR-115, §5.6).
        Assert.Equal(DbFixture.EstimatedPrice, booking.EstimatedPriceInclVat);
        Assert.Contains("21\u00A0250\u00A0kr", mail.HtmlBody);
        Assert.Contains("4\u00A0250\u00A0kr", mail.HtmlBody);
        Assert.DoesNotContain("separat", mail.HtmlBody);
    }

    [Fact]
    public async Task RequestDeposit_HoldsTheDates_AgainstAnotherInquiry()
    {
        var provider = NewProvider(out _);
        var held = await CreatePendingStayAsync(provider);
        var heldBooking = await ReloadAsync(provider, held);
        var rival = await CreatePendingStayAsync(provider, heldBooking.CheckIn);

        using var scope = provider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<BookingService>();
        Assert.True((await svc.RequestDepositAsync(held, DbFixture.EstimatedPrice)).Succeeded);

        // The waiting booking occupies its nights (FR-10/FR-113): neither path can say yes to the rival.
        Assert.False((await svc.ApproveAsync(rival, DbFixture.EstimatedPrice)).Succeeded);
        Assert.False((await svc.RequestDepositAsync(rival, DbFixture.EstimatedPrice)).Succeeded);
    }

    [Fact]
    public async Task RequestDeposit_RunsTheSameChecksAsApproval_BuildingRequired()
    {
        var provider = NewProvider(out var email);
        Guid id;
        using (var scope = provider.CreateScope())
        {
            var checkIn = DbFixture.StayWindow(scope.ServiceProvider, Interlocked.Increment(ref _window), FarOutDays);
            var b = await scope.ServiceProvider.GetRequiredService<BookingService>().CreateStayRequestAsync(new StayRequestInput
            {
                ContactName = "Gäst",
                ContactEmail = $"guest-{Guid.NewGuid():N}@test.se",
                CheckIn = checkIn,
                CheckOut = checkIn.AddDays(2),
                OvernightGuestCount = 4,
                MealPlan = MealPlan.FullBoardAll,
                AcceptTerms = true
            }, createdByAdmin: true);
            id = b.Id; // no building assigned
        }

        using (var scope = provider.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<BookingService>().RequestDepositAsync(id, DbFixture.EstimatedPrice);
            Assert.False(result.Succeeded);
            Assert.Equal("Tilldela minst en byggnad innan bokningen godkänns.", result.Error);
        }

        Assert.Equal(BookingStatus.Pending, (await ReloadAsync(provider, id)).Status);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task Confirm_AfterDeposit_ApprovesTheBooking_WithTheConfirmationEmail()
    {
        var provider = NewProvider(out var email);
        var id = await CreatePendingStayAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<BookingService>();
            Assert.True((await svc.RequestDepositAsync(id, DbFixture.EstimatedPrice)).Succeeded);
            Assert.True((await svc.ApproveAsync(id, null)).Succeeded);
        }

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.Approved, booking.Status);
        Assert.Null(booking.DeleteAfter);          // live in-progress state (FR-91)
        Assert.NotNull(booking.DepositDueDate);    // kept: the record that a deposit was asked for
        var confirmation = Assert.Single(email.Sent, e => e.Subject.Contains("bekräftad"));
        Assert.Contains("Er bokning är nu bekräftad", confirmation.HtmlBody);
        Assert.Equal(DbFixture.EstimatedPrice, booking.EstimatedPriceInclVat); // kept as sent
        Assert.Contains("21\u00A0250\u00A0kr", confirmation.HtmlBody);         // and stated again
    }

    // ── The estimated price (FR-38, §5.6) ────────────────────────────────────────

    [Fact]
    public async Task Approve_Directly_StoresThePrice_AndTheEmailStatesIt()
    {
        var provider = NewProvider(out var email);
        var id = await CreatePendingStayAsync(provider);

        using (var scope = provider.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(id, DbFixture.EstimatedPrice)).Succeeded);

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(DbFixture.EstimatedPrice, booking.EstimatedPriceInclVat);
        Assert.Null(booking.DepositDueDate); // no deposit on the direct path
        var mail = Assert.Single(email.Sent);
        Assert.Contains("Bokningens beräknade pris är <strong>21\u00A0250\u00A0kr</strong> inklusive moms", mail.HtmlBody);
        Assert.DoesNotContain("separat", mail.HtmlBody);
    }

    /// <summary>Saying yes without a valid price is refused on both paths, before anything changes or
    /// is sent — the email that says yes states the price (FR-38). Values as strings: InlineData
    /// cannot carry a decimal.</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("0", false)]
    [InlineData("-500", false)]
    [InlineData("21250.50", false)]
    [InlineData("5000001", false)]
    [InlineData(null, true)]
    [InlineData("0", true)]
    [InlineData("21250.50", true)]
    public async Task SayingYes_WithoutAValidPrice_IsRefused_AndNothingIsSent(string? price, bool askForDeposit)
    {
        var provider = NewProvider(out var email);
        var id = await CreatePendingStayAsync(provider);
        decimal? entered = price is null ? null : decimal.Parse(price, CultureInfo.InvariantCulture);

        using (var scope = provider.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<BookingService>();
            string? error;
            if (askForDeposit)
            {
                var result = await svc.RequestDepositAsync(id, entered);
                Assert.False(result.Succeeded);
                error = result.Error;
            }
            else
            {
                var result = await svc.ApproveAsync(id, entered);
                Assert.False(result.Succeeded);
                error = result.Error;
            }
            Assert.NotNull(error);
            Assert.Equal(DepositPolicy.ValidatePrice(entered), error);
        }

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Null(booking.EstimatedPriceInclVat);
        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task Confirm_AfterDeposit_RefusesANewPrice_ThePriceSentIsNeverChanged()
    {
        var provider = NewProvider(out var email);
        var id = await CreatePendingStayAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<BookingService>();
            Assert.True((await svc.RequestDepositAsync(id, DbFixture.EstimatedPrice)).Succeeded);
            var result = await svc.ApproveAsync(id, 30_000m);
            Assert.False(result.Succeeded);
            Assert.Equal("Priset har redan skickats till gästen och kan inte ändras.", result.Error);
        }

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.AwaitingDeposit, booking.Status);
        Assert.Equal(DbFixture.EstimatedPrice, booking.EstimatedPriceInclVat);
        Assert.Single(email.Sent); // the deposit request only
    }

    [Fact]
    public async Task Confirm_AfterDeposit_WithoutAStoredPrice_StillConfirms_WithTheOldWording()
    {
        // A booking asked for its deposit before prices were entered (§5.6) holds none — it must still
        // be confirmable, and its email keeps the "faktureras" wording rather than an empty price.
        var provider = NewProvider(out var email);
        var id = await CreatePendingStayAsync(provider);

        using (var scope = provider.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<BookingService>().RequestDepositAsync(id, DbFixture.EstimatedPrice)).Succeeded);
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var legacy = (await db.Bookings.FindAsync(id))!;
            legacy.EstimatedPriceInclVat = null; // as every booking asked before 2026-10
            await db.SaveChangesAsync();
        }

        using (var scope = provider.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<BookingService>().ApproveAsync(id, null)).Succeeded);

        var confirmation = Assert.Single(email.Sent, e => e.Subject.Contains("bekräftad"));
        Assert.Contains("Resterande belopp faktureras av oss", confirmation.HtmlBody);
        Assert.DoesNotContain("\u00A0kr", confirmation.HtmlBody);
    }

    /// <summary>Paying is the binding moment (D15). Cancelling a booking still waiting for its
    /// deposit asks whether the money arrived, and the answer decides what the guest is told.</summary>
    [Theory]
    [InlineData(false, "inte skyldiga oss något")]
    [InlineData(true, "Handpenningen återbetalas inte.")]
    public async Task Cancel_WhileAwaitingDeposit_TheAnswerAboutTheDepositPicksTheEmail(bool depositPaid, string expected)
    {
        var provider = NewProvider(out var email);
        var id = await CreatePendingStayAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<BookingService>();
            Assert.True((await svc.RequestDepositAsync(id, DbFixture.EstimatedPrice)).Succeeded);
            Assert.True((await svc.CancelAsync(id, CancellationInitiator.GuestInitiated, null, depositPaid)).Succeeded);
        }

        Assert.Equal(BookingStatus.Cancelled, (await ReloadAsync(provider, id)).Status);
        var mail = Assert.Single(email.Sent, e => e.Subject.Contains("avbokats"));
        Assert.Contains(expected, mail.HtmlBody);
    }

    [Fact]
    public async Task Cancel_ABookingApprovedDirectly_NeverClaimsADepositWasPaid()
    {
        // A stray `depositPaid: true` on a booking that was never asked for one must not put
        // "Handpenningen återbetalas inte" in the guest's email.
        var provider = NewProvider(out var email);
        var id = await CreatePendingStayAsync(provider);

        using (var scope = provider.CreateScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<BookingService>();
            Assert.True((await svc.ApproveAsync(id, DbFixture.EstimatedPrice)).Succeeded);
            Assert.True((await svc.CancelAsync(id, CancellationInitiator.GuestInitiated, null, depositPaid: true)).Succeeded);
        }

        var mail = Assert.Single(email.Sent, e => e.Subject.Contains("avbokats"));
        Assert.DoesNotContain("Handpenning", mail.HtmlBody);
        Assert.Contains("Eventuell avbokningsavgift följer", mail.HtmlBody);
    }

    [Fact]
    public async Task Approve_Twice_IsBlocked_ByStateMachine()
    {
        var provider = NewProvider(out _);
        var id = await CreatePendingStayAsync(provider);

        using var scope = provider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<BookingService>();
        Assert.True((await svc.ApproveAsync(id, DbFixture.EstimatedPrice)).Succeeded);
        Assert.False((await svc.ApproveAsync(id, DbFixture.EstimatedPrice)).Succeeded); // Approved -> Approved not allowed
    }

    /// <summary>
    /// The one-group-at-a-time invariant, enforced where it becomes true rather than where it is
    /// displayed (FR-10). Two Pending inquiries can sit in the inbox over the same week — the
    /// calendar cannot prevent that, because a Pending booking occupies nothing. Approving the
    /// second must fail.
    /// </summary>
    [Fact]
    public async Task Approve_OverDatesAnotherGroupHolds_IsBlocked()
    {
        var provider = NewProvider(out _);
        var checkIn = DbFixture.TodayIn(provider).AddDays(300);
        var first = await CreatePendingStayAsync(provider, checkIn: checkIn);
        var second = await CreatePendingStayAsync(provider, checkIn: checkIn);

        using var scope = provider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<BookingService>();

        Assert.True((await svc.ApproveAsync(first, DbFixture.EstimatedPrice)).Succeeded);

        var result = await svc.ApproveAsync(second, DbFixture.EstimatedPrice);
        Assert.False(result.Succeeded);
        Assert.Contains("en grupp i taget", result.Error);

        // Blocked, not silently swallowed: the second booking is still Pending and can be denied.
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(BookingStatus.Pending, (await db.Bookings.SingleAsync(b => b.Id == second)).Status);
    }

    /// <summary>
    /// A stay whose departure lands on the next group's arrival is refused too: the two turnover
    /// days after check-out are part of what a stay needs (FR-106). Judged on nights alone this
    /// pair does not overlap at all.
    /// </summary>
    [Fact]
    public async Task Approve_WhenDepartureCollidesWithTheNextArrival_IsBlocked()
    {
        var provider = NewProvider(out _);
        var firstCheckIn = DbFixture.TodayIn(provider).AddDays(400);
        var first = await CreatePendingStayAsync(provider, checkIn: firstCheckIn);

        // CreatePendingStayAsync books three nights, so the first stay checks out on +3 — the day
        // cleaning starts. A group arriving that day leaves no turnover window.
        var second = await CreatePendingStayAsync(provider, checkIn: firstCheckIn.AddDays(3));

        using var scope = provider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<BookingService>();

        Assert.True((await svc.ApproveAsync(first, DbFixture.EstimatedPrice)).Succeeded);
        Assert.False((await svc.ApproveAsync(second, DbFixture.EstimatedPrice)).Succeeded);
    }

    /// <summary>Forces a booking to a given status directly (bypassing the guest completion form)
    /// so the admin lifecycle-end transitions can be exercised in isolation.</summary>
    private static async Task SetStatusAsync(IServiceProvider provider, Guid id, BookingStatus status)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var booking = await db.Bookings.SingleAsync(b => b.Id == id);
        booking.Status = status;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task MarkArrived_FromDetailsComplete_SetsActive_NoEmail_KeepsDeleteAfterNull()
    {
        var provider = NewProvider(out var email);
        var id = await CreatePendingStayAsync(provider);
        await SetStatusAsync(provider, id, BookingStatus.DetailsComplete);

        using (var scope = provider.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<BookingService>().MarkArrivedAsync(id)).Succeeded);

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.Active, booking.Status);
        Assert.Null(booking.DeleteAfter); // Active is a live state (FR-91)
        Assert.Empty(email.Sent); // no guest email on "arrived"
    }

    [Fact]
    public async Task MarkCompleted_FromActive_SetsCompleted_SetsRetention_AndSendsThankYou()
    {
        var provider = NewProvider(out var email);
        var checkOut = new DateOnly(2026, 12, 4);
        var id = await CreatePendingStayAsync(provider, checkIn: checkOut.AddDays(-3));
        await SetStatusAsync(provider, id, BookingStatus.Active);

        using (var scope = provider.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<BookingService>().MarkCompletedAsync(id)).Succeeded);

        var booking = await ReloadAsync(provider, id);
        Assert.Equal(BookingStatus.Completed, booking.Status);
        Assert.Equal(checkOut.AddYears(2), booking.DeleteAfter); // FR-91: check-out + 2 years
        Assert.Contains(email.Sent, e => e.Subject.Contains("Tack för ditt besök"));
    }

    [Fact]
    public async Task MarkCompleted_FromDetailsComplete_IsBlocked_ByStateMachine()
    {
        var provider = NewProvider(out _);
        var id = await CreatePendingStayAsync(provider);
        await SetStatusAsync(provider, id, BookingStatus.DetailsComplete);

        using var scope = provider.CreateScope();
        // §2.5: Completed is only reachable from Active, not directly from DetailsComplete.
        Assert.False((await scope.ServiceProvider.GetRequiredService<BookingService>().MarkCompletedAsync(id)).Succeeded);
    }

    [Fact]
    public async Task MarkArrived_FromApproved_IsBlocked_ByStateMachine()
    {
        var provider = NewProvider(out _);
        var id = await CreatePendingStayAsync(provider);
        await SetStatusAsync(provider, id, BookingStatus.Approved);

        using var scope = provider.CreateScope();
        // §2.5: Active is only reachable from DetailsComplete.
        Assert.False((await scope.ServiceProvider.GetRequiredService<BookingService>().MarkArrivedAsync(id)).Succeeded);
    }

    [Fact]
    public async Task RegenerateLink_ReplacesToken_AndRequiresApproved()
    {
        var provider = NewProvider(out _, pinnedNow: DbFixture.FixedNow);
        // Inside the issue window, so approval really does produce a first token to replace.
        var id = await CreatePendingStayAsync(provider, checkIn: DbFixture.TodayIn(provider).AddDays(36));

        using var scope = provider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<BookingService>();

        Assert.False((await svc.RegenerateCompletionLinkAsync(id)).Succeeded); // not Approved yet

        await svc.ApproveAsync(id, DbFixture.EstimatedPrice);
        var first = (await ReloadAsync(provider, id)).CompletionToken;
        Assert.False(string.IsNullOrEmpty(first));

        Assert.True((await svc.RegenerateCompletionLinkAsync(id)).Succeeded);
        var second = (await ReloadAsync(provider, id)).CompletionToken;

        Assert.False(string.IsNullOrEmpty(second));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task RegenerateLink_SendsTheLinkEarly_ToABookingThatHasNoneYet()
    {
        // FR-49/108: this is how a guest who already knows their participants gets the form before
        // the automatic issue at 40 days. It must send the "you can fill in your details" email, not
        // a second copy of the approval the guest read months ago.
        var provider = NewProvider(out var email, pinnedNow: DbFixture.FixedNow);
        var id = await CreatePendingStayAsync(provider, checkIn: DbFixture.TodayIn(provider).AddDays(190));

        using var scope = provider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<BookingService>();
        await svc.ApproveAsync(id, DbFixture.EstimatedPrice);
        Assert.Null((await ReloadAsync(provider, id)).CompletionToken); // precondition: no link yet

        Assert.True((await svc.RegenerateCompletionLinkAsync(id)).Succeeded);

        var booking = await ReloadAsync(provider, id);
        Assert.False(string.IsNullOrEmpty(booking.CompletionToken));
        Assert.NotNull(booking.CompletionLinkSentAt);

        var linkMail = Assert.Single(email.Sent, e => e.Subject.Contains("fylla i era uppgifter"));
        Assert.Contains("/slutfor/", linkMail.HtmlBody);
        Assert.Single(email.Sent, e => e.Subject.Contains("godkänd")); // the approval was not re-sent
    }

    [Fact]
    public async Task GetBookingsAsync_FiltersByReference_FR36_92()
    {
        var provider = NewProvider(out _);
        var wantedId = await CreatePendingStayAsync(provider);
        await CreatePendingStayAsync(provider); // a second, unrelated booking in the same list

        using var scope = provider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<BookingService>();
        var wanted = await ReloadAsync(provider, wantedId);

        var exact = await svc.GetBookingsAsync(reference: wanted.BookingReference);
        Assert.Single(exact);
        Assert.Equal(wantedId, exact[0].Id);

        // Partial match (what an admin would actually type) also finds it.
        var partial = await svc.GetBookingsAsync(reference: wanted.BookingReference[..^2]);
        Assert.Contains(partial, b => b.Id == wantedId);

        var none = await svc.GetBookingsAsync(reference: "AG-9999-9999");
        Assert.Empty(none);
    }
}
