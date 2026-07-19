using System.Text.RegularExpressions;
using AkeroGarden.E2E.Infrastructure;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// Slice-3 trust pages. Most of these assert structure + accessibility rather than seeded
/// content, since these pages carry no reference data of their own — but the E2E database is
/// *not* empty: <see cref="E2EFixture.SeedReferenceDataAsync"/> seeds one Facility and one
/// Building (needed by the completion/cleaning tests) that also happen to be the only ones
/// /faciliteter has to render, which the lodging test below relies on directly.
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
    public async Task Mat_och_priser_opens_on_a_lead_photo_teaser()
    {
        await Page.GotoAsync("/mat-och-priser");

        // The page previously had no photography at all; the Menyn section now opens on the
        // shared .teaser component (same one used on Home/Facilities) before the dish list.
        await Assertions.Expect(Page.Locator(".teaser .teaser__photo")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Hemlagat, med plats för önskemål" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task About_page_has_a_second_photo_moment_and_a_real_closing_cta()
    {
        await Page.GotoAsync("/om-oss");

        // Previously one static photo carried the whole page; the grounds paragraph now gets
        // its own .teaser (reversed layout), and the three links that used to be buried
        // mid-sentence are now a real flat-card CTA.
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Drygt fem hektar att utforska" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".teaser--reverse .teaser__photo")).ToBeVisibleAsync();

        await Page.Locator(".cta-card a.btn").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/boka$"));
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

    [Fact]
    public async Task Contact_info_is_split_into_labelled_groups()
    {
        await Page.GotoAsync("/kontakt");

        // Was one flat <dl> with six equal-weight rows; now two labelled groups so "how to
        // reach us" and "how to get here" read as distinct, plus the location photo anchor.
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Kontakta oss", Level = 2 })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Hitta hit", Level = 2 })).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".contact-photo")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Facilities_renders_the_seeded_facility_as_lead_feature_and_the_lodging_group()
    {
        await Page.GotoAsync("/faciliteter");

        // E2EFixture.SeedReferenceDataAsync seeds exactly one active Facility ("Konferensrum",
        // needed by the cleaning-completion tests) and one Building ("Huvudbyggnaden") under one
        // PhysicalBuilding ("Sovbyggnad", needed by the room-capacity tests) — the DB is never
        // actually empty in this suite. With a single facility it becomes the lead .teaser
        // feature and the "Fler faciliteter" grid doesn't render (nothing left to fill it).
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Konferensrum", Level = 2 })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByText("Övriga faciliteter på gården")).Not.ToBeVisibleAsync();

        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sovbyggnad", Level = 3 })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Huvudbyggnaden", Level = 4 })).ToBeVisibleAsync();

        // The page closes on a quiet sign-off line instead of a CTA band (header CTA + docked
        // mobile CTA already cover the funnel route on this page).
        await Assertions.Expect(Page.GetByText("Vilket sällskap ni än är")).ToBeVisibleAsync();
    }
}
