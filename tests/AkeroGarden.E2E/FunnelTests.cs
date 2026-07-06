using System.Text.RegularExpressions;
using AkeroGarden.E2E.Infrastructure;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// The booking funnel — the primary KPI. Covers the full journey home → kalender → boka → tack,
/// the two-track toggle, date pre-fill from the calendar, and accessibility of the funnel pages.
/// </summary>
public class FunnelTests(E2EFixture fixture) : E2ETestBase(fixture)
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
        // Phone is optional, but [Phone] currently rejects an empty string (a known Phase-7
        // "phone validation" backlog item), so a realistic user fills it.
        await Page.FillAsync("#s-phone", "0701234567");
        await Page.FillAsync("#s-guests", "24");
        await Page.SelectOptionAsync("#mealplan", "FullBoardAll");
        await Page.Locator(".track--stay label:has-text('integritetspolicyn') input[type=checkbox]").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka bokningsförfrågan" }).ClickAsync();

        // Acknowledgement
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).ToBeVisibleAsync();
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
