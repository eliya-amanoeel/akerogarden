using System.Text.RegularExpressions;
using AkeroGarden.E2E.Infrastructure;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// The booking funnel — the primary KPI. Covers the full journey home → kalender → boka → tack,
/// the two-track toggle, date pre-fill from the calendar, and accessibility of the funnel pages.
/// </summary>
public class FunnelTests(E2EFixture fixture) : PublicE2ETestBase(fixture)
{
    [Fact]
    public async Task Full_funnel_home_to_calendar_to_inquiry_to_thanks()
    {
        // Discover → availability
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Se tillgänglighet" }).First.ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/kalender$"));

        var captureDir = Environment.GetEnvironmentVariable("E2E_CAPTURE_DIR");
        if (captureDir is not null)
            await Page.ScreenshotAsync(new() { Path = Path.Combine(captureDir, "kalender.png"), FullPage = true });

        // Pick a date range on the calendar (empty DB => every future day is free/selectable).
        var freeDays = Page.Locator(".cal-day.cal-free a");
        await freeDays.Nth(0).ClickAsync();
        await freeDays.Nth(4).ClickAsync();
        await Assertions.Expect(Page.Locator("#cal-selection")).ToBeVisibleAsync();
        await Page.Locator("#cal-go").ClickAsync();

        // Inquiry form — dates arrive pre-filled from the calendar
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/boka\?"));
        await Assertions.Expect(Page.Locator(".prefill").First).ToBeVisibleAsync();
        Assert.False(string.IsNullOrEmpty(await Page.Locator("#s-checkin").InputValueAsync()));

        await Page.FillAsync("#s-name", "Anna Andersson");
        await Page.FillAsync("#s-email", "anna@example.se");
        await Page.FillAsync("#s-email-confirm", "anna@example.se");
        // Leave the optional phone blank on purpose — that is the real, common path, and it must
        // submit successfully (regression guard for the OptionalPhone fix).
        await Page.FillAsync("#s-guests", "24");
        await Page.SelectOptionAsync("#mealplan", "FullBoardAll");
        await Page.Locator(".track--stay label:has-text('integritetspolicyn') input[type=checkbox]").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka bokningsförfrågan" }).ClickAsync();

        // Acknowledgement
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).ToBeVisibleAsync();
    }

    /// <summary>Fills every required stay-request field with valid values, so a test can invalidate a
    /// single field and assert its message in isolation. Leaves the optional phone blank.</summary>
    private static async Task FillValidStayFormAsync(IPage page)
    {
        await page.FillAsync("#s-name", "Anna Andersson");
        await page.FillAsync("#s-email", "anna@example.se");
        await page.FillAsync("#s-email-confirm", "anna@example.se");
        await page.FillAsync("#s-checkin", "2026-08-01");
        await page.FillAsync("#s-checkout", "2026-08-03");
        await page.FillAsync("#s-guests", "12");
        await page.SelectOptionAsync("#mealplan", "FullBoardAll");
        await page.Locator(".track--stay label:has-text('integritetspolicyn') input[type=checkbox]").CheckAsync();
    }

    [Fact]
    public async Task Client_side_validation_blocks_the_postback_on_invalid_input()
    {
        // A2d core claim: an invalid submit is caught in the browser and never posts — no wasted
        // round-trip that re-renders just to show an error and scrolls the guest away from it.
        // We assert the inline message appears AND that no POST to /boka was made.
        await Page.GotoAsync("/boka");
        await FillValidStayFormAsync(Page);
        await Page.FillAsync("#s-email-confirm", "typo@example.se");   // mismatch — client-detectable

        var postsToBoka = new List<string>();
        void OnRequest(object? _, IRequest r)
        {
            if (r.Method == "POST" && r.Url.Contains("/boka")) postsToBoka.Add(r.Url);
        }
        Page.Request += OnRequest;
        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka bokningsförfrågan" }).ClickAsync();

        await Assertions.Expect(Page.GetByText("E-postadresserna stämmer inte överens.")).ToBeVisibleAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/boka$"));
        Page.Request -= OnRequest;

        Assert.Empty(postsToBoka); // blocked client-side — nothing posted
    }

    [Fact]
    public async Task With_javascript_disabled_the_server_still_blocks_an_invalid_email()
    {
        // Graceful degradation (A2d): with the client-side script inert, the invalid email must
        // still be caught — the post goes through and our styled server [EmailAddress] message
        // renders. The server is always authoritative (belt-and-suspenders).
        await using var context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Fixture.PublicBaseUrl,
            JavaScriptEnabled = false,
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync("/boka");
        // Submitting a malformed email must be caught by the server even with no client script — the
        // post goes through and the styled [EmailAddress] message renders. (Only the email is filled;
        // the point is that the server, not the browser, rejects it. The CSS-only track toggle keeps
        // the stay form reachable without JS.) Submit with Enter — a native form submission a JS-off
        // user can do without scrolling to the button.
        await page.FillAsync("#s-email", "anna-at-example.se");   // no @ → invalid
        await page.Locator("#s-email").PressAsync("Enter");

        await Assertions.Expect(page.GetByText("Ange en giltig e-postadress.")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task Organisation_without_a_name_is_blocked()
    {
        // OrgName is Conditional (§8): ticking "for an organisation" makes naming it required.
        await Page.GotoAsync("/boka");
        await FillValidStayFormAsync(Page);
        await Page.Locator("#s-isorg").CheckAsync();            // reveals the org panel (progressive JS)
        // Leave #s-orgname empty on purpose.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka bokningsförfrågan" }).ClickAsync();

        await Assertions.Expect(
            Page.GetByText("Organisationsnamn krävs när bokningen görs för en organisation."))
            .ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task Inquiry_toggle_switches_between_the_two_tracks()
    {
        await Page.GotoAsync("/boka");

        // Stay request is the default track.
        await Assertions.Expect(Page.Locator(".track--stay")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".track--general")).Not.ToBeVisibleAsync();

        // Toggle to the general-inquiry track.
        await Page.Locator(".segmented__btn[for='track-general']").ClickAsync();
        await Assertions.Expect(Page.Locator(".track--general")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".track--stay")).Not.ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("/kalender")]
    [InlineData("/boka")]
    public async Task Funnel_page_has_no_serious_accessibility_violations(string path)
    {
        await Page.GotoAsync(path);

        var results = await Page.RunAxe();
        var serious = results.Violations.Where(v => v.Impact is "serious" or "critical").ToList();

        Assert.True(
            serious.Count == 0,
            $"axe found serious/critical a11y violations on {path}:\n" +
            string.Join("\n", serious.SelectMany(v => v.Nodes.Select(n => $"  [{v.Id}] {n.Html}"))));
    }
}
