using System.Text.RegularExpressions;
using AkeroGarden.E2E.Infrastructure;
using AkeroGarden.Web.Core.Services;
using ClosedXML.Excel;
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
        // The edge-day panel specifically — the Excel upload (FR-118) uses the same box.
        await Assertions.Expect(Page.Locator(".group-default", new() { HasText = "Ankomst- och avresedagen" })).ToBeVisibleAsync();
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
    public async Task Every_lunch_and_dinner_dropdown_offers_the_whole_menu()
    {
        // One menu (§4.5): a dish has no lunch/dinner type, so both kinds of sitting list every
        // active dish. Contains rather than equals — the E2E database is shared, and another test
        // may briefly have its own dish on the menu.
        await Page.GotoAsync(CompletionUrl);

        string[] menu = ["Rotfruktssoppa", "Ugnsbakad lax", "Grönsakslasagne", "Kycklinggryta"];
        foreach (var sitting in new[] { "Lunch", "Dinner" })
        {
            var offered = await Page.Locator($"[data-sitting][data-meal='{sitting}']:visible select").First
                .Locator("option").AllInnerTextsAsync();
            Assert.All(menu, dish => Assert.Contains(dish, offered));
        }
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

    [Fact]
    public async Task After_submitting_back_and_reload_send_nothing_and_never_show_an_error()
    {
        // NFR-41. The link is single-use, so a re-sent form could never change the booking — but
        // while the thank-you was the POST response, Back or Reload re-sent it and the guest who had
        // just submitted was shown "Uppgifterna är redan inskickade … Behöver du ändra något?".
        var url = Url(E2EFixture.CompletionResubmitToken);
        await Page.GotoAsync(url);
        await Page.Locator(".prow[aria-label='Deltagare 1'] .prow__name").FillAsync("Anna Andersson");
        await Page.Locator(".prow[aria-label='Deltagare 2'] .prow__name").FillAsync("Bengt Berg");
        await ChooseEveryVisibleSittingAsync();

        var posts = new List<string>();
        Page.Request += (_, r) =>
        {
            if (r.Method == "POST" && r.Url.Contains("/slutfor/")) posts.Add(r.Url);
        };
        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka in uppgifter" }).ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/slutfor/tack$"));
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).ToBeVisibleAsync();

        await Page.ReloadAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).ToBeVisibleAsync();

        // Back is the link itself, read fresh: the honest state of a used link, and no re-send.
        await Page.GoBackAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(Regex.Escape(url) + "$"));
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Uppgifterna är redan inskickade" }))
            .ToBeVisibleAsync();

        Assert.Single(posts);
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
    public async Task Uploading_the_filled_template_fills_the_form_and_the_form_then_submits()
    {
        // FR-118, the whole journey: the guest downloads this booking's template from the form, fills
        // it in, uploads it, sees the rows filled, and submits through the ordinary validation. The
        // upload itself submits nothing — asserted by the form still being there afterwards.
        await Page.GotoAsync(Url(E2EFixture.CompletionUploadToken));

        var download = await Page.RunAndWaitForDownloadAsync(() =>
            Page.GetByRole(AriaRole.Link, new() { Name = "Ladda ner mall (Excel)" }).ClickAsync());
        var template = await File.ReadAllBytesAsync(await download.PathAsync());

        var filled = Path.Combine(Path.GetTempPath(), $"deltagare-{Guid.NewGuid():N}.xlsx");
        using (var workbook = new XLWorkbook(new MemoryStream(template)))
        {
            var overnight = workbook.Worksheet(ParticipantTemplate.OvernightSheet);
            var first = ParticipantTemplate.HeaderRow + 1;
            overnight.Cell(first, ParticipantTemplate.NameColumn).Value = "Anna Andersson";
            overnight.Cell(first, ParticipantTemplate.AgeColumn).Value = "Vuxen (4+)";
            overnight.Cell(first + 1, ParticipantTemplate.NameColumn).Value = "Bengt Berg";
            overnight.Cell(first + 1, ParticipantTemplate.AgeColumn).Value = "Barn (0–3)";
            overnight.Cell(first + 1, ParticipantTemplate.FirstAllergyColumn(false) + 2).Value = "Ja"; // Nötallergi

            // The day dropdown's first value — whatever the seeded stay's first day is called.
            var firstDay = workbook.Worksheet(ParticipantTemplate.ListsSheet).Cell(1, 2).GetString();
            var dayGuests = workbook.Worksheet(ParticipantTemplate.DayGuestSheet);
            dayGuests.Cell(first, ParticipantTemplate.NameColumn).Value = "Dag Dagsson";
            dayGuests.Cell(first, ParticipantTemplate.AgeColumn).Value = "Vuxen (4+)";
            dayGuests.Cell(first, ParticipantTemplate.DayColumn).Value = firstDay;
            workbook.SaveAs(filled);
        }

        try
        {
            await Page.Locator("#participant-file").SetInputFilesAsync(filled);
            await Page.GetByRole(AriaRole.Button, new() { Name = "Fyll i från filen" }).ClickAsync();

            await Assertions.Expect(Page.Locator(".alert--info"))
                .ToContainTextAsync("2 deltagare och 1 dagsgäst har fyllts i från filen");
            await Assertions.Expect(Page.Locator(".prow[aria-label='Deltagare 1'] .prow__name")).ToHaveValueAsync("Anna Andersson");
            await Assertions.Expect(Page.Locator(".prow[aria-label='Deltagare 2'] .prow__name")).ToHaveValueAsync("Bengt Berg");
            await Assertions.Expect(Page.Locator(".prow[aria-label='Deltagare 2'] .prow__age")).ToHaveValueAsync("Child");
            await Assertions.Expect(Page.Locator(".prow[aria-label='Deltagare 2'] input[value='Nut']")).ToBeCheckedAsync();
            await Assertions.Expect(Page.Locator("#d0-name")).ToHaveValueAsync("Dag Dagsson");

            await ChooseEveryVisibleSittingAsync();
            // Bengt's nut allergy needs the consent (FR-110) — the upload changes none of the rules.
            await Page.Locator("input[name='Form.AllergyConsent'][type='checkbox']").CheckAsync();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka in uppgifter" }).ClickAsync();

            await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).ToBeVisibleAsync();
        }
        finally
        {
            File.Delete(filled);
        }
    }

    [Fact]
    public async Task A_file_that_is_not_the_template_fills_nothing_and_keeps_what_was_typed()
    {
        // All or nothing (FR-118): a wrong file changes nothing on the form, and the guest's own
        // typing survives the round trip. Importing never consumes the link, so this shares the
        // read-only token.
        await Page.GotoAsync(CompletionUrl);
        await Page.Locator(".prow[aria-label='Deltagare 1'] .prow__name").FillAsync("Redan Skriven");

        await Page.Locator("#participant-file").SetInputFilesAsync(new FilePayload
        {
            Name = "deltagare.xlsx",
            MimeType = ParticipantTemplate.ContentType,
            Buffer = System.Text.Encoding.UTF8.GetBytes("Namn;Ålder\nAnna;Vuxen\n"),
        });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Fyll i från filen" }).ClickAsync();

        await Assertions.Expect(Page.Locator(".alert--error")).ToContainTextAsync("Inget har fyllts i.");
        await Assertions.Expect(Page.Locator(".alert--error")).ToContainTextAsync(ParticipantImport.NotExcel);
        await Assertions.Expect(Page.Locator(".prow[aria-label='Deltagare 1'] .prow__name")).ToHaveValueAsync("Redan Skriven");
    }

    [Fact]
    public async Task Pressing_upload_without_a_file_asks_for_one()
    {
        await Page.GotoAsync(CompletionUrl);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Fyll i från filen" }).ClickAsync();

        await Assertions.Expect(Page.Locator(".alert--error")).ToContainTextAsync(ParticipantImport.NoFile);
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

    [Fact]
    public async Task Completion_thank_you_has_no_serious_accessibility_violations()
    {
        // Its own page since NFR-41 — no longer covered by the form's check above.
        await Page.GotoAsync("/slutfor/tack");

        var results = await Page.RunAxe();
        var serious = results.Violations.Where(v => v.Impact is "serious" or "critical").ToList();

        Assert.True(
            serious.Count == 0,
            "axe found serious/critical a11y violations on the completion thank-you:\n" +
            string.Join("\n", serious.SelectMany(v => v.Nodes.Select(n => $"  [{v.Id}] {n.Html}"))));
    }
}
