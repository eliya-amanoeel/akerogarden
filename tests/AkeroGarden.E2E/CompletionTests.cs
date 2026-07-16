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

        // Group defaults are now per day: a day's lunch cascades to THAT day's slot for everyone.
        var gdLunch = Page.Locator("select[data-groupdefault-slot][data-meal='Lunch']").First;
        var slot = await gdLunch.GetAttributeAsync("data-groupdefault-slot");
        await gdLunch.SelectOptionAsync(new SelectOptionValue { Label = "Ugnsbakad lax" });
        var chosen = await gdLunch.InputValueAsync();

        // A participant whose own select was never touched now carries the group choice for that day.
        await Page.Locator(".prow[aria-label='Deltagare 2'] .prow__toggle").ClickAsync();
        await Assertions.Expect(Page.Locator($".prow[aria-label='Deltagare 2'] select[data-slot='{slot}']"))
            .ToHaveValueAsync(chosen);
    }

    [Fact]
    public async Task Group_default_meal_pickers_are_hidden_when_group_default_full_board_is_unchecked()
    {
        // Regression for the Phase-8 smoke-test bug: on a SelfCateringAll booking the "Helpension
        // för alla" checkbox correctly renders unticked, but the per-day meal pickers rendered
        // regardless — there was no gate mirroring the per-participant syncMeals behaviour.
        await Page.GotoAsync(Url(E2EFixture.SelfCateringCompletionToken));

        await Assertions.Expect(Page.Locator("[data-groupdefault-fullboard]")).Not.ToBeCheckedAsync();
        await Assertions.Expect(Page.Locator("[data-groupdefault-days]").First).Not.ToBeVisibleAsync();

        await Page.Locator("[data-groupdefault-fullboard]").CheckAsync();
        await Assertions.Expect(Page.Locator("[data-groupdefault-days]").First).ToBeVisibleAsync();
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
    public async Task Group_default_value_survives_an_add_day_guest_postback()
    {
        // Regression for the "shortcut resets on postback" bug: the group-default controls are
        // JS-cascade helpers, but their displayed value must round-trip the add/remove-day-guest
        // server postback (which re-renders the whole form) instead of blanking out. Adding a day
        // guest does not consume the token, so this can share the read-only CompletionToken.
        await Page.GotoAsync(CompletionUrl);

        var gdLunch = Page.Locator("select[data-groupdefault-slot][data-meal='Lunch']").First;
        await gdLunch.SelectOptionAsync(new SelectOptionValue { Label = "Ugnsbakad lax" });
        var chosen = await gdLunch.InputValueAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Lägg till dagsgäst" }).ClickAsync();

        // A new day-guest row proves the postback happened; the group-default select kept its value.
        await Assertions.Expect(Page.Locator(".pcard[aria-label='Dagsgäst 1']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("select[data-groupdefault-slot][data-meal='Lunch']").First)
            .ToHaveValueAsync(chosen);
    }

    [Fact]
    public async Task Completion_submits_successfully_to_the_acknowledgement()
    {
        await Page.GotoAsync(Url(E2EFixture.CompletionSubmitToken));

        await Page.Locator(".prow[aria-label='Deltagare 1'] .prow__name").FillAsync("Anna Andersson");
        await Page.Locator(".prow[aria-label='Deltagare 2'] .prow__name").FillAsync("Bengt Berg");
        // Each day's group-default lunch cascades to that day's slot for every participant; dinner is
        // single-option and auto-filled — so every full-board meal slot is satisfied.
        await SelectEveryGroupDefaultLunchAsync("Ugnsbakad lax");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka in uppgifter" }).ClickAsync();

        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).ToBeVisibleAsync();
    }

    /// <summary>Selects the given option on every per-day group-default lunch dropdown, cascading it
    /// to that day's slot for all participants (the per-day successor to the old single "all days").</summary>
    private async Task SelectEveryGroupDefaultLunchAsync(string label)
    {
        var selects = Page.Locator("select[data-groupdefault-slot][data-meal='Lunch']");
        var count = await selects.CountAsync();
        for (var i = 0; i < count; i++)
            await selects.Nth(i).SelectOptionAsync(new SelectOptionValue { Label = label });
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
        await SelectEveryGroupDefaultLunchAsync("Ugnsbakad lax");

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
