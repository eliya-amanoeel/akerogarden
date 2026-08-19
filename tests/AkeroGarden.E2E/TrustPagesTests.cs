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
    public async Task Address_is_plain_text_now_that_the_map_carries_the_route()
    {
        await Page.GotoAsync("/kontakt");

        // The address used to be the link to Google Maps. The map above it is that link now, so
        // the address went back to being text — two targets for one destination, and the smaller
        // of the two was the one people had to hit. The link-out itself is asserted below in
        // Contact_map_is_inline_and_links_out_rather_than_embedding_a_provider.
        var address = Page.Locator(".contact-dl dd", new() { HasTextString = "Åkerövägen 81" });

        await Assertions.Expect(address).ToBeVisibleAsync();
        Assert.Equal(0, await address.Locator("a").CountAsync());
    }

    [Fact]
    public async Task Contact_map_is_inline_and_links_out_rather_than_embedding_a_provider()
    {
        await Page.GotoAsync("/kontakt");

        // Inline SVG rather than <img> or <iframe>. <img> would sandbox it away from the site's
        // tokens and self-hosted fonts; an embedded provider map would load third-party script,
        // tiles and cookies for every visitor regardless of intent — the NFR-35 reasoning that
        // already rules out Google Fonts, and the reason §7 of the privacy policy can still say
        // no third-party service places cookies here.
        await Assertions.Expect(Page.Locator("figure.findmap-figure svg.findmap")).ToBeVisibleAsync();
        Assert.Equal(0, await Page.Locator("iframe").CountAsync());

        var mapLink = Page.GetByRole(
            AriaRole.Link,
            new() { Name = "Öppna Åkerögården i Google Maps för vägbeskrivning" });

        await Assertions.Expect(mapLink).ToHaveAttributeAsync("target", "_blank");
        await Assertions.Expect(mapLink).ToHaveAttributeAsync("rel", "noopener");

        var href = await mapLink.GetAttributeAsync("href");
        Assert.StartsWith("https://www.google.com/maps/search/?api=1&query=", href);
    }

    [Fact]
    public async Task Contact_map_credits_the_openstreetmap_source()
    {
        // The geometry is derived from OpenStreetMap, so ODbL requires the source to be credited.
        // It lives in the caption rather than inside the drawing, where it would be unselectable.
        await Page.GotoAsync("/kontakt");

        var attribution = Page.Locator(".findmap-figure__caption a");

        await Assertions.Expect(attribution)
            .ToHaveAttributeAsync("href", "https://www.openstreetmap.org/copyright");
        await Assertions.Expect(attribution).ToContainTextAsync("OpenStreetMap");
    }

    /// <summary>
    /// What a map label is actually rendered at: its computed font size is in viewBox units, so the
    /// physical size is that times the map's own scale factor. Reading the CSS alone tells you
    /// nothing — both label-size defects this file guards against measured fine in the stylesheet.
    /// </summary>
    private async Task<double> RenderedMapLabelPxAsync() =>
        await Page.EvaluateAsync<double>(@"() => {
            const svg = document.querySelector('svg.findmap');
            const label = document.querySelector('.findmap__label');
            const scale = svg.getBoundingClientRect().width / svg.viewBox.baseVal.width;
            return parseFloat(getComputedStyle(label).fontSize) * scale;
        }");

    [Fact]
    public async Task Contact_map_labels_stay_legible_where_the_map_is_narrowest()
    {
        // 1024px is where .contact-grid turns two-column and the map drops to roughly 495px — the
        // narrowest it gets on a desktop, and the case the first sizes were never checked against.
        // They were picked against the widest rendering and landed at 9.7px here: the smallest text
        // on the site, one pixel of viewport away from 19px.
        await Page.SetViewportSizeAsync(1024, 900);
        await Page.GotoAsync("/kontakt");

        var rendered = await RenderedMapLabelPxAsync();

        Assert.True(
            rendered >= 11.0,
            $"Kartetiketterna renderas som {rendered:0.0}px vid 1024px bred vy — under 11px är de "
            + "sidans minsta text. Storlekarna är viewBox-enheter: mät den smalaste renderingen, inte den bredaste.");
    }

    [Fact]
    public async Task Contact_map_labels_are_sized_in_rem_so_they_follow_the_reader()
    {
        await Page.GotoAsync("/kontakt");

        // A proxy, and deliberately labelled as one. The behavioural version of this test — raise the
        // root font size, then measure — could not be made to run: an injected `html { font-size: 24px
        // !important }` is enumerable in document.styleSheets, its sheet reports enabled with empty
        // media and the right ownerNode, a custom property set by the very same rule *does* apply, and
        // yet font-size and colour from that rule never take effect. Setting the inline style on <html>
        // behaved identically. The cause was never found (see the notes in this repo's session memory).
        //
        // What this asserts instead is the property the fix turns on: the unit. In `px` the map is the
        // one thing on the page that ignores the reader's text-size setting — and it does not merely
        // fail to grow, it shrinks, because .container's rem padding narrows the column and drops the
        // map's scale factor. Verified by hand against the running site: at a 24px root the caption goes
        // 13.6 -> 20.4px while px labels went 13.7 -> 13.0px. If the behavioural route ever becomes
        // reliable, replace this with it rather than adding to it.
        var units = await Page.EvaluateAsync<string>(@"() => {
            const wanted = ['.findmap__label', '.findmap__place', '.findmap__region'];
            const found = {};
            for (const sheet of document.styleSheets) {
                let rules = [];
                try { rules = [...sheet.cssRules]; } catch (e) { continue; }
                const walk = list => {
                    for (const r of list) {
                        // Check the rule itself *before* recursing. A plain CSSStyleRule also exposes
                        // .cssRules these days (CSS nesting) — an empty list, which is truthy, so
                        // treating it as the grouping-rule test skips every ordinary rule.
                        if (r.selectorText && r.style && r.style.fontSize) {
                            for (const w of wanted) {
                                if (r.selectorText.split(',').some(p => p.trim() === w)) found[w] = r.style.fontSize;
                            }
                        }
                        if (r.cssRules && r.cssRules.length) walk([...r.cssRules]);
                    }
                };
                walk(rules);
            }
            return wanted.map(w => w + '=' + (found[w] || 'MISSING')).join(', ');
        }");

        Assert.DoesNotContain("MISSING", units);
        Assert.DoesNotContain("px", units);
        Assert.Contains("rem", units);
    }

    [Fact]
    public async Task Contact_map_labels_do_not_collide_at_their_largest()
    {
        // Below 530px the map switches to the branch that hides the supporting labels and raises the
        // rest — the largest the type ever gets relative to the drawing, and therefore the case where
        // stacked labels would run into each other. They do not, because the second line of each pair
        // is a tspan with an em-based dy, so its offset scales with the type rather than staying at a
        // hard-coded y. This is the collision half of the size work; the viewport is the one lever that
        // demonstrably works in this suite.
        await Page.SetViewportSizeAsync(400, 900);
        await Page.GotoAsync("/kontakt");

        var overlaps = await Page.EvaluateAsync<string>(@"() => {
            const leaves = [...document.querySelectorAll('svg.findmap text, svg.findmap tspan')]
                .filter(e => e.children.length === 0
                             && e.textContent.trim()
                             && getComputedStyle(e).display !== 'none');
            const boxes = leaves.map(e => ({ t: e.textContent.trim(), b: e.getBBox() }));
            const hits = [];
            for (let i = 0; i < boxes.length; i++) {
                for (let j = i + 1; j < boxes.length; j++) {
                    const a = boxes[i].b, b = boxes[j].b;
                    if (a.x < b.x + b.width && b.x < a.x + a.width
                        && a.y < b.y + b.height && b.y < a.y + a.height) {
                        hits.push(boxes[i].t + ' / ' + boxes[j].t);
                    }
                }
            }
            return hits.join(', ');
        }");

        Assert.True(overlaps.Length == 0, $"Kartetiketter överlappar varandra på smal skärm: {overlaps}");

        var rendered = await RenderedMapLabelPxAsync();
        Assert.True(rendered >= 11.0, $"Kartetiketterna renderas som {rendered:0.0}px på 400px bred vy.");
    }

    [Fact]
    public async Task Contact_page_loads_nothing_from_a_third_party()
    {
        // The claim the map has to keep true, asserted directly rather than by reading the markup:
        // rendering /kontakt must not reach any host but our own. This is what would break the
        // moment someone swaps the drawing for a provider embed or reaches for a font CDN.
        var external = new List<string>();
        Page.Request += (_, request) =>
        {
            if (request.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && !request.Url.StartsWith(AppBaseUrl, StringComparison.Ordinal))
            {
                external.Add(request.Url);
            }
        };

        await Page.GotoAsync("/kontakt", new() { WaitUntil = WaitUntilState.NetworkIdle });

        Assert.True(
            external.Count == 0,
            "/kontakt hämtade resurser från tredje part: " + string.Join(", ", external));
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
