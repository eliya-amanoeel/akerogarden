using System.Text.RegularExpressions;
using AkeroGarden.E2E.Infrastructure;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Services;
using Deque.AxeCore.Playwright;
using Microsoft.EntityFrameworkCore;
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

        // Exact, not Contains: this is the site's search-result headline, and "Åkerögården — Välkommen"
        // satisfied a Contains check while saying nothing about the place.
        Assert.Equal("Åkerögården — läger- och retreatgård i Norrtälje", await Page.TitleAsync());
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
    public async Task Hero_photo_is_cropped_to_its_frame_at_every_size()
    {
        foreach (var vp in new[] { Mobile, Tablet, Desktop })
        {
            await Page.SetViewportSizeAsync(vp.Width, vp.Height);
            await Page.GotoAsync("/");

            var frame = Page.Locator(".hero__photo");
            var img = frame.GetByRole(AriaRole.Img, new() { Name = "Huvudbyggnaden på Åkerögården, med trappan upp till altanen" });
            var what = $"hero photo at {vp.Width}px";

            await PagePhotoAssertions.LoadsAndFillsFrameAsync(img, frame, what);

            // Below 64em the frame is its min-height — 11rem on a phone, which is what keeps the primary
            // CTA above the fold (design.md, Mobile fold discipline), so a photo stretching it would
            // push the funnel down. From 64em it deliberately stretches to the text column beside it.
            if (vp.Width < 1024)
                await PagePhotoAssertions.FrameHeightIsItsMinHeightAsync(frame, what);

            await Assertions.Expect(frame.Locator(".photo-tag")).ToHaveCountAsync(0);
        }
    }

    [Fact]
    public async Task Link_preview_image_is_served_as_a_jpeg()
    {
        // The og:image URL is absolute on the production host, so only its path can be fetched here —
        // but the path is the part that breaks (a renamed or unpublished file). A tag pointing at a 404
        // gives every shared link a blank card, and nothing on the page itself would show it.
        await Page.GotoAsync("/");
        var content = await Page.Locator("meta[property='og:image']").GetAttributeAsync("content");
        Assert.NotNull(content);
        var path = new Uri(content).AbsolutePath;

        var response = await Page.APIRequest.GetAsync(path);
        Assert.Equal(200, response.Status);
        Assert.Equal("image/jpeg", response.Headers["content-type"]);
        Assert.True((await response.BodyAsync()).Length > 10_000, "og:image is suspiciously small");
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

    [Theory]
    [InlineData("Läger & retreat", "Altanen med bord och stolar en solig sommardag")]
    [InlineData("Konferens & möte", "Samlingssal med röda stolar framför en öppen spis")]
    [InlineData("Kurs & workshop", "Kursrum med röda stolar, stora fönster och en färgglad bonad på väggen")]
    public async Task Usecard_photo_is_cropped_to_its_frame_at_every_size(string heading, string alt)
    {
        foreach (var vp in new[] { Mobile, Tablet, Desktop })
        {
            await Page.SetViewportSizeAsync(vp.Width, vp.Height);
            await Page.GotoAsync("/");

            var card = Page.Locator(".usecard").Filter(new() { Has = Page.GetByRole(AriaRole.Heading, new() { Name = heading }) });
            var frame = card.Locator(".usecard__img");
            var img = card.GetByRole(AriaRole.Img, new() { Name = alt });
            var what = $"'{heading}' photo at {vp.Width}px";

            await PagePhotoAssertions.LoadsAndFillsFrameAsync(img, frame, what);
            await PagePhotoAssertions.FrameAspectIsAsync(frame, 4.0 / 3.0, what);
            await Assertions.Expect(card.Locator(".photo-tag")).ToHaveCountAsync(0);
        }
    }

    [Fact]
    public async Task Facilities_teaser_photo_is_cropped_to_its_frame_at_every_size()
    {
        // Tablet matters most here: the frame is a ~3:1 band at 768px.
        foreach (var vp in new[] { Mobile, Tablet, Desktop })
        {
            await Page.SetViewportSizeAsync(vp.Width, vp.Height);
            await Page.GotoAsync("/");

            // Found by its link, not its heading: the heading counts the active houses ("Åtta hus, en
            // gård" in production), and other tests add houses while this suite runs.
            var teaser = Page.Locator(".teaser").Filter(new() { Has = Page.Locator("a[href='/faciliteter']") });
            var frame = teaser.Locator(".teaser__photo");
            var img = teaser.GetByRole(AriaRole.Img, new() { Name = "Flygbild över gården med husen utspridda längs gångvägar och gräsytor" });
            var what = $"facilities teaser photo at {vp.Width}px";

            await PagePhotoAssertions.LoadsAndFillsFrameAsync(img, frame, what);
            await PagePhotoAssertions.FrameHeightIsItsMinHeightAsync(frame, what);
            await Assertions.Expect(teaser.Locator(".photo-tag")).ToHaveCountAsync(0);
        }
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

    [Fact]
    public async Task Home_and_om_oss_count_the_lodging_rather_than_state_it()
    {
        // FR-81: every bed and house count on the public site is counted from the active rooms,
        // never written into the copy. A typed number would survive this: the test adds one house
        // with a known number of beds and requires all five places on the two pages to move by
        // exactly that. The baseline is read off the page and checked for agreement first, so the
        // test does not depend on what else the shared database holds.
        const int addedBeds = 7;
        var (beds, houses) = await ReadCapacityAsync();
        await ExpectCapacityAsync(beds, houses);

        var pb = new PhysicalBuilding { Id = Guid.NewGuid(), Name = $"E2E Räknehus {Guid.NewGuid():N}", IsActive = true };
        var house = new Building { Id = Guid.NewGuid(), Name = "E2E Räknestuga", PhysicalBuildingId = pb.Id, IsActive = true };
        house.Rooms.Add(new Room { Id = Guid.NewGuid(), BuildingId = house.Id, Name = "Rum 1", BedCount = addedBeds, IsActive = true });

        await using (var db = Fixture.NewDbContext())
        {
            db.PhysicalBuildings.Add(pb);
            db.Buildings.Add(house);
            await db.SaveChangesAsync();
        }

        try
        {
            await ExpectCapacityAsync(beds + addedBeds, houses + 1);
        }
        finally
        {
            await using var db = Fixture.NewDbContext();
            await db.Rooms.Where(r => r.BuildingId == house.Id).ExecuteDeleteAsync();
            await db.Buildings.Where(b => b.Id == house.Id).ExecuteDeleteAsync();
            await db.PhysicalBuildings.Where(p => p.Id == pb.Id).ExecuteDeleteAsync();
        }
    }

    /// <summary>The counts Om oss shows in its fact strip — "N hus" and "Upp till N".</summary>
    private async Task<(int Beds, int Houses)> ReadCapacityAsync()
    {
        await Page.GotoAsync("/om-oss");
        var nums = await Page.Locator(".facts .facts__num").AllInnerTextsAsync();
        var houses = int.Parse(Regex.Match(nums.Single(n => n.EndsWith(" hus", StringComparison.Ordinal)), @"\d+").Value);
        var beds = int.Parse(Regex.Match(nums.Single(n => n.StartsWith("Upp till", StringComparison.Ordinal)), @"\d+").Value);
        return (beds, houses);
    }

    /// <summary>All five places that state the capacity, on both pages, say these numbers.</summary>
    private async Task ExpectCapacityAsync(int beds, int houses)
    {
        await Page.GotoAsync("/");
        await Assertions.Expect(Page.Locator(".hero__sub")).ToContainTextAsync($"till {beds} gäster");
        await Assertions.Expect(Page.Locator(".trust-strip__num", new() { HasText = "Upp till" })).ToHaveTextAsync($"Upp till {beds}");
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = $"{SwedishNumbers.CapitalisedWord(houses)} hus, en gård" }))
            .ToBeVisibleAsync();

        await Page.GotoAsync("/om-oss");
        await Assertions.Expect(Page.Locator("p", new() { HasText = "hus att bo i" }))
            .ToContainTextAsync($"Gården har {SwedishNumbers.Word(houses)} hus att bo i, med sammanlagt plats för upp till {beds} gäster");
        await Assertions.Expect(Page.Locator(".facts .facts__num", new() { HasText = " hus" })).ToHaveTextAsync($"{houses} hus");
        await Assertions.Expect(Page.Locator(".facts .facts__num", new() { HasText = "Upp till" })).ToHaveTextAsync($"Upp till {beds}");
    }
}
