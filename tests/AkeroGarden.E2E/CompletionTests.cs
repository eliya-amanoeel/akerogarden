using AkeroGarden.E2E.Infrastructure;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// The token-gated guest completion form (/slutfor/{token}), against Approved bookings + valid
/// tokens seeded by the fixture. Drives the real behaviours: collapse, the group-default cascade,
/// the single-option fixed meal, and a full submit to the acknowledgement.
/// </summary>
public class CompletionTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    private static string Url(string token) => $"/slutfor/{token}";
    private string CompletionUrl => Url(E2EFixture.CompletionToken);

    [Fact]
    public async Task Valid_token_renders_the_group_default_and_participant_rows()
    {
        await Page.GotoAsync(CompletionUrl);

        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Nästan klart!" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".group-default")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".prow[aria-label='Deltagare 1']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".prow[aria-label='Deltagare 2']")).ToBeVisibleAsync();

        var captureDir = Environment.GetEnvironmentVariable("E2E_CAPTURE_DIR");
        if (captureDir is not null)
            await Page.ScreenshotAsync(new() { Path = Path.Combine(captureDir, "completion.png"), FullPage = true });
    }

    [Fact]
    public async Task Participant_row_collapses_meals_until_expanded()
    {
        await Page.GotoAsync(CompletionUrl);
        var row = Page.Locator(".prow[aria-label='Deltagare 1']");

        await Assertions.Expect(row.Locator(".prow__body")).Not.ToBeVisibleAsync();
        await row.Locator(".prow__toggle").ClickAsync();
        await Assertions.Expect(row.Locator(".prow__body")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Single_option_meal_renders_as_fixed_text_not_a_dropdown()
    {
        await Page.GotoAsync(CompletionUrl);
        var row = Page.Locator(".prow[aria-label='Deltagare 1']");
        await row.Locator(".prow__toggle").ClickAsync();

        // Lunch has 2 options → a select; dinner has 1 → fixed text (both dinner slots) + a hidden field.
        await Assertions.Expect(row.Locator("select[data-meal='Lunch']")).ToHaveCountAsync(1);
        await Assertions.Expect(row.Locator("select[data-meal='Dinner']")).ToHaveCountAsync(0);
        await Assertions.Expect(row.Locator(".meal-fixed")).ToHaveCountAsync(2);
    }

    [Fact]
    public async Task Group_default_lunch_cascades_to_every_participant()
    {
        await Page.GotoAsync(CompletionUrl);

        await Page.Locator("#gd-lunch").SelectOptionAsync(new SelectOptionValue { Label = "Ugnsbakad lax" });
        var chosen = await Page.Locator("#gd-lunch").InputValueAsync();

        // A participant whose own select was never touched now carries the group choice.
        await Page.Locator(".prow[aria-label='Deltagare 2'] .prow__toggle").ClickAsync();
        await Assertions.Expect(Page.Locator(".prow[aria-label='Deltagare 2'] select[data-meal='Lunch']"))
            .ToHaveValueAsync(chosen);
    }

    [Fact]
    public async Task Completion_submits_successfully_to_the_acknowledgement()
    {
        await Page.GotoAsync(Url(E2EFixture.CompletionSubmitToken));

        await Page.Locator(".prow[aria-label='Deltagare 1'] .prow__name").FillAsync("Anna Andersson");
        await Page.Locator(".prow[aria-label='Deltagare 2'] .prow__name").FillAsync("Bengt Berg");
        // Standard lunch cascades to every participant's lunch select; dinner is single-option and
        // auto-filled — so every full-board meal slot is satisfied.
        await Page.Locator("#gd-lunch").SelectOptionAsync(new SelectOptionValue { Label = "Ugnsbakad lax" });

        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka in uppgifter" }).ClickAsync();

        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Empty_participant_name_is_blocked_with_a_styled_message()
    {
        // Deltagare 1's name is left empty. Without a native `required` attribute the post goes
        // through, so the server-side normalizer's styled error banner surfaces our message
        // ("Deltagare 1: namn krävs.") rather than a native browser bubble. A failed submit does
        // not consume the token, so this can safely share the read-only CompletionToken.
        await Page.GotoAsync(CompletionUrl);
        await Page.Locator(".prow[aria-label='Deltagare 2'] .prow__name").FillAsync("Bengt Berg");
        await Page.Locator("#gd-lunch").SelectOptionAsync(new SelectOptionValue { Label = "Ugnsbakad lax" });

        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka in uppgifter" }).ClickAsync();

        await Assertions.Expect(Page.Locator(".alert--error")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".alert--error").GetByText("Deltagare 1: namn krävs.")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).Not.ToBeVisibleAsync();
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
