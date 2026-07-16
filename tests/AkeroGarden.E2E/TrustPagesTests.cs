using System.Text.RegularExpressions;
using AkeroGarden.E2E.Infrastructure;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// Slice-3 trust pages. The E2E database is a fresh container (no seeded reference data), so
/// these assert structure + accessibility, not seeded content.
/// </summary>
public class TrustPagesTests(E2EFixture fixture) : PublicE2ETestBase(fixture)
{
    [Theory]
    [InlineData("/om-oss")]
    [InlineData("/faciliteter")]
    [InlineData("/mat-och-priser")]
    [InlineData("/kontakt")]
    [InlineData("/integritetspolicy")]
    [InlineData("/not-found")]
    public async Task Page_has_no_serious_accessibility_violations(string path)
    {
        await Page.GotoAsync(path);

        var results = await Page.RunAxe();
        var serious = results.Violations.Where(v => v.Impact is "serious" or "critical").ToList();

        Assert.True(
            serious.Count == 0,
            $"{path}: " + string.Join("\n", serious.SelectMany(v => v.Nodes.Select(n => $"  [{v.Id}] {n.Html}"))));

        var captureDir = Environment.GetEnvironmentVariable("E2E_CAPTURE_DIR");
        if (captureDir is not null)
            await Page.ScreenshotAsync(new()
            {
                Path = Path.Combine(captureDir, $"trust-{path.Trim('/').Replace('/', '-')}.png"),
                FullPage = true,
            });
    }

    [Fact]
    public async Task Mat_och_priser_merges_menu_and_prices_on_one_page()
    {
        await Page.GotoAsync("/mat-och-priser");

        // Menu section (eyebrow) + both price tables' headings all live on the one page.
        await Assertions.Expect(Page.GetByText("Menyn", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Prislista" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Städning" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Old_menu_and_prices_routes_are_gone()
    {
        await Page.GotoAsync("/meny");
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sidan hittades inte" })).ToBeVisibleAsync();

        await Page.GotoAsync("/priser");
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sidan hittades inte" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Contact_cta_leads_to_the_inquiry_form()
    {
        await Page.GotoAsync("/kontakt");

        await Page.Locator(".cta-card a.btn").ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/boka$"));
    }
}
