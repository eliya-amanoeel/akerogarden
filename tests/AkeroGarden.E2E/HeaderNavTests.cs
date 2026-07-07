using System.Text.RegularExpressions;
using AkeroGarden.E2E.Infrastructure;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>Public shell — the top nav. Current-page state + the primary CTA.</summary>
public class HeaderNavTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Current_page_is_marked_in_the_nav()
    {
        await Page.GotoAsync("/faciliteter");

        var nav = Page.Locator(".site-header__nav");
        var current = nav.GetByRole(AriaRole.Link, new() { Name = "Faciliteter" });

        // Screen-reader + visual current-page indicator.
        await Assertions.Expect(current).ToHaveAttributeAsync("aria-current", "page");
        await Assertions.Expect(current).ToHaveClassAsync(new Regex(@"\bactive\b"));

        var captureDir = Environment.GetEnvironmentVariable("E2E_CAPTURE_DIR");
        if (captureDir is not null)
            await Page.Locator(".site-header").ScreenshotAsync(
                new() { Path = Path.Combine(captureDir, "header-faciliteter.png") });
    }

    [Fact]
    public async Task Cta_leads_to_the_inquiry_form()
    {
        await Page.GotoAsync("/");

        await Page.Locator(".site-header__cta").ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/boka$"));
    }
}
