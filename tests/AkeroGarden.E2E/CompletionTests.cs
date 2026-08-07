using AkeroGarden.E2E.Infrastructure;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// The token-gated guest completion form (/slutfor/{token}), against Approved bookings + valid
/// tokens seeded by the fixture. Drives the real behaviours: collapse, the group-default cascade,
/// the single-option fixed meal, and a full submit to the acknowledgement.
/// </summary>
public class CompletionTests(E2EFixture fixture) : PublicE2ETestBase(fixture)
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
    public async Task Participant_row_collapses_allergies_until_expanded()
    {
        await Page.GotoAsync(CompletionUrl);
        var row = Page.Locator(".prow[aria-label='Deltagare 1']");

        await Assertions.Expect(row.Locator(".prow__body")).Not.ToBeVisibleAsync();
        await row.Locator(".prow__toggle").ClickAsync();
        await Assertions.Expect(row.Locator(".prow__body")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Meals_are_chosen_once_for_the_group_not_per_participant()
    {
        // FR-105: the meal grid lives outside the participant rows, and a participant row carries
        // no meal control at all.
        await Page.GotoAsync(CompletionUrl);

        await Assertions.Expect(Page.Locator("[data-group-meals]")).ToBeVisibleAsync();

        var row = Page.Locator(".prow[aria-label='Deltagare 1']");
        await row.Locator(".prow__toggle").ClickAsync();

        // No meal control of any kind in the row. Scoped to the meal markup rather than to "select"
        // outright — the age-category dropdown is a select and legitimately belongs here.
        await Assertions.Expect(row.Locator("[data-sitting]")).ToHaveCountAsync(0);
        await Assertions.Expect(row.Locator("select[data-meal-select]")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Ticking_an_edge_day_meal_reveals_that_sittings_dropdown()
    {
        // The arrival-day lunch is not taken by default, so its dropdown starts hidden; ticking the
        // arrival-lunch box reveals it (§4.1, FR-105).
        await Page.GotoAsync(CompletionUrl);

        var arrivalLunch = Page.Locator("[data-sitting][data-edge='arrival'][data-meal='Lunch']");
        await Assertions.Expect(arrivalLunch).Not.ToBeVisibleAsync();

        await Page.Locator("input[name='Form.ArrivalLunch'][data-edge-meal]").CheckAsync();
        await Assertions.Expect(arrivalLunch).ToBeVisibleAsync();

        await Page.Locator("input[name='Form.ArrivalLunch'][data-edge-meal]").UncheckAsync();
        await Assertions.Expect(arrivalLunch).Not.ToBeVisibleAsync();
    }

    // REMOVED 2026-08-07: Same_dish_all_days_copies_the_chosen_lunch_across_the_stay. The
    // "Samma rätt alla dagar" button it covered is gone — see §4.1. The shortcut existed to cascade
    // one choice across every *participant*, which group-level meal selection (FR-105) now does by
    // construction; read literally as "one dish for every day" it made the booking worse rather than
    // faster. Nothing weaker remains to assert: the behaviour no longer exists.

    [Fact]
    public async Task Self_catering_booking_has_no_meal_section_at_all()
    {
        // FR-55: a SelfCateringAll booking has no meal selection section — not a hidden one.
        await Page.GotoAsync(Url(E2EFixture.SelfCateringCompletionToken));

        await Assertions.Expect(Page.Locator("[data-group-meals]")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Organisation_invoice_fields_are_hidden_until_the_checkbox_is_ticked()
    {
        // Regression for the smoke-test bug: the org invoice fields rendered regardless of the
        // "Fakturera en organisation" checkbox, and ticking it did nothing. They must start hidden
        // and toggle with the checkbox (completion-form.js), mirroring the inquiry form.
        await Page.GotoAsync(CompletionUrl);

        var orgFields = Page.Locator("#org-invoice-fields");
        await Assertions.Expect(orgFields).Not.ToBeVisibleAsync();

        await Page.Locator("#is-org").CheckAsync();
        await Assertions.Expect(orgFields).ToBeVisibleAsync();

        await Page.Locator("#is-org").UncheckAsync();
        await Assertions.Expect(orgFields).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task Meal_choices_survive_an_add_day_guest_postback()
    {
        // Regression for the "choices reset on postback" bug: the add/remove-day-guest round-trip
        // re-renders the whole form, and the posted grid must round-trip instead of blanking out.
        // Adding a day guest does not consume the token, so this shares the read-only token.
        await Page.GotoAsync(CompletionUrl);

        var lunch = Page.Locator("[data-sitting][data-meal='Lunch']:visible select").First;
        await lunch.SelectOptionAsync(new SelectOptionValue { Label = "Ugnsbakad lax" });
        var chosen = await lunch.InputValueAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Lägg till dagsgäst" }).ClickAsync();

        // A new day-guest row proves the postback happened; the meal select kept its value.
        await Assertions.Expect(Page.Locator(".pcard[aria-label='Dagsgäst 1']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("[data-sitting][data-meal='Lunch']:visible select").First)
            .ToHaveValueAsync(chosen);
    }

    [Fact]
    public async Task Completion_submits_successfully_to_the_acknowledgement()
    {
        await Page.GotoAsync(Url(E2EFixture.CompletionSubmitToken));

        await Page.Locator(".prow[aria-label='Deltagare 1'] .prow__name").FillAsync("Anna Andersson");
        await Page.Locator(".prow[aria-label='Deltagare 2'] .prow__name").FillAsync("Bengt Berg");
        await ChooseEveryVisibleSittingAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka in uppgifter" }).ClickAsync();

        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).ToBeVisibleAsync();
    }

    /// <summary>Picks the first real option on every sitting the group is taking, so the form is
    /// complete — the server requires a dish for each (§4.1).</summary>
    private async Task ChooseEveryVisibleSittingAsync()
    {
        var selects = Page.Locator("[data-sitting]:visible select");
        var count = await selects.CountAsync();
        for (var i = 0; i < count; i++)
            await selects.Nth(i).SelectOptionAsync(new SelectOptionValue { Index = 1 });
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
        await ChooseEveryVisibleSittingAsync();

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
