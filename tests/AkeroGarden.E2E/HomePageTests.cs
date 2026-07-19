using System.Text.RegularExpressions;
using AkeroGarden.E2E.Infrastructure;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// Home page (`/`) — direction A. Asserts branding, the primary funnel step (CTA → inquiry),
/// accessibility (axe, no serious/critical violations), and responsive reachability across
/// mobile/tablet/desktop. No pixel diffing — behaviour and structure only.
/// </summary>
public class HomePageTests(E2EFixture fixture) : PublicE2ETestBase(fixture)
{
    [Fact]
    public async Task Loads_and_is_branded_Swedish()
    {
        await Page.GotoAsync("/");

        Assert.Contains("Åkerögården", await Page.TitleAsync());
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Landa tillsammans, på landet" }))
            .ToBeVisibleAsync();
    }

    [Fact]
    public async Task Hero_primary_cta_starts_the_inquiry_funnel()
    {
        await Page.GotoAsync("/");

        // The hero's primary action is the top of the booking funnel (the primary KPI).
        await Page.Locator(".hero__cta a.btn").ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/boka$"));
    }

    [Fact]
    public async Task Hero_secondary_cta_reaches_availability()
    {
        await Page.GotoAsync("/");

        await Page.Locator(".hero__cta a.btn-ghost").ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/kalender$"));
    }

    [Fact]
    public async Task Usecards_are_image_cards_not_flat_text_blocks()
    {
        await Page.GotoAsync("/");

        // Was three identical flat text-only cards (a green rule + heading); each now carries
        // its own photo (design.md's image-card pattern, same shape as Facilities' .fac-card).
        var cards = Page.Locator(".usecard");
        await Assertions.Expect(cards).ToHaveCountAsync(3);
        await Assertions.Expect(cards.Locator(".usecard__img").First).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Läger & retreat" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Has_no_serious_accessibility_violations()
    {
        await Page.GotoAsync("/");

        var results = await Page.RunAxe();
        var serious = results.Violations
            .Where(v => v.Impact is "serious" or "critical")
            .ToList();

        Assert.True(
            serious.Count == 0,
            "axe found serious/critical a11y violations:\n" +
            string.Join("\n", serious.SelectMany(v => v.Nodes.Select(n => $"  [{v.Id}] {n.Html}"))));
    }

    [Fact]
    public async Task Is_reachable_across_breakpoints()
    {
        var captureDir = Environment.GetEnvironmentVariable("E2E_CAPTURE_DIR");

        foreach (var (name, vp) in new[] { ("mobile", Mobile), ("tablet", Tablet), ("desktop", Desktop) })
        {
            await Page.SetViewportSizeAsync(vp.Width, vp.Height);
            await Page.GotoAsync("/");

            // The primary CTA must be reachable at every size.
            await Assertions.Expect(Page.Locator(".hero__cta a.btn")).ToBeVisibleAsync();

            if (name == "mobile")
                // Mobile collapses the nav behind the hamburger.
                await Assertions.Expect(Page.Locator(".site-header__burger")).ToBeVisibleAsync();
            else
                // Desktop shows the inline nav.
                await Assertions.Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Faciliteter" }).First)
                    .ToBeVisibleAsync();

            if (captureDir is not null)
                await Page.ScreenshotAsync(new() { Path = Path.Combine(captureDir, $"home-{name}.png"), FullPage = true });
        }
    }
}
