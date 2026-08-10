using System.Text.RegularExpressions;
using AkeroGarden.E2E.Infrastructure;
using AkeroGarden.Web.Core.Data.Entities;
using Deque.AxeCore.Playwright;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// Admin surface (Phase 6 slice 6) — the productivity redesign. Logs in with the seeded admin,
/// then asserts the operational dashboard, the status-badged bookings list, and the two-column
/// booking detail with its action rail. Accessibility (axe, no serious/critical) and responsive
/// reachability (sidebar collapses on mobile) are checked on the key screens. Behaviour/structure
/// only — no pixel diffing.
/// </summary>
public class AdminTests(E2EFixture fixture) : AdminE2ETestBase(fixture)
{
    /// <summary>Matches the toast's error variant as a whole class token, so it cannot be satisfied
    /// by a substring of some other class on the element.</summary>
    private static readonly Regex ErrorToast = new(@"\bform-status--error\b");

    /// <summary>
    /// Opens the collapsed (≤860px) admin menu via the hamburger. It's a CSS-only checkbox toggle
    /// (the layout is static SSR — no circuit), so a single click on the label reliably reveals the
    /// links; assert they're visible so a regression surfaces here.
    /// </summary>
    private async Task OpenMobileNavAsync()
    {
        await Page.Locator(".admin-nav__toggle").ClickAsync();
        await Assertions.Expect(Page.Locator("#admin-nav-links")).ToBeVisibleAsync();
    }

    /// <summary>Logs in through the real /login form and lands on the dashboard at "/".</summary>
    private async Task LoginAsync()
    {
        await Page.GotoAsync("/login");
        await Page.FillAsync("#email", E2EFixture.AdminEmail);
        await Page.FillAsync("#password", E2EFixture.AdminPassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Logga in" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/$"));
    }

    /// <summary>Logs in as an arbitrary account, without asserting where it lands — the landing page
    /// is role-dependent (FR-104) and is itself the thing under test in the Samordnare journey.</summary>
    private async Task LoginAsAsync(string email, string password)
    {
        await Page.GotoAsync("/login");
        await Page.FillAsync("#email", email);
        await Page.FillAsync("#password", password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Logga in" }).ClickAsync();
    }

    /// <summary>
    /// Signing in again from an already-signed-in session must work — the path an admin takes to
    /// switch accounts, by going to /login without logging out first.
    ///
    /// <para>Regression guard: /login signs the current user out during its render, but that only
    /// queues the cookie deletion; <c>HttpContext.User</c> stayed signed-in for the rest of that
    /// render, so the form's antiforgery token was minted for the old identity and the subsequent
    /// anonymous POST was rejected with "A valid antiforgery token was not provided with the
    /// request". The submit failed and the user sat on /login with no idea why.</para>
    /// </summary>
    [Fact]
    public async Task Signing_in_again_while_already_signed_in_succeeds()
    {
        await LoginAsync();

        await Page.GotoAsync("/login");
        await Page.FillAsync("#email", E2EFixture.AdminEmail);
        await Page.FillAsync("#password", E2EFixture.AdminPassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Logga in" }).ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/$"));
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Översikt" })).ToBeVisibleAsync();
    }

    private async Task LogoutAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Logga ut" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/login"));
    }

    [Fact]
    public async Task Login_lands_on_operational_dashboard()
    {
        await LoginAsync();

        // The dashboard topbar + the three operational queues.
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Översikt" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Väntar på svar" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Kommande ankomster" })).ToBeVisibleAsync();

        // The seeded Pending booking surfaces in the "Väntar på svar" queue.
        await Assertions.Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Familjen Pendlar" }).First).ToBeVisibleAsync();

        // …and drives the sidebar count badge.
        await Assertions.Expect(Page.Locator(".admin-nav a .count")).ToBeVisibleAsync();

        await CaptureAsync("admin-dashboard");
    }

    /// <summary>Optional visual capture for manual review (no pixel diffing). No-op unless
    /// E2E_CAPTURE_DIR is set.</summary>
    private async Task CaptureAsync(string name)
    {
        var dir = Environment.GetEnvironmentVariable("E2E_CAPTURE_DIR");
        if (dir is not null)
            await Page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{name}.png"), FullPage = true });
    }

    [Fact]
    public async Task Admin_can_create_a_manual_stay_booking()
    {
        // Regression guard (Phase 7): the shared StayRequestInput now requires a matching
        // ConfirmEmail (the guest-facing confirm-email safeguard). The admin form has no confirm
        // field — it mirrors ContactEmail → ConfirmEmail — so a manual booking must still submit
        // and land on the newly created booking's detail page.
        await LoginAsync();
        await Page.GotoAsync("/bokningar/ny");
        // Admin is Interactive Server: wait for the SignalR circuit to connect before filling, else
        // the @bind onchange events fire before the circuit is live and never reach the server model.
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.FillAsync("#mb-name", "Testgrupp Norrtälje");
        await Page.FillAsync("#mb-email", "grupp@example.se");
        await Page.FillAsync("#mb-checkin", "2026-09-01");
        await Page.FillAsync("#mb-checkout", "2026-09-03");
        await Page.FillAsync("#mb-guests", "8");
        await Page.SelectOptionAsync("#mb-mealplan", "FullBoardAll");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Skapa bokning" }).ClickAsync();

        // Lands on the new booking's detail page — proving the form submitted despite the required
        // ConfirmEmail (mirrored from ContactEmail), i.e. the Phase 7 change didn't break admin.
        await Assertions.Expect(Page).ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex(@"/bokningar/[0-9a-fA-F-]{36}$"));
    }

    [Fact]
    public async Task Manual_booking_invalid_phone_shows_a_styled_message_and_blocks_submit()
    {
        // Finding (Phase 7 smoke test): the manual-booking phone field was missing a
        // <ValidationMessage>, so an invalid phone silently blocked OnValidSubmit with no feedback —
        // unlike the public form. With the message wired, [OptionalPhone] surfaces the same Swedish
        // error and the form does NOT navigate to a new booking's detail page.
        await LoginAsync();
        await Page.GotoAsync("/bokningar/ny");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.FillAsync("#mb-name", "Testgrupp Telefon");
        await Page.FillAsync("#mb-email", "telefon@example.se");
        await Page.FillAsync("#mb-checkin", "2026-09-10");
        await Page.FillAsync("#mb-checkout", "2026-09-12");
        await Page.FillAsync("#mb-guests", "4");
        await Page.SelectOptionAsync("#mb-mealplan", "FullBoardAll");
        await Page.FillAsync("#mb-phone", "abc123");        // letters → rejected by [OptionalPhone]
        await Page.GetByRole(AriaRole.Button, new() { Name = "Skapa bokning" }).ClickAsync();

        await Assertions.Expect(Page.GetByText("Ange ett giltigt telefonnummer.")).ToBeVisibleAsync();
        // Did not create a booking: still on the manual-booking form, not a detail page.
        await Assertions.Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/bokningar/ny$"));
    }

    [Fact]
    public async Task Approval_warning_surfaces_when_dates_overlap_a_block()
    {
        // The seeded Pending "Familjen Pendlar" booking (12 guests, one 40-bed room, no block) shows
        // no approval warning. Seed a whole-property block over its dates (directly, as test setup)
        // and confirm the non-blocking warning renders on its detail page — the behaviour under test
        // is the UI rendering, verified through the browser; the block is removed afterwards so the
        // shared E2E app is left as we found it.
        var blockId = Guid.NewGuid();
        var start = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(33);
        var end = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(39);

        await using (var db = Fixture.NewDbContext())
        {
            db.BlockedPeriods.Add(new BlockedPeriod
            {
                Id = blockId,
                Scope = BlockScope.WholeProperty,
                StartDate = start,
                EndDate = end,
                Reason = "E2E approval-guard",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        try
        {
            await LoginAsync();
            await Page.GotoAsync("/bokningar");
            await Page.GetByRole(AriaRole.Link, new() { Name = "Familjen Pendlar" }).First.ClickAsync();
            await Assertions.Expect(
                Page.GetByText("Datumen överlappar en eller flera blockeringar")).ToBeVisibleAsync();
        }
        finally
        {
            await using var db = Fixture.NewDbContext();
            await db.BlockedPeriods.Where(b => b.Id == blockId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Bookings_list_shows_status_badges()
    {
        await LoginAsync();
        // The sidebar "Bokningar" link points at the list…
        await Assertions.Expect(Page.Locator(".admin-nav").GetByRole(AriaRole.Link, new() { Name = "Bokningar" }))
            .ToHaveAttributeAsync("href", "/bokningar");
        await Page.GotoAsync("/bokningar");

        await Assertions.Expect(Page.Locator("table.admin-table")).ToBeVisibleAsync();
        // The Pending booking carries the coloured status badge.
        await Assertions.Expect(Page.Locator(".badge.badge--pending").First).ToBeVisibleAsync();

        await CaptureAsync("admin-bookings-list");
    }

    [Fact]
    public async Task Booking_detail_has_action_rail()
    {
        await LoginAsync();
        await Page.GotoAsync("/bokningar");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Familjen Pendlar" }).First.ClickAsync();

        // Two-column detail with the sticky action rail; a Pending stay offers Approve.
        await Assertions.Expect(Page.Locator(".detail-grid .detail-rail")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Åtgärder" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Godkänn" })).ToBeVisibleAsync();

        await CaptureAsync("admin-booking-detail");
    }

    // REMOVED 2026-08-06 (demo-feedback slice 1): Save_feedback_shows_as_a_toast_within_the_viewport_
    // when_saving_far_down. It covered the Phase 8 client fix that turned the save confirmation into a
    // fixed toast, because the old top-anchored message was off-screen when saving low on the booking
    // detail page. It was driven through the payment-status save, which FR-42 removed.
    //
    // It is deleted rather than re-pointed because it has no honest vehicle left: a fixed-position
    // toast is trivially inside the viewport, so the assertion only means anything when the save
    // button starts below the fold. Measured, not assumed — the notes save sits at y=648 in a 1280x900
    // viewport, and the only lower control (Spara deltagaruppgifter) needs a booking with participants,
    // which the E2E fixture does not seed. Keeping it pointed at an above-the-fold save would have left
    // it permanently green while unable to detect the regression it exists to catch.
    //
    // COVERAGE GAP, deliberately logged rather than papered over: the toast-position fix now has no E2E
    // regression test. Reinstate in slice 2, which seeds a participant-bearing booking for the
    // building-assignment tests and will make Spara deltagaruppgifter reachable below the fold.

    // Slice 7: axe now sweeps every authenticated admin page — the operational dashboard, the
    // bookings list + manual-booking form, the calendar and blocks, and all six reference-data
    // CRUD screens plus accounts. (Booking detail needs a real {id} and is covered separately.)
    [Theory]
    [InlineData("/")]
    [InlineData("/bokningar")]
    [InlineData("/bokningar/ny")]
    [InlineData("/kalender")]
    [InlineData("/blockeringar")]
    [InlineData("/byggnader")]
    [InlineData("/matalternativ")]
    [InlineData("/priser")]
    [InlineData("/stadszoner")]
    [InlineData("/faciliteter")]
    [InlineData("/konton")]
    public async Task Admin_pages_have_no_serious_accessibility_violations(string path)
    {
        await LoginAsync();
        await Page.GotoAsync(path);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await AssertNoSeriousAxeViolationsAsync(path);
    }

    /// <summary>Booking detail is axe-checked via a real navigation (list → row) so it renders
    /// against the seeded Pending booking's {id} rather than a synthetic route.</summary>
    [Fact]
    public async Task Booking_detail_has_no_serious_accessibility_violations()
    {
        await LoginAsync();
        await Page.GotoAsync("/bokningar");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Familjen Pendlar" }).First.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await AssertNoSeriousAxeViolationsAsync("booking detail");
    }

    /// <summary>Admin invite lifecycle (Phase 9, FR-74): a fresh invite shows the pending badge
    /// with resend/remove actions; resend keeps it pending; remove deletes the row. No SMTP
    /// server in E2E (see E2EFixture), so this only asserts the UI state, not email delivery.
    /// Phase 11: the invite form moved from an always-visible card into the same slide-in panel
    /// pattern as every other reference-data page — "+ Bjud in användare" opens it.</summary>
    [Fact]
    public async Task Admin_can_resend_and_remove_a_pending_invite()
    {
        var email = $"{Guid.NewGuid()}@akerogarden.se";

        await LoginAsync();
        await Page.GotoAsync("/konton");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.GetByRole(AriaRole.Button, new() { Name = "+ Bjud in användare" }).ClickAsync();
        var panel = Page.Locator(".admin-panel");
        await Assertions.Expect(panel).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-open"));
        await Page.FillAsync("#invite-email", email);
        await panel.GetByRole(AriaRole.Button, new() { Name = "Skicka inbjudan" }).ClickAsync();

        var row = Page.Locator("tr", new() { HasText = email });
        await Assertions.Expect(row.Locator(".badge")).ToHaveTextAsync("Väntar på aktivering");

        await row.GetByRole(AriaRole.Button, new() { Name = "Skicka ny inbjudan" }).ClickAsync();
        await Assertions.Expect(Page.Locator(".form-status")).ToContainTextAsync("Ny inbjudan skickad");
        await Assertions.Expect(row.Locator(".badge")).ToHaveTextAsync("Väntar på aktivering");

        // "Ta bort" now opens a confirmation dialog (Phase 11); confirm it to actually remove the invite.
        await row.GetByRole(AriaRole.Button, new() { Name = "Ta bort" }).ClickAsync();
        await Page.GetByRole(AriaRole.Dialog)
            .GetByRole(AriaRole.Button, new() { Name = "Ta bort" }).ClickAsync();
        await Assertions.Expect(Page.Locator(".form-status")).ToContainTextAsync("har tagits bort");
        await Assertions.Expect(Page.Locator("tr", new() { HasText = email })).Not.ToBeVisibleAsync();
    }

    /// <summary>Phase 11 (audit-trail work): an invited admin sets their own required first name,
    /// last name and phone when activating their account (FR-74, §6.4). Drives the real flow end to
    /// end: invite through the Konton panel, follow the actual setup link (minted via the
    /// Development-only /e2e/setup-link seam, since there is no SMTP in E2E), prove the profile
    /// fields are required (empty submit is rejected and does NOT activate), then complete activation
    /// and cross-check the database that the profile landed and setup was stamped.</summary>
    [Fact]
    public async Task Invited_admin_sets_required_profile_fields_on_activation()
    {
        var email = $"{Guid.NewGuid():N}@akerogarden.se";
        const string password = "E2e-Setup-Pass1!";

        try
        {
            // 1. Invite the new admin through the real Konton panel (creates a pending account).
            await LoginAsync();
            await Page.GotoAsync("/konton");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Page.GetByRole(AriaRole.Button, new() { Name = "+ Bjud in användare" }).ClickAsync();
            var panel = Page.Locator(".admin-panel");
            await Assertions.Expect(panel).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-open"));
            await Page.FillAsync("#invite-email", email);
            await panel.GetByRole(AriaRole.Button, new() { Name = "Skicka inbjudan" }).ClickAsync();
            await Assertions.Expect(Page.Locator("tr", new() { HasText = email }).Locator(".badge"))
                .ToHaveTextAsync("Väntar på aktivering");

            // 2. Fetch the real setup link (the app builds a valid data-protected code + reset token).
            var resp = await Page.APIRequest.GetAsync($"/e2e/setup-link?email={Uri.EscapeDataString(email)}");
            Assert.True(resp.Ok, $"setup-link seam returned {resp.Status}");
            var setupPathAndQuery = new Uri(await resp.TextAsync()).PathAndQuery;

            // 3. The activation page shows the three required profile fields.
            await Page.GotoAsync(setupPathAndQuery);
            await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Aktivera ditt konto" })).ToBeVisibleAsync();
            await Assertions.Expect(Page.Locator("#firstName")).ToBeVisibleAsync();
            await Assertions.Expect(Page.Locator("#lastName")).ToBeVisibleAsync();
            await Assertions.Expect(Page.Locator("#phone")).ToBeVisibleAsync();

            // 4. Password valid but profile empty → rejected, and the account is NOT activated.
            await Page.FillAsync("#password", password);
            await Page.FillAsync("#confirm", password);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Aktivera konto" }).ClickAsync();
            await Assertions.Expect(Page.Locator(".form-error"))
                .ToContainTextAsync("Förnamn, efternamn och telefonnummer krävs.");
            await using (var db = Fixture.NewDbContext())
                Assert.Null((await db.Users.SingleAsync(u => u.Email == email)).SetupCompletedAt);

            // 5. Complete activation with the profile filled in (re-fill after the post re-render).
            await Page.FillAsync("#firstName", "Bengt");
            await Page.FillAsync("#lastName", "Bengtsson");
            await Page.FillAsync("#phone", "070-987 65 43");
            await Page.FillAsync("#password", password);
            await Page.FillAsync("#confirm", password);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Aktivera konto" }).ClickAsync();
            await Assertions.Expect(Page.GetByText("Ditt konto är aktiverat.")).ToBeVisibleAsync();

            // 6. Honest cross-check: the profile persisted and activation was stamped.
            await using (var db = Fixture.NewDbContext())
            {
                var user = await db.Users.SingleAsync(u => u.Email == email);
                Assert.Equal("Bengt", user.FirstName);
                Assert.Equal("Bengtsson", user.LastName);
                Assert.Equal("070-987 65 43", user.PhoneNumber);
                Assert.NotNull(user.SetupCompletedAt);
            }
        }
        finally
        {
            await using var db = Fixture.NewDbContext();
            await db.Users.Where(u => u.Email == email).ExecuteDeleteAsync();
        }
    }

    /// <summary>
    /// FR-104 end to end, in a real browser with a real Identity cookie: an Administratör invites a
    /// Samordnare account, that account activates, logs in, and is then held inside its own corner of
    /// the admin panel.
    ///
    /// <para>This is the journey the integration matrix cannot cover — it substitutes the sign-in, so
    /// it never exercises the login redirect, the navigation an actual coordinator sees, or the
    /// cookie handler's redirect to the no-permission page. Each assertion below names the failure it
    /// guards against, because most of them are silent: landing on "/" would show an access-denied
    /// wall immediately after logging in, and a stray nav link is a dead end rather than a leak.</para>
    /// </summary>
    [Fact]
    public async Task Samordnare_account_lands_on_its_own_lists_and_cannot_reach_bookings()
    {
        var email = $"{Guid.NewGuid():N}@akerogarden.se";
        const string password = "E2e-Samordnare-Pass1!";
        var bookingId = Guid.NewGuid();
        var reference = $"AG-E2E-{Guid.NewGuid():N}"[..20];

        try
        {
            // A booking with downloadable lists. Seeded by this test rather than taken from the
            // fixture because the fixture seeds none in DetailsComplete — one only appears if the
            // completion-submit test happens to have run first, which would make this order-dependent.
            var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(60);
            await using (var db = Fixture.NewDbContext())
            {
                db.Bookings.Add(new Booking
                {
                    Id = bookingId,
                    BookingReference = reference,
                    BookingType = BookingType.StayRequest,
                    Status = BookingStatus.DetailsComplete,
                    ContactName = "Karin Karlsson",
                    ContactEmail = "karin@example.se",
                    CheckIn = checkIn,
                    CheckOut = checkIn.AddDays(2),
                    OvernightGuestCount = 1,
                    MealPlan = MealPlan.SelfCateringAll,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    DeleteAfter = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1),
                    Participants =
                    {
                        new Participant
                        {
                            Id = Guid.NewGuid(),
                            FullName = "Karin Karlsson",
                            ParticipantType = ParticipantType.Overnight,
                            AgeCategory = AgeCategory.Adult,
                        }
                    }
                });
                await db.SaveChangesAsync();
            }

            // 1. An Administratör invites the account with the Samordnare role.
            await LoginAsync();
            await Page.GotoAsync("/konton");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Page.GetByRole(AriaRole.Button, new() { Name = "+ Bjud in användare" }).ClickAsync();
            var panel = Page.Locator(".admin-panel");
            await Assertions.Expect(panel).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-open"));
            await Page.FillAsync("#invite-email", email);
            await Page.SelectOptionAsync("#invite-role", "Samordnare");
            await panel.GetByRole(AriaRole.Button, new() { Name = "Skicka inbjudan" }).ClickAsync();

            // The row must show the chosen role — picking Samordnare and silently creating a full
            // administrator is the worst possible outcome of this page, and nothing else would show it.
            await Assertions.Expect(Page.Locator("tr", new() { HasText = email }))
                .ToContainTextAsync("Samordnare");

            // 2. Activate it through the real setup link.
            var resp = await Page.APIRequest.GetAsync($"/e2e/setup-link?email={Uri.EscapeDataString(email)}");
            Assert.True(resp.Ok, $"setup-link seam returned {resp.Status}");
            await Page.GotoAsync(new Uri(await resp.TextAsync()).PathAndQuery);
            await Page.FillAsync("#firstName", "Sara");
            await Page.FillAsync("#lastName", "Samordnare");
            await Page.FillAsync("#phone", "070-111 22 33");
            await Page.FillAsync("#password", password);
            await Page.FillAsync("#confirm", password);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Aktivera konto" }).ClickAsync();
            await Assertions.Expect(Page.GetByText("Ditt konto är aktiverat.")).ToBeVisibleAsync();

            // 3. Log in as the coordinator, straight from the still-signed-in admin session — no
            //    logout first, deliberately: that is how someone actually switches accounts, and it
            //    is the path that surfaced the /login antiforgery bug.
            //    The landing page must be /listor — "/" is the dashboard, so landing there would put
            //    them on an access-denied page the instant they log in.
            await LoginAsAsync(email, password);
            await Assertions.Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/listor$"));

            // 4. The navigation offers only what this role can actually open. A leftover link is a
            //    dead end, not a leak — but it is the kind of thing nobody notices until a user clicks.
            var nav = Page.Locator("#admin-nav-links");
            await Assertions.Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Deltagar- och matlistor" })).ToBeVisibleAsync();
            await Assertions.Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Bokningar" })).ToHaveCountAsync(0);
            await Assertions.Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Administratörskonton" })).ToHaveCountAsync(0);
            await Assertions.Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Översikt" })).ToHaveCountAsync(0);

            // 5. Typing the URL is the real test of the boundary — the pages are gated, not the links.
            foreach (var forbidden in new[] { "/", "/konton", "/bokningar", $"/bokningar/{bookingId}" })
            {
                await Page.GotoAsync(forbidden);
                await Assertions.Expect(Page).ToHaveURLAsync(
                    new System.Text.RegularExpressions.Regex(@"/ingen-behorighet"));
                // Not the login form: they are signed in, and being told to log in again reads as a
                // broken session rather than a permission boundary.
                await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Du har inte behörighet" }))
                    .ToBeVisibleAsync();
            }

            // 6. What the role is *for*: find the booking and actually get the file out.
            await Page.GotoAsync("/listor");
            await Assertions.Expect(Page.GetByText(reference).First).ToBeVisibleAsync();

            // No guest contact data on the way to the download (D4).
            await Assertions.Expect(Page.GetByText("karin@example.se")).ToHaveCountAsync(0);
            await Assertions.Expect(Page.GetByText("Karin Karlsson")).ToHaveCountAsync(0);

            await Page.GetByRole(AriaRole.Link, new() { Name = "Öppna →" }).First.ClickAsync();
            await Assertions.Expect(Page).ToHaveURLAsync(
                new System.Text.RegularExpressions.Regex($"/listor/{bookingId}"));

            var download = await Page.RunAndWaitForDownloadAsync(async () =>
                await Page.Locator("dd").First.GetByRole(AriaRole.Link, new() { Name = "PDF" }).ClickAsync());

            // The reference is in the filename (FR-92) — without it every booking downloads as the
            // same "deltagarlista.pdf" and they collide in the downloads folder.
            Assert.Contains(reference, download.SuggestedFilename);
            var path = await download.PathAsync();
            Assert.False(string.IsNullOrEmpty(path), "the PDF link produced no file");
            Assert.True(new FileInfo(path!).Length > 0, "the downloaded PDF was empty");

            await LogoutAsync();
        }
        finally
        {
            await using var db = Fixture.NewDbContext();
            await db.Participants.Where(p => p.BookingId == bookingId).ExecuteDeleteAsync();
            await db.Bookings.Where(b => b.Id == bookingId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Email == email).ExecuteDeleteAsync();
        }
    }

    /// <summary>Phase 11: irreversible "Ta bort" opens a confirmation dialog. Reference-data pages
    /// were reworked to the same row+slide-in-panel pattern as Byggnader (Aktivera/Ta bort moved
    /// off the row and into the panel footer, row keeps only Redigera) — Ta bort now lives inside
    /// the panel, not inline in the row. Seed an unreferenced facility, prove Avbryt keeps the
    /// row, and that confirming actually removes it (reaches the "har tagits bort" toast and the
    /// row disappears) — the real outcome, not merely that a dialog renders.</summary>
    [Fact]
    public async Task Delete_facility_asks_for_confirmation_then_removes_the_row()
    {
        var name = $"E2E Radera {Guid.NewGuid():N}";
        var id = Guid.NewGuid();

        await using (var db = Fixture.NewDbContext())
        {
            db.Facilities.Add(new Facility { Id = id, Name = name, IsActive = true });
            await db.SaveChangesAsync();
        }

        try
        {
            await LoginAsync();
            await Page.GotoAsync("/faciliteter");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var row = Page.Locator("tr", new() { HasText = name });
            await Assertions.Expect(row).ToBeVisibleAsync();

            await row.GetByRole(AriaRole.Button, new() { Name = "Redigera" }).ClickAsync();
            var panel = Page.Locator(".admin-panel");
            await Assertions.Expect(panel).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-open"));

            // Open the dialog, then cancel — the row must survive (nothing deleted).
            await panel.GetByRole(AriaRole.Button, new() { Name = "Ta bort" }).ClickAsync();
            var dialog = Page.GetByRole(AriaRole.Dialog);
            await Assertions.Expect(dialog).ToBeVisibleAsync();
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Avbryt" }).ClickAsync();
            await Assertions.Expect(dialog).Not.ToBeVisibleAsync();
            await Assertions.Expect(row).ToBeVisibleAsync();

            // Open again and confirm — the facility is actually deleted.
            await panel.GetByRole(AriaRole.Button, new() { Name = "Ta bort" }).ClickAsync();
            await Page.GetByRole(AriaRole.Dialog)
                .GetByRole(AriaRole.Button, new() { Name = "Ta bort" }).ClickAsync();
            await Assertions.Expect(Page.Locator(".form-status")).ToContainTextAsync("har tagits bort");
            await Assertions.Expect(Page.Locator("tr", new() { HasText = name })).Not.ToBeVisibleAsync();
        }
        finally
        {
            await using var db = Fixture.NewDbContext();
            await db.Facilities.Where(f => f.Id == id).ExecuteDeleteAsync();
        }
    }

    /// <summary>Phase 11: the Byggnader tree rework (PhysicalBuilding → Building → Room in one
    /// page instead of two disconnected flat-table pages). Drives the real hierarchy end to end
    /// through the UI: expand an empty physical building, add a building under it via the
    /// contextual "+ Lägg till" row, add a room under that building, then edit the room — each
    /// step re-opens the tree state to prove the new node actually landed in the right place,
    /// not just that a toast appeared.</summary>
    [Fact]
    public async Task Byggnader_tree_builds_out_and_edits_the_full_hierarchy_through_the_panel()
    {
        var pbName = $"E2E Fysisk {Guid.NewGuid():N}";
        var pbId = Guid.NewGuid();
        var buildingName = $"E2E Byggnad {Guid.NewGuid():N}";
        var roomName = $"E2E Rum {Guid.NewGuid():N}";

        await using (var db = Fixture.NewDbContext())
        {
            db.PhysicalBuildings.Add(new PhysicalBuilding { Id = pbId, Name = pbName, IsActive = true });
            await db.SaveChangesAsync();
        }

        try
        {
            await LoginAsync();
            await Page.GotoAsync("/byggnader");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // NetworkIdle is not a readiness signal on an Interactive Server page: the prerendered
            // "Laddar…" is served long before the circuit connects and OnInitializedAsync loads the
            // tree. Locally that gap is under a second, so asserting straight away passed; on the
            // slower CI agent it exceeded the 5s expect timeout and this test failed there while
            // passing 3/3 here. Wait for the placeholder to clear — the actual condition — rather
            // than raising a timeout and hoping.
            await Assertions.Expect(Page.Locator(".admin-empty", new() { HasText = "Laddar…" }))
                .ToHaveCountAsync(0, new() { Timeout = 15000 });

            var pbRow = Page.Locator(".bldg-row--l0", new() { HasText = pbName });
            await Assertions.Expect(pbRow).ToBeVisibleAsync();

            // .bldg-children is a sibling of the row, not a descendant — and the empty-state text and
            // "+ Lägg till" wording are generic, so on the shared E2E database other physical
            // buildings/rooms created by other tests can carry the exact same text. Scope to this
            // row's own children container (its immediate following sibling) rather than a page-wide
            // text search, or the assertion is ambiguous the moment more than one exists.
            ILocator ChildrenOf(ILocator row) =>
                row.Locator("xpath=following-sibling::div[contains(concat(' ', normalize-space(@class), ' '), ' bldg-children ')][1]");

            // Expand the (empty) physical building — its own empty state + nested add-row appear.
            await pbRow.Locator(".bldg-twist").ClickAsync();
            var pbChildren = ChildrenOf(pbRow);
            await Assertions.Expect(pbChildren).ToContainTextAsync("Inga boendeenheter i denna fysiska byggnad ännu.");
            var addBuildingRow = pbChildren.GetByText($"+ Lägg till byggnad i {pbName}");
            await Assertions.Expect(addBuildingRow).ToBeVisibleAsync();

            // Create the building — the panel pre-scopes it to this physical building.
            await addBuildingRow.ClickAsync();
            var panel = Page.Locator(".admin-panel");
            await Assertions.Expect(panel).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-open"));
            await Page.FillAsync("#b-name", buildingName);
            await panel.GetByRole(AriaRole.Button, new() { Name = "Spara" }).ClickAsync();
            await Assertions.Expect(Page.Locator(".form-status")).ToContainTextAsync("har lagts till");

            var buildingRow = Page.Locator(".bldg-row--l1", new() { HasText = buildingName });
            await Assertions.Expect(buildingRow).ToBeVisibleAsync();

            // The new building is auto-expanded (empty-room state + nested add-row visible without an extra click).
            var buildingChildren = ChildrenOf(buildingRow);
            await Assertions.Expect(buildingChildren).ToContainTextAsync("Inga rum i denna byggnad ännu.");
            await buildingChildren.GetByText($"+ Lägg till rum i {buildingName}").ClickAsync();
            await Page.FillAsync("#r-name", roomName);
            await Page.FillAsync("#r-beds", "3");
            await panel.GetByRole(AriaRole.Button, new() { Name = "Spara" }).ClickAsync();
            await Assertions.Expect(Page.Locator(".form-status")).ToContainTextAsync("har lagts till");

            var roomRow = Page.Locator(".bldg-row--l2", new() { HasText = roomName });
            await Assertions.Expect(roomRow).ToBeVisibleAsync();
            await Assertions.Expect(roomRow).ToContainTextAsync("3 bäddar");

            // Edit the room — the panel reopens pre-filled and the change reaches the row, not just the toast.
            await roomRow.ClickAsync();
            await Assertions.Expect(Page.Locator("#r-beds")).ToHaveValueAsync("3");
            await Page.FillAsync("#r-beds", "5");
            await panel.GetByRole(AriaRole.Button, new() { Name = "Spara" }).ClickAsync();
            await Assertions.Expect(Page.Locator(".form-status")).ToContainTextAsync("har uppdaterats");
            await Assertions.Expect(roomRow).ToContainTextAsync("5 bäddar");
        }
        finally
        {
            await using var db = Fixture.NewDbContext();
            await db.Rooms.Where(r => r.Name == roomName).ExecuteDeleteAsync();
            await db.Buildings.Where(b => b.Name == buildingName).ExecuteDeleteAsync();
            await db.PhysicalBuildings.Where(p => p.Id == pbId).ExecuteDeleteAsync();
        }
    }

    /// <summary>Phase 11: the no-hard-delete-when-referenced rule (FR-16/20 pattern), driven through
    /// the new tree/panel UI rather than the service layer directly (that's already covered by
    /// PhysicalBuildingIntegrationTests/BuildingRoomIntegrationTests) — proves the panel's "Ta bort"
    /// honestly reflects the same guard: blocked while a child exists, and only actually deletes
    /// bottom-up once each level is empty.</summary>
    [Fact]
    public async Task Byggnader_hard_delete_is_blocked_while_referenced_then_succeeds_bottom_up()
    {
        var pbName = $"E2E Radera fysisk {Guid.NewGuid():N}";
        var pbId = Guid.NewGuid();
        var buildingName = $"E2E Radera byggnad {Guid.NewGuid():N}";
        var buildingId = Guid.NewGuid();
        var roomName = $"E2E Radera rum {Guid.NewGuid():N}";
        var roomId = Guid.NewGuid();

        await using (var db = Fixture.NewDbContext())
        {
            db.PhysicalBuildings.Add(new PhysicalBuilding { Id = pbId, Name = pbName, IsActive = true });
            db.Buildings.Add(new Building { Id = buildingId, Name = buildingName, PhysicalBuildingId = pbId, IsActive = true });
            db.Rooms.Add(new Room { Id = roomId, BuildingId = buildingId, Name = roomName, BedCount = 2, IsActive = true });
            await db.SaveChangesAsync();
        }

        try
        {
            await LoginAsync();
            await Page.GotoAsync("/byggnader");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Both levels start collapsed — expand the physical building, then the building, so the
            // nested rows are actually visible (not just present in the DOM behind display:none).
            var pbRowInitial = Page.Locator(".bldg-row--l0", new() { HasText = pbName });
            await pbRowInitial.Locator(".bldg-twist").ClickAsync();
            var buildingRow = Page.Locator(".bldg-row--l1", new() { HasText = buildingName });
            await Assertions.Expect(buildingRow).ToBeVisibleAsync();
            await buildingRow.Locator(".bldg-twist").ClickAsync();

            // Blocked: the building still has a room. The panel's own "Ta bort" reaches the same
            // service-layer guard as the service tests — this proves the UI path honestly reflects it.
            await buildingRow.ClickAsync();
            var panel = Page.Locator(".admin-panel");
            await panel.GetByRole(AriaRole.Button, new() { Name = "Ta bort" }).ClickAsync();
            await Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Ta bort" }).ClickAsync();
            await Assertions.Expect(Page.Locator(".form-status")).ToContainTextAsync("kan inte tas bort");
            // A blocked delete must not look like a completed one. Until 2026-08-10 both used the
            // identical toast, so "kan inte tas bort" was as easy to miss as "har sparats".
            await Assertions.Expect(Page.Locator(".form-status")).ToHaveClassAsync(ErrorToast);
            await Assertions.Expect(buildingRow).ToBeVisibleAsync();

            // Delete the room first — now the building is actually deletable.
            var roomRow = Page.Locator(".bldg-row--l2", new() { HasText = roomName });
            await Assertions.Expect(roomRow).ToBeVisibleAsync();
            await roomRow.ClickAsync();
            await panel.GetByRole(AriaRole.Button, new() { Name = "Ta bort" }).ClickAsync();
            await Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Ta bort" }).ClickAsync();
            await Assertions.Expect(Page.Locator(".form-status")).ToContainTextAsync("har tagits bort");
            // The other half of the claim: the success toast is *not* styled as an error. Asserting
            // only the error side would pass even if every toast were red.
            await Assertions.Expect(Page.Locator(".form-status")).Not.ToHaveClassAsync(ErrorToast);
            await Assertions.Expect(roomRow).Not.ToBeVisibleAsync();

            await buildingRow.ClickAsync();
            await panel.GetByRole(AriaRole.Button, new() { Name = "Ta bort" }).ClickAsync();
            await Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Ta bort" }).ClickAsync();
            await Assertions.Expect(Page.Locator(".form-status")).ToContainTextAsync("har tagits bort");
            await Assertions.Expect(buildingRow).Not.ToBeVisibleAsync();

            await pbRowInitial.ClickAsync();
            await panel.GetByRole(AriaRole.Button, new() { Name = "Ta bort" }).ClickAsync();
            await Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Ta bort" }).ClickAsync();
            await Assertions.Expect(Page.Locator(".form-status")).ToContainTextAsync("har tagits bort");
            await Assertions.Expect(pbRowInitial).Not.ToBeVisibleAsync();
        }
        finally
        {
            await using var db = Fixture.NewDbContext();
            await db.Rooms.Where(r => r.Id == roomId).ExecuteDeleteAsync();
            await db.Buildings.Where(b => b.Id == buildingId).ExecuteDeleteAsync();
            await db.PhysicalBuildings.Where(p => p.Id == pbId).ExecuteDeleteAsync();
        }
    }

    /// <summary>Phase 11: the mobile-first rework of the Byggnader tree — rows stack, the numeric
    /// columns fold into one summary line, and the whole row (not just the explicit Redigera
    /// button) opens the edit panel, which itself goes full-screen. Asserts the actual behaviour
    /// (values reach the row, the panel is reachable and operable) at 360px, not just that some
    /// mobile CSS class is present.</summary>
    [Fact]
    public async Task Byggnader_tree_stacks_and_stays_operable_on_mobile()
    {
        var pbName = $"E2E Mobil {Guid.NewGuid():N}";
        var pbId = Guid.NewGuid();
        var buildingName = $"E2E Mobil byggnad {Guid.NewGuid():N}";
        var buildingId = Guid.NewGuid();
        var roomName = $"E2E Mobil rum {Guid.NewGuid():N}";
        var roomId = Guid.NewGuid();

        await using (var db = Fixture.NewDbContext())
        {
            db.PhysicalBuildings.Add(new PhysicalBuilding { Id = pbId, Name = pbName, IsActive = true });
            db.Buildings.Add(new Building { Id = buildingId, Name = buildingName, PhysicalBuildingId = pbId, IsActive = true });
            db.Rooms.Add(new Room { Id = roomId, BuildingId = buildingId, Name = roomName, BedCount = 4, IsActive = true });
            await db.SaveChangesAsync();
        }

        try
        {
            await LoginAsync();
            await Page.SetViewportSizeAsync(Mobile.Width, Mobile.Height);
            await Page.GotoAsync("/byggnader");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var pbRow = Page.Locator(".bldg-row--l0", new() { HasText = pbName });
            await Assertions.Expect(pbRow).ToBeVisibleAsync();

            // The numeric columns are hidden on mobile; the same counts fold into the summary line instead.
            await Assertions.Expect(pbRow.Locator(".bldg-row__stats .num").First).Not.ToBeVisibleAsync();
            await Assertions.Expect(pbRow.Locator(".bldg-row__summary")).ToContainTextAsync("byggnad");

            // Expand via the chevron (its hit area is enlarged on mobile, but the element itself is unchanged).
            await pbRow.Locator(".bldg-twist").ClickAsync();
            var buildingRow = Page.Locator(".bldg-row--l1", new() { HasText = buildingName });
            await Assertions.Expect(buildingRow).ToBeVisibleAsync();
            await buildingRow.Locator(".bldg-twist").ClickAsync();
            var roomRow = Page.Locator(".bldg-row--l2", new() { HasText = roomName });
            await Assertions.Expect(roomRow).ToBeVisibleAsync();

            // Tapping the row body (not a button) still opens the panel — the forgiving mobile hit area.
            await roomRow.Locator(".bldg-row__summary").ClickAsync();
            var panel = Page.Locator(".admin-panel");
            await Assertions.Expect(panel).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-open"));

            // The panel goes full-screen and actually saves an edit on mobile.
            var panelBox = await panel.BoundingBoxAsync();
            Assert.NotNull(panelBox);
            Assert.True(panelBox!.Width >= Mobile.Width - 1, $"Panel should be full-width on mobile (width={panelBox.Width}).");

            await Page.FillAsync("#r-beds", "6");
            await panel.GetByRole(AriaRole.Button, new() { Name = "Spara" }).ClickAsync();
            await Assertions.Expect(Page.Locator(".form-status")).ToContainTextAsync("har uppdaterats");
            await Assertions.Expect(roomRow).ToContainTextAsync("6 bäddar");

            var docScrollWidth = await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
            Assert.True(docScrollWidth <= Mobile.Width + 1,
                $"Byggnader page overflows horizontally at {Mobile.Width}px: scrollWidth={docScrollWidth}.");
        }
        finally
        {
            await using var db = Fixture.NewDbContext();
            await db.Rooms.Where(r => r.Id == roomId).ExecuteDeleteAsync();
            await db.Buildings.Where(b => b.Id == buildingId).ExecuteDeleteAsync();
            await db.PhysicalBuildings.Where(p => p.Id == pbId).ExecuteDeleteAsync();
        }
    }

    /// <summary>Phase 11: guest-facing booking actions gate behind a confirmation dialog. On a
    /// throwaway Pending stay, "Godkänn" opens the dialog; Avbryt leaves it Pending (the approval
    /// email/one-way transition does NOT fire), and confirming actually approves it (reaches the
    /// "godkänd" toast). Seeded/cleaned up here so the shared Pending booking is untouched.</summary>
    [Fact]
    public async Task Approving_without_a_building_assignment_is_refused()
    {
        // FR-103/D1 driven through the real UI: the approval email names the assigned buildings, so
        // approving without them would send the guest an incomplete answer. Asserts the booking is
        // genuinely still Pending afterwards, not merely that a message appeared.
        var id = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await using (var db = Fixture.NewDbContext())
        {
            db.Bookings.Add(new Booking
            {
                Id = id,
                BookingReference = $"AG-E2E-{Guid.NewGuid():N}"[..20],
                BookingType = BookingType.StayRequest,
                Status = BookingStatus.Pending,
                ContactName = "Utan Byggnad",
                ContactEmail = "utanbyggnad@example.se",
                // Inside the 40-day issue window on purpose: a successful approval here *would* have
                // issued a link (FR-51/108), so the null-token assertion below still proves the guard
                // blocked it. Seeded further out, that assertion would pass either way.
                CheckIn = today.AddDays(30),
                CheckOut = today.AddDays(32),
                OvernightGuestCount = 4,
                MealPlan = MealPlan.FullBoardAll,
                ArrivalDinner = true,
                DepartureBreakfast = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                DeleteAfter = today.AddYears(1),
            });
            await db.SaveChangesAsync();
        }

        try
        {
            await LoginAsync();
            await Page.GotoAsync($"/bokningar/{id}");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await Page.GetByRole(AriaRole.Button, new() { Name = "Godkänn", Exact = true }).ClickAsync();
            var dialog = Page.GetByRole(AriaRole.Dialog);
            await Assertions.Expect(dialog).ToBeVisibleAsync();
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Godkänn", Exact = true }).ClickAsync();

            await Assertions.Expect(Page.Locator(".form-status"))
                .ToContainTextAsync("Tilldela minst en byggnad");
            // A blocked approval is styled as an error and offers a dismiss control, rather than
            // looking and behaving like the green "har sparats" confirmation it used to (2026-08-10).
            await Assertions.Expect(Page.Locator(".form-status")).ToHaveClassAsync(ErrorToast);
            await Assertions.Expect(
                Page.Locator(".form-status").GetByRole(AriaRole.Button, new() { Name = "Stäng meddelandet" }))
                .ToBeVisibleAsync();

            await using var check = Fixture.NewDbContext();
            var booking = await check.Bookings.FindAsync(id);
            Assert.Equal(BookingStatus.Pending, booking!.Status);
            Assert.Null(booking.CompletionToken); // no magic link was issued
        }
        finally
        {
            await using var db = Fixture.NewDbContext();
            await db.Bookings.Where(b => b.Id == id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Approve_gates_behind_confirmation_and_only_fires_on_confirm()
    {
        var id = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await using (var db = Fixture.NewDbContext())
        {
            db.Bookings.Add(new Booking
            {
                Id = id,
                BookingReference = $"AG-E2E-{Guid.NewGuid():N}"[..20],
                BookingType = BookingType.StayRequest,
                Status = BookingStatus.Pending,
                ContactName = "Bekräfta Godkänn",
                ContactEmail = "godkann@example.se",
                CheckIn = today.AddDays(60),
                CheckOut = today.AddDays(62),
                OvernightGuestCount = 6,
                MealPlan = MealPlan.FullBoardAll,
                ArrivalDinner = true,
                DepartureBreakfast = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                DeleteAfter = today.AddYears(1),
            });
            await db.SaveChangesAsync();
        }

        try
        {
            await LoginAsync();
            await Page.GotoAsync($"/bokningar/{id}");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Approval requires a lodging assignment (FR-103/D1), so reach an approvable state the
            // way an admin does — through the assignment box — rather than seeding around the guard.
            // Ticking persists immediately; there is no save step.
            await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Huvudbyggnaden" }).CheckAsync();
            await Assertions.Expect(Page.Locator(".form-status"))
                .ToContainTextAsync("Byggnadstilldelningen är sparad.");

            var approve = Page.GetByRole(AriaRole.Button, new() { Name = "Godkänn", Exact = true });
            await approve.ClickAsync();

            // Dialog opens; cancelling must leave the booking Pending (Godkänn still offered).
            var dialog = Page.GetByRole(AriaRole.Dialog);
            await Assertions.Expect(dialog).ToBeVisibleAsync();

            // Check-in is 60 days out, so approval issues no link (FR-51/108) and the dialog must not
            // promise one. It still names the address, which is the point of stating it (FR-38).
            await Assertions.Expect(dialog).ToContainTextAsync("godkann@example.se");
            await Assertions.Expect(dialog).ToContainTextAsync("cirka sex veckor före ankomst");
            await Assertions.Expect(dialog).Not.ToContainTextAsync("med slutförandelänk skickas");

            await dialog.GetByRole(AriaRole.Button, new() { Name = "Avbryt" }).ClickAsync();
            await Assertions.Expect(dialog).Not.ToBeVisibleAsync();
            await Assertions.Expect(approve).ToBeVisibleAsync();

            // Honest cross-check: the transition genuinely did not happen on cancel.
            await using (var db = Fixture.NewDbContext())
            {
                var status = (await db.Bookings.FindAsync(id))!.Status;
                Assert.Equal(BookingStatus.Pending, status);
            }

            // Confirm this time — the booking is actually approved.
            await approve.ClickAsync();
            await Page.GetByRole(AriaRole.Dialog)
                .GetByRole(AriaRole.Button, new() { Name = "Godkänn", Exact = true }).ClickAsync();
            await Assertions.Expect(Page.Locator(".form-status")).ToContainTextAsync("godkänd");

            // …and the toast tells the truth about the link rather than printing a blank expiry.
            await Assertions.Expect(Page.Locator(".form-status"))
                .ToContainTextAsync("cirka sex veckor före ankomst");

            await using (var db = Fixture.NewDbContext())
            {
                var booking = (await db.Bookings.FindAsync(id))!;
                Assert.Equal(BookingStatus.Approved, booking.Status);
                Assert.Null(booking.CompletionToken);
                Assert.Null(booking.CompletionLinkSentAt);
            }

            // The action rail explains the missing link instead of showing an empty "giltig till".
            // Narrowed twice on purpose: the success toast carries the same sentence, and the
            // building-assignment box is a second .action-note — either alone still matches two
            // elements and fails on strict mode.
            await Assertions.Expect(Page.Locator(".action-note", new() { HasText = "Slutförandelänk" }))
                .ToContainTextAsync("Skickas automatiskt cirka sex veckor före ankomst");
            await Assertions.Expect(
                Page.GetByRole(AriaRole.Button, new() { Name = "Skicka slutförandelänk nu" }))
                .ToBeVisibleAsync();
        }
        finally
        {
            await using var db = Fixture.NewDbContext();
            await db.Bookings.Where(b => b.Id == id).ExecuteDeleteAsync();
        }
    }

    /// <summary>
    /// The other half of the approve dialog's conditional: a booking inside the 40-day window is
    /// promised a link, because approving it really does send one (FR-51/108). Cancels rather than
    /// approves — the transition itself is covered above.
    /// </summary>
    [Fact]
    public async Task Approve_dialog_promises_the_link_when_check_in_is_inside_the_issue_window()
    {
        var id = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await using (var db = Fixture.NewDbContext())
        {
            db.Bookings.Add(new Booking
            {
                Id = id,
                BookingReference = $"AG-E2E-{Guid.NewGuid():N}"[..20],
                BookingType = BookingType.StayRequest,
                Status = BookingStatus.Pending,
                ContactName = "Nära Incheckning",
                ContactEmail = "nara@example.se",
                CheckIn = today.AddDays(30),
                CheckOut = today.AddDays(32),
                OvernightGuestCount = 6,
                MealPlan = MealPlan.FullBoardAll,
                ArrivalDinner = true,
                DepartureBreakfast = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                DeleteAfter = today.AddYears(1),
            });
            await db.SaveChangesAsync();
        }

        try
        {
            await LoginAsync();
            await Page.GotoAsync($"/bokningar/{id}");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            await Page.GetByRole(AriaRole.Button, new() { Name = "Godkänn", Exact = true }).ClickAsync();

            var dialog = Page.GetByRole(AriaRole.Dialog);
            await Assertions.Expect(dialog).ToBeVisibleAsync();
            await Assertions.Expect(dialog).ToContainTextAsync("med slutförandelänk skickas till nara@example.se");
            await Assertions.Expect(dialog).Not.ToContainTextAsync("cirka sex veckor före ankomst");

            await dialog.GetByRole(AriaRole.Button, new() { Name = "Avbryt" }).ClickAsync();
        }
        finally
        {
            await using var db = Fixture.NewDbContext();
            await db.Bookings.Where(b => b.Id == id).ExecuteDeleteAsync();
        }
    }

    // The three login-layout pages are public (no auth). /aterstall-losenord renders its
    // invalid-link state when navigated without a ?code, which is what we axe-check here.
    [Theory]
    [InlineData("/login")]
    [InlineData("/glomt-losenord")]
    [InlineData("/aterstall-losenord")]
    public async Task Auth_pages_have_no_serious_accessibility_violations(string path)
    {
        await Page.GotoAsync(path);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await AssertNoSeriousAxeViolationsAsync(path);
    }

    private async Task AssertNoSeriousAxeViolationsAsync(string path)
    {
        var results = await Page.RunAxe();
        var serious = results.Violations.Where(v => v.Impact is "serious" or "critical").ToList();

        Assert.True(
            serious.Count == 0,
            $"axe found serious/critical a11y violations on {path}:\n" +
            string.Join("\n", serious.SelectMany(v => v.Nodes.Select(n => $"  [{v.Id}] {n.Html}"))));
    }

    // Slice 7 (updated Phase 8): responsive reachability sweep. Representative admin surfaces must keep
    // their topbar title and the primary nav reachable at every breakpoint. ≤860px the sidebar collapses
    // behind a hamburger, so on mobile/tablet the links are reached by opening the toggle first.
    [Theory]
    [InlineData("/", "Översikt")]
    [InlineData("/bokningar", "Bokningar")]
    [InlineData("/matalternativ", "Matalternativ")]
    [InlineData("/byggnader", "Byggnader")]
    public async Task Admin_pages_stay_reachable_across_breakpoints(string path, string title)
    {
        await LoginAsync();

        foreach (var viewport in new[] { Mobile, Tablet, Desktop })
        {
            await Page.SetViewportSizeAsync(viewport.Width, viewport.Height);
            await Page.GotoAsync(path);
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // The topbar title anchors the page…
            await Assertions.Expect(Page.Locator(".admin-topbar").GetByRole(AriaRole.Heading, new() { Name = title }))
                .ToBeVisibleAsync();

            // …and the primary nav stays reachable at every width — on the collapsed (≤860px) layout by
            // opening the hamburger, on desktop it's the always-on sidebar.
            if (await Page.Locator(".admin-nav__toggle").IsVisibleAsync())
                await OpenMobileNavAsync();
            await Assertions.Expect(Page.Locator(".admin-nav").GetByRole(AriaRole.Link, new() { Name = "Bokningar" }))
                .ToBeVisibleAsync();
        }
    }

    [Fact]
    public async Task Mobile_nav_collapses_behind_a_hamburger_toggle()
    {
        await LoginAsync();

        // Desktop: full vertical sidebar — links + group headings shown, no hamburger.
        await Page.SetViewportSizeAsync(Desktop.Width, Desktop.Height);
        await Page.GotoAsync("/");
        await Assertions.Expect(Page.Locator(".admin-nav__group").First).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".admin-nav__toggle")).Not.ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".admin-nav").GetByRole(AriaRole.Link, new() { Name = "Bokningar" }))
            .ToBeVisibleAsync();

        // Mobile: links hidden by default, hamburger shown.
        await Page.SetViewportSizeAsync(Mobile.Width, Mobile.Height);
        await Page.GotoAsync("/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var bokningar = Page.Locator(".admin-nav").GetByRole(AriaRole.Link, new() { Name = "Bokningar" });
        await Assertions.Expect(Page.Locator(".admin-nav__toggle")).ToBeVisibleAsync();
        await Assertions.Expect(bokningar).Not.ToBeVisibleAsync();

        // Tapping the hamburger reveals the links…
        await OpenMobileNavAsync();
        await Assertions.Expect(bokningar).ToBeVisibleAsync();

        // …and navigating via a link closes the menu again.
        await bokningar.ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/bokningar$"));
        await Assertions.Expect(Page.Locator(".admin-nav").GetByRole(AriaRole.Link, new() { Name = "Bokningar" }))
            .Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task Dashboard_cards_stack_and_avoid_horizontal_overflow_on_mobile()
    {
        await LoginAsync();
        await Page.SetViewportSizeAsync(Mobile.Width, Mobile.Height);
        await Page.GotoAsync("/");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The two top cards stack vertically on a phone rather than being squeezed side by side.
        var cards = Page.Locator(".dash-grid > .card");
        await Assertions.Expect(cards).ToHaveCountAsync(2);
        var first = await cards.Nth(0).BoundingBoxAsync();
        var second = await cards.Nth(1).BoundingBoxAsync();
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(second!.Y >= first!.Y + first.Height - 1,
            $"Dashboard cards not stacked on mobile: first(y={first.Y}, h={first.Height}) second(y={second.Y}).");

        // No page-level horizontal scroll at 360px (wide tables scroll inside their own box, not the page).
        var docScrollWidth = await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        Assert.True(docScrollWidth <= Mobile.Width + 1,
            $"Page overflows horizontally at {Mobile.Width}px: scrollWidth={docScrollWidth}.");
    }

    /// <summary>Admin panel mobile polish: below 640px every list page swaps its .admin-table for a
    /// .data-cards list instead of scrolling sideways. Checks the actual swap (table hidden, card
    /// visible with the same data) and that the card is still a working link to the detail page,
    /// not just that some mobile class exists.</summary>
    [Fact]
    public async Task Bookings_list_swaps_the_table_for_cards_on_mobile()
    {
        await LoginAsync();
        await Page.SetViewportSizeAsync(Mobile.Width, Mobile.Height);
        await Page.GotoAsync("/bokningar");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(Page.Locator(".admin-table-wrap")).Not.ToBeVisibleAsync();
        var cards = Page.Locator(".data-cards .data-card");
        await Assertions.Expect(cards.First).ToBeVisibleAsync();

        var pendlarCard = cards.Filter(new() { HasText = "Familjen Pendlar" }).First;
        await Assertions.Expect(pendlarCard.Locator(".badge")).ToBeVisibleAsync();

        // The card itself is the tap target — same navigation the desktop row link offers.
        await pendlarCard.ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/bokningar/[0-9a-fA-F-]+$"));

        var docScrollWidth = await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        Assert.True(docScrollWidth <= Mobile.Width + 1,
            $"Bookings list overflows horizontally at {Mobile.Width}px: scrollWidth={docScrollWidth}.");
    }

    /// <summary>Admin panel polish: a completed booking's participants no longer load as N fully
    /// expanded edit forms. Each participant starts as a compact read-only summary; only clicking
    /// "Redigera" reveals the actual input fields for that one person, and saving reaches the
    /// database (not just a UI-only toggle).</summary>
    [Fact]
    public async Task Participant_starts_read_only_and_toggles_into_edit_mode_on_its_own()
    {
        var bookingId = Guid.NewGuid();
        var keptParticipantId = Guid.NewGuid();
        var editedParticipantId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await using (var db = Fixture.NewDbContext())
        {
            db.Bookings.Add(new Booking
            {
                Id = bookingId,
                BookingReference = $"AG-E2E-{Guid.NewGuid():N}"[..20],
                BookingType = BookingType.StayRequest,
                Status = BookingStatus.DetailsComplete,
                ContactName = "Familjen Radhus",
                ContactEmail = "radhus@example.se",
                CheckIn = today.AddDays(60),
                CheckOut = today.AddDays(62),
                OvernightGuestCount = 2,
                MealPlan = MealPlan.FullBoardAll,
                ArrivalDinner = true,
                DepartureBreakfast = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                DeleteAfter = today.AddYears(1),
            });
            db.Participants.Add(new Participant
            {
                Id = keptParticipantId,
                BookingId = bookingId,
                FullName = "Karin Radhus",
                ParticipantType = ParticipantType.Overnight,
                AgeCategory = AgeCategory.Adult,
                Allergies = [Allergy.Nut],
            });
            db.Participants.Add(new Participant
            {
                Id = editedParticipantId,
                BookingId = bookingId,
                FullName = "Olle Radhus",
                ParticipantType = ParticipantType.Overnight,
                AgeCategory = AgeCategory.Adult,
            });
            await db.SaveChangesAsync();
        }

        try
        {
            await LoginAsync();
            await Page.GotoAsync($"/bokningar/{bookingId}");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var section = Page.Locator(".detail-acc", new() { HasText = "Deltagare" });
            await Assertions.Expect(section).ToContainTextAsync("2 st");

            // Selector keys off the stable participant ID (data-participant), not visible text — the
            // name only exists as text in read view and becomes an <input value> once editing, so a
            // HasText-based locator would stop matching its own fieldset the moment it toggles.
            var olleCard = Page.Locator($"fieldset.participant[data-participant='{editedParticipantId}']");
            var karinCard = Page.Locator($"fieldset.participant[data-participant='{keptParticipantId}']");

            // Read view first: the name is plain text and no input for it exists yet.
            await Assertions.Expect(olleCard.Locator(".participant-view__name")).ToHaveTextAsync("Olle Radhus");
            await Assertions.Expect(olleCard.Locator("input")).Not.ToBeVisibleAsync();

            // The other participant's allergy chip is visible from the summary alone, unedited.
            await Assertions.Expect(karinCard.Locator(".chip")).ToContainTextAsync("Nötallergi");

            // Opening edit on Olle reveals the real bound input, prefilled with the current name.
            await olleCard.Locator(".edit-pencil").ClickAsync();
            var nameInput = olleCard.Locator("input").First;
            await Assertions.Expect(nameInput).ToHaveValueAsync("Olle Radhus");

            // There is no save button on this page: a text field saves when you leave it, so Tab is
            // the real gesture a user makes. Every other control saves on change.
            await nameInput.FillAsync("Olle Radhusson");
            await nameInput.PressAsync("Tab");
            await Assertions.Expect(Page.Locator(".form-status")).ToContainTextAsync("Deltagaruppgifterna är sparade.");

            // Honest cross-check: the edit actually reached the database, not just the DOM.
            await using var db = Fixture.NewDbContext();
            var reloaded = await db.Participants.FindAsync(editedParticipantId);
            Assert.Equal("Olle Radhusson", reloaded!.FullName);

            // Saving does NOT close the editor — tabbing between fields must not eject you from the
            // row you are working on. "Klar" is what closes it, and the read view shows the new name.
            await Assertions.Expect(nameInput).ToBeVisibleAsync();
            await olleCard.GetByRole(AriaRole.Button, new() { Name = "Klar" }).ClickAsync();
            // Count rather than visibility: the edit view holds six inputs, and asserting "not
            // visible" on a multi-match locator trips Playwright's strict mode instead of waiting.
            await Assertions.Expect(olleCard.Locator("input")).ToHaveCountAsync(0);
            await Assertions.Expect(olleCard.Locator(".participant-view__name")).ToHaveTextAsync("Olle Radhusson");
        }
        finally
        {
            await using var db = Fixture.NewDbContext();
            await db.Participants.Where(p => p.BookingId == bookingId).ExecuteDeleteAsync();
            await db.Bookings.Where(b => b.Id == bookingId).ExecuteDeleteAsync();
        }
    }
}
