using AkeroGarden.E2E.Infrastructure;
using AkeroGarden.Web.Data.Entities;
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
public class AdminTests(E2EFixture fixture) : E2ETestBase(fixture)
{
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

    /// <summary>Logs in through the real /login form and lands on /admin.</summary>
    private async Task LoginAsync()
    {
        await Page.GotoAsync("/login");
        await Page.FillAsync("#email", E2EFixture.AdminEmail);
        await Page.FillAsync("#password", E2EFixture.AdminPassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Logga in" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/admin$"));
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
        await Page.GotoAsync("/admin/bokningar/ny");
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
            new System.Text.RegularExpressions.Regex(@"/admin/bokningar/[0-9a-fA-F-]{36}$"));
    }

    [Fact]
    public async Task Manual_booking_invalid_phone_shows_a_styled_message_and_blocks_submit()
    {
        // Finding (Phase 7 smoke test): the manual-booking phone field was missing a
        // <ValidationMessage>, so an invalid phone silently blocked OnValidSubmit with no feedback —
        // unlike the public form. With the message wired, [OptionalPhone] surfaces the same Swedish
        // error and the form does NOT navigate to a new booking's detail page.
        await LoginAsync();
        await Page.GotoAsync("/admin/bokningar/ny");
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
        await Assertions.Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/admin/bokningar/ny$"));
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
            await Page.GotoAsync("/admin/bokningar");
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
            .ToHaveAttributeAsync("href", "/admin/bokningar");
        await Page.GotoAsync("/admin/bokningar");

        await Assertions.Expect(Page.Locator("table.admin-table")).ToBeVisibleAsync();
        // The Pending booking carries the coloured status badge.
        await Assertions.Expect(Page.Locator(".badge.badge--pending").First).ToBeVisibleAsync();

        await CaptureAsync("admin-bookings-list");
    }

    [Fact]
    public async Task Booking_detail_has_action_rail()
    {
        await LoginAsync();
        await Page.GotoAsync("/admin/bokningar");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Familjen Pendlar" }).First.ClickAsync();

        // Two-column detail with the sticky action rail; a Pending stay offers Approve.
        await Assertions.Expect(Page.Locator(".detail-grid .detail-rail")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Åtgärder" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Godkänn" })).ToBeVisibleAsync();

        await CaptureAsync("admin-booking-detail");
    }

    [Fact]
    public async Task Save_feedback_shows_as_a_toast_within_the_viewport_when_saving_far_down()
    {
        // Client feedback (Phase 8): saving payment status near the bottom of the detail page used to
        // render the confirmation at the very top, so the admin had to scroll up to see it worked.
        // It is now a fixed toast — assert it both appears and sits within the viewport after a save
        // performed at the bottom of the page.
        await LoginAsync();
        await Page.GotoAsync("/admin/bokningar");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Familjen Pendlar" }).First.ClickAsync();
        // Interactive Server: let the circuit connect before toggling @bind checkboxes / clicking.
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var saveBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Spara betalningsstatus" });
        await saveBtn.ScrollIntoViewIfNeededAsync();
        await Page.GetByLabel("Handpenning fakturerad").CheckAsync();
        await saveBtn.ClickAsync();

        var toast = Page.Locator(".form-status");
        await Assertions.Expect(toast).ToBeVisibleAsync();
        await Assertions.Expect(toast).ToContainTextAsync("Betalningsstatus är sparad.");

        // Honest check of the actual claim: the toast is within the viewport without scrolling back up.
        var box = await toast.BoundingBoxAsync();
        var viewport = Page.ViewportSize!;
        Assert.NotNull(box);
        Assert.True(box!.Y >= 0 && box.Y + box.Height <= viewport.Height + 1,
            $"Toast should sit within the viewport (y={box.Y}, h={box.Height}, viewportHeight={viewport.Height}).");
    }

    // Slice 7: axe now sweeps every authenticated admin page — the operational dashboard, the
    // bookings list + manual-booking form, the calendar and blocks, and all six reference-data
    // CRUD screens plus accounts. (Booking detail needs a real {id} and is covered separately.)
    [Theory]
    [InlineData("/admin")]
    [InlineData("/admin/bokningar")]
    [InlineData("/admin/bokningar/ny")]
    [InlineData("/admin/kalender")]
    [InlineData("/admin/blockeringar")]
    [InlineData("/admin/byggnader")]
    [InlineData("/admin/matalternativ")]
    [InlineData("/admin/priser")]
    [InlineData("/admin/stadszoner")]
    [InlineData("/admin/faciliteter")]
    [InlineData("/admin/konton")]
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
        await Page.GotoAsync("/admin/bokningar");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Familjen Pendlar" }).First.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await AssertNoSeriousAxeViolationsAsync("booking detail");
    }

    /// <summary>Admin invite lifecycle (Phase 9, FR-74): a fresh invite shows the pending badge
    /// with resend/remove actions; resend keeps it pending; remove deletes the row. No SMTP
    /// server in E2E (see E2EFixture), so this only asserts the UI state, not email delivery.</summary>
    [Fact]
    public async Task Admin_can_resend_and_remove_a_pending_invite()
    {
        var email = $"{Guid.NewGuid()}@akerogarden.se";

        await LoginAsync();
        await Page.GotoAsync("/admin/konton");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Page.FillAsync("#invite-email", email);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka inbjudan" }).ClickAsync();

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

    /// <summary>Phase 11: irreversible "Ta bort" now opens a confirmation dialog. Seed an
    /// unreferenced facility, prove Avbryt keeps the row, and that confirming actually removes it
    /// (reaches the "har tagits bort" toast and the row disappears) — the real outcome, not merely
    /// that a dialog renders.</summary>
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
            await Page.GotoAsync("/admin/faciliteter");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var row = Page.Locator("tr", new() { HasText = name });
            await Assertions.Expect(row).ToBeVisibleAsync();

            // Open the dialog, then cancel — the row must survive (nothing deleted).
            await row.GetByRole(AriaRole.Button, new() { Name = "Ta bort" }).ClickAsync();
            var dialog = Page.GetByRole(AriaRole.Dialog);
            await Assertions.Expect(dialog).ToBeVisibleAsync();
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Avbryt" }).ClickAsync();
            await Assertions.Expect(dialog).Not.ToBeVisibleAsync();
            await Assertions.Expect(row).ToBeVisibleAsync();

            // Open again and confirm — the facility is actually deleted.
            await row.GetByRole(AriaRole.Button, new() { Name = "Ta bort" }).ClickAsync();
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

    /// <summary>Phase 11: guest-facing booking actions gate behind a confirmation dialog. On a
    /// throwaway Pending stay, "Godkänn" opens the dialog; Avbryt leaves it Pending (the approval
    /// email/one-way transition does NOT fire), and confirming actually approves it (reaches the
    /// "godkänd" toast). Seeded/cleaned up here so the shared Pending booking is untouched.</summary>
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
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                DeleteAfter = today.AddYears(1),
            });
            await db.SaveChangesAsync();
        }

        try
        {
            await LoginAsync();
            await Page.GotoAsync($"/admin/bokningar/{id}");
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var approve = Page.GetByRole(AriaRole.Button, new() { Name = "Godkänn", Exact = true });
            await approve.ClickAsync();

            // Dialog opens; cancelling must leave the booking Pending (Godkänn still offered).
            var dialog = Page.GetByRole(AriaRole.Dialog);
            await Assertions.Expect(dialog).ToBeVisibleAsync();
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
    [InlineData("/admin", "Översikt")]
    [InlineData("/admin/bokningar", "Bokningar")]
    [InlineData("/admin/matalternativ", "Matalternativ")]
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
        await Page.GotoAsync("/admin");
        await Assertions.Expect(Page.Locator(".admin-nav__group").First).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".admin-nav__toggle")).Not.ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".admin-nav").GetByRole(AriaRole.Link, new() { Name = "Bokningar" }))
            .ToBeVisibleAsync();

        // Mobile: links hidden by default, hamburger shown.
        await Page.SetViewportSizeAsync(Mobile.Width, Mobile.Height);
        await Page.GotoAsync("/admin");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var bokningar = Page.Locator(".admin-nav").GetByRole(AriaRole.Link, new() { Name = "Bokningar" });
        await Assertions.Expect(Page.Locator(".admin-nav__toggle")).ToBeVisibleAsync();
        await Assertions.Expect(bokningar).Not.ToBeVisibleAsync();

        // Tapping the hamburger reveals the links…
        await OpenMobileNavAsync();
        await Assertions.Expect(bokningar).ToBeVisibleAsync();

        // …and navigating via a link closes the menu again.
        await bokningar.ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/admin/bokningar$"));
        await Assertions.Expect(Page.Locator(".admin-nav").GetByRole(AriaRole.Link, new() { Name = "Bokningar" }))
            .Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task Dashboard_cards_stack_and_avoid_horizontal_overflow_on_mobile()
    {
        await LoginAsync();
        await Page.SetViewportSizeAsync(Mobile.Width, Mobile.Height);
        await Page.GotoAsync("/admin");
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
}
