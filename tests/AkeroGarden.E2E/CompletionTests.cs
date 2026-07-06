using AkeroGarden.E2E.Infrastructure;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// The token-gated guest completion form (/slutfor/{token}). Uses the Approved booking + valid
/// token seeded by the fixture. Verifies the redesigned form renders (participant cards, meals)
/// and is accessible; the submit/validation path is covered by the integration suite.
/// </summary>
public class CompletionTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    private string CompletionUrl => $"/slutfor/{E2EFixture.CompletionToken}";

    [Fact]
    public async Task Valid_token_renders_the_participant_cards()
    {
        await Page.GotoAsync(CompletionUrl);

        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Nästan klart!" })).ToBeVisibleAsync();
        // One card per overnight guest (the fixture booking has 2).
        await Assertions.Expect(Page.Locator(".pcard[aria-label='Deltagare 1']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".pcard[aria-label='Deltagare 2']")).ToBeVisibleAsync();

        var captureDir = Environment.GetEnvironmentVariable("E2E_CAPTURE_DIR");
        if (captureDir is not null)
            await Page.ScreenshotAsync(new() { Path = Path.Combine(captureDir, "completion.png"), FullPage = true });
    }

    [Fact]
    public async Task Invalid_token_shows_a_friendly_message()
    {
        await Page.GotoAsync("/slutfor/not-a-real-token");

        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Länken är inte giltig" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Completion_form_has_no_serious_accessibility_violations()
    {
        await Page.GotoAsync(CompletionUrl);

        var results = await Page.RunAxe();
        var serious = results.Violations.Where(v => v.Impact is "serious" or "critical").ToList();

        Assert.True(
            serious.Count == 0,
            "axe found serious/critical a11y violations on the completion form:\n" +
            string.Join("\n", serious.SelectMany(v => v.Nodes.Select(n => $"  [{v.Id}] {n.Html}"))));
    }
}
