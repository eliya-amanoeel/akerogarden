using AkeroGarden.E2E.Infrastructure;
using Deque.AxeCore.Playwright;
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

    // Slice 7: responsive reachability sweep. Representative admin surfaces (dashboard, list, a CRUD
    // screen) must keep their topbar title and the sidebar nav reachable at every breakpoint — the
    // sidebar reflows to a top bar on mobile but its links never disappear.
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
            // …and the primary nav stays reachable at every width.
            await Assertions.Expect(Page.Locator(".admin-nav").GetByRole(AriaRole.Link, new() { Name = "Bokningar" }))
                .ToBeVisibleAsync();
        }
    }

    [Fact]
    public async Task Sidebar_collapses_on_mobile()
    {
        await LoginAsync();

        // Desktop: full vertical sidebar with the group headings.
        await Page.SetViewportSizeAsync(Desktop.Width, Desktop.Height);
        await Page.GotoAsync("/admin");
        await Assertions.Expect(Page.Locator(".admin-nav__group").First).ToBeVisibleAsync();

        // Mobile: the sidebar reflows to a top bar (group headings hidden) but nav stays reachable.
        await Page.SetViewportSizeAsync(Mobile.Width, Mobile.Height);
        await Page.GotoAsync("/admin");
        await Assertions.Expect(Page.Locator(".admin-nav__group").First).Not.ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".admin-nav").GetByRole(AriaRole.Link, new() { Name = "Bokningar" })).ToBeVisibleAsync();
    }
}
