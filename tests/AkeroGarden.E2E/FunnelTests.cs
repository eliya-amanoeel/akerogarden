using System.Text.RegularExpressions;
using AkeroGarden.E2E.Infrastructure;
using Deque.AxeCore.Playwright;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// The booking funnel — the primary KPI. Covers the full journey home → kalender → boka → tack,
/// the two-track toggle, date pre-fill from the calendar, and accessibility of the funnel pages.
/// </summary>
public class FunnelTests(E2EFixture fixture) : PublicE2ETestBase(fixture)
{
    [Fact]
    public async Task Full_funnel_home_to_calendar_to_inquiry_to_thanks()
    {
        // Discover → availability
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Se tillgänglighet" }).First.ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/kalender$"));

        var captureDir = Environment.GetEnvironmentVariable("E2E_CAPTURE_DIR");
        if (captureDir is not null)
            await Page.ScreenshotAsync(new() { Path = Path.Combine(captureDir, "kalender.png"), FullPage = true });

        // Pick a date range on the calendar (empty DB => every future day is free/selectable).
        var freeDays = Page.Locator(".cal-day.cal-free a");
        await freeDays.Nth(0).ClickAsync();
        await freeDays.Nth(4).ClickAsync();
        await Assertions.Expect(Page.Locator("#cal-selection")).ToBeVisibleAsync();
        await Page.Locator("#cal-go").ClickAsync();

        // Inquiry form — dates arrive pre-filled from the calendar
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/boka\?"));
        await Assertions.Expect(Page.Locator(".prefill").First).ToBeVisibleAsync();
        Assert.False(string.IsNullOrEmpty(await Page.Locator("#s-checkin").InputValueAsync()));

        await Page.FillAsync("#s-name", "Anna Andersson");
        await Page.FillAsync("#s-email", "anna@example.se");
        await Page.FillAsync("#s-email-confirm", "anna@example.se");
        // Leave the optional phone blank on purpose — that is the real, common path, and it must
        // submit successfully (regression guard for the OptionalPhone fix).
        await Page.FillAsync("#s-guests", "24");
        await Page.SelectOptionAsync("#mealplan", "FullBoardAll");
        await Page.Locator(".track--stay label:has-text('integritetspolicyn') input[type=checkbox]").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka bokningsförfrågan" }).ClickAsync();

        // Acknowledgement
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).ToBeVisibleAsync();
    }

    /// <summary>Fills every required stay-request field with valid values, so a test can invalidate a
    /// single field and assert its message in isolation. Leaves the optional phone blank.</summary>
    private static async Task FillValidStayFormAsync(IPage page)
    {
        await page.FillAsync("#s-name", "Anna Andersson");
        await page.FillAsync("#s-email", "anna@example.se");
        await page.FillAsync("#s-email-confirm", "anna@example.se");
        // Relative to today, never a literal: the date inputs carry min="today" (FR-113), so a
        // hardcoded past date would be rejected by the browser and never post — and the tests built
        // on this helper assert *server-side* messages, which only appear if the post happens.
        var checkIn = DateTime.UtcNow.Date.AddDays(60);
        await page.FillAsync("#s-checkin", checkIn.ToString("yyyy-MM-dd"));
        await page.FillAsync("#s-checkout", checkIn.AddDays(2).ToString("yyyy-MM-dd"));
        await page.FillAsync("#s-guests", "12");
        await page.SelectOptionAsync("#mealplan", "FullBoardAll");
        await page.Locator(".track--stay label:has-text('integritetspolicyn') input[type=checkbox]").CheckAsync();
    }

    [Fact]
    public async Task Client_side_validation_blocks_the_postback_on_invalid_input()
    {
        // A2d core claim: an invalid submit is caught in the browser and never posts — no wasted
        // round-trip that re-renders just to show an error and scrolls the guest away from it.
        // We assert the inline message appears AND that no POST to /boka was made.
        await Page.GotoAsync("/boka");
        await FillValidStayFormAsync(Page);
        await Page.FillAsync("#s-email-confirm", "typo@example.se");   // mismatch — client-detectable

        var postsToBoka = new List<string>();
        void OnRequest(object? _, IRequest r)
        {
            if (r.Method == "POST" && r.Url.Contains("/boka")) postsToBoka.Add(r.Url);
        }
        Page.Request += OnRequest;
        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka bokningsförfrågan" }).ClickAsync();

        await Assertions.Expect(Page.GetByText("E-postadresserna stämmer inte överens.")).ToBeVisibleAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/boka$"));
        Page.Request -= OnRequest;

        Assert.Empty(postsToBoka); // blocked client-side — nothing posted
    }

    [Fact]
    public async Task Client_side_acknowledgement_message_names_both_documents()
    {
        // inquiry-form.js mirrors the server's field names and messages by hand, and nothing tested
        // it — so when AcceptPrivacyPolicy became AcceptTerms (FR-109) the mirror kept the old field
        // name and the pre-FR-109 wording. The client gate silently stopped matching the field while
        // still rendering a message that named only the privacy policy. Found by hand, not by CI.
        await Page.GotoAsync("/boka");
        await FillValidStayFormAsync(Page);
        await Page.Locator(".track--stay label:has-text('bokningsvillkoren') input[type=checkbox]").UncheckAsync();

        var postsToBoka = new List<string>();
        void OnRequest(object? _, IRequest r)
        {
            if (r.Method == "POST" && r.Url.Contains("/boka")) postsToBoka.Add(r.Url);
        }
        Page.Request += OnRequest;
        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka bokningsförfrågan" }).ClickAsync();

        var message = Page.GetByText("Du måste godkänna integritetspolicyn och bokningsvillkoren för att skicka förfrågan.");
        await Assertions.Expect(message).ToBeVisibleAsync();
        Page.Request -= OnRequest;

        // Still caught in the browser, like every other client-detectable rule.
        Assert.Empty(postsToBoka);
    }

    [Fact]
    public async Task With_javascript_disabled_the_server_still_blocks_an_invalid_email()
    {
        // Graceful degradation (A2d): with the client-side script inert, the invalid email must
        // still be caught — the post goes through and our styled server [EmailAddress] message
        // renders. The server is always authoritative (belt-and-suspenders).
        await using var context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Fixture.PublicBaseUrl,
            JavaScriptEnabled = false,
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync("/boka");
        // Submitting a malformed email must be caught by the server even with no client script — the
        // post goes through and the styled [EmailAddress] message renders. (Only the email is filled;
        // the point is that the server, not the browser, rejects it. The CSS-only track toggle keeps
        // the stay form reachable without JS.) Submit with Enter — a native form submission a JS-off
        // user can do without scrolling to the button.
        await page.FillAsync("#s-email", "anna-at-example.se");   // no @ → invalid
        await page.Locator("#s-email").PressAsync("Enter");

        await Assertions.Expect(page.GetByText("Ange en giltig e-postadress.")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task Organisation_without_a_name_is_blocked()
    {
        // OrgName is Conditional (§8): ticking "for an organisation" makes naming it required.
        await Page.GotoAsync("/boka");
        await FillValidStayFormAsync(Page);
        await Page.Locator("#s-isorg").CheckAsync();            // reveals the org panel (progressive JS)
        // Leave #s-orgname empty on purpose.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka bokningsförfrågan" }).ClickAsync();

        await Assertions.Expect(
            Page.GetByText("Organisationsnamn krävs när bokningen görs för en organisation."))
            .ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task Inquiry_toggle_switches_between_the_two_tracks()
    {
        await Page.GotoAsync("/boka");

        // Stay request is the default track.
        await Assertions.Expect(Page.Locator(".track--stay")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".track--general")).Not.ToBeVisibleAsync();

        // Toggle to the general-inquiry track.
        await Page.Locator(".segmented__btn[for='track-general']").ClickAsync();
        await Assertions.Expect(Page.Locator(".track--general")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".track--stay")).Not.ToBeVisibleAsync();
    }

    [Theory]
    [InlineData("/kalender")]
    [InlineData("/boka")]
    [InlineData("/boka/tack")]
    [InlineData("/kontakt/tack")]
    public async Task Funnel_page_has_no_serious_accessibility_violations(string path)
    {
        await Page.GotoAsync(path);

        var results = await Page.RunAxe();
        var serious = results.Violations.Where(v => v.Impact is "serious" or "critical").ToList();

        Assert.True(
            serious.Count == 0,
            $"axe found serious/critical a11y violations on {path}:\n" +
            string.Join("\n", serious.SelectMany(v => v.Nodes.Select(n => $"  [{v.Id}] {n.Html}"))));
    }

    [Fact]
    public async Task Arrival_and_departure_time_are_a_fixed_dropdown_prefilled_with_the_standard()
    {
        // FR-100: a constrained picker, not free text and not a native time field the guest can type
        // anything into. The standard times are pre-selected so the ordinary case needs no decision.
        await Page.GotoAsync("/boka");

        await Assertions.Expect(Page.Locator("#s-checkin-time")).ToHaveValueAsync("14:00");
        await Assertions.Expect(Page.Locator("#s-checkout-time")).ToHaveValueAsync("12:00");

        // 07:00–22:00 on the half hour = 31 choices, first and last as stated in §2.8.
        var options = Page.Locator("#s-checkin-time option");
        await Assertions.Expect(options).ToHaveCountAsync(31);
        await Assertions.Expect(options.First).ToHaveAttributeAsync("value", "07:00");
        await Assertions.Expect(options.Last).ToHaveAttributeAsync("value", "22:00");
    }

    [Fact]
    public async Task A_stated_arrival_time_reaches_the_database()
    {
        // Driven to the acknowledgement and then cross-checked against the row — "the dropdown
        // rendered" would not prove the value survives binding and normalisation.
        await Page.GotoAsync("/boka");
        var email = $"tid-{Guid.NewGuid():N}@example.se";

        await Page.FillAsync("#s-name", "Tidig Ankomst");
        await Page.FillAsync("#s-email", email);
        await Page.FillAsync("#s-email-confirm", email);
        var statedCheckIn = DateTime.UtcNow.Date.AddDays(120);
        await Page.FillAsync("#s-checkin", statedCheckIn.ToString("yyyy-MM-dd"));
        await Page.FillAsync("#s-checkout", statedCheckIn.AddDays(2).ToString("yyyy-MM-dd"));
        await Page.FillAsync("#s-guests", "8");
        await Page.SelectOptionAsync("#mealplan", "FullBoardAll");
        await Page.SelectOptionAsync("#s-checkin-time", "08:30");
        await Page.SelectOptionAsync("#s-checkout-time", "18:00");
        await Page.Locator(".track--stay label:has-text('integritetspolicyn') input[type=checkbox]").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka bokningsförfrågan" }).ClickAsync();

        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).ToBeVisibleAsync();

        await using var db = Fixture.NewDbContext();
        var booking = await db.Bookings.SingleAsync(b => b.ContactEmail == email);
        Assert.Equal(new TimeOnly(8, 30), booking.CheckInTime);
        Assert.Equal(new TimeOnly(18, 0), booking.CheckOutTime);
    }

    // ── Sent once (NFR-41) ───────────────────────────────────────────────────────────────────────
    // Production received one stay request three times in 40 seconds on 2026-10-08: the thank-you
    // was the response to the POST, and each step Back onto it re-sent the form. These drive the
    // browser the way that guest did and count what actually reached the server.

    /// <summary>Records every POST to <paramref name="path"/> the page sends from now on.</summary>
    private static List<string> RecordPosts(IPage page, string path)
    {
        var posts = new List<string>();
        page.Request += (_, r) =>
        {
            if (r.Method == "POST" && new Uri(r.Url).AbsolutePath == path) posts.Add(r.Url);
        };
        return posts;
    }

    [Fact]
    public async Task A_sent_stay_request_survives_back_reload_and_forward_without_being_sent_again()
    {
        await Page.GotoAsync("/boka");
        await FillValidStayFormAsync(Page);
        var email = $"en-gang-{Guid.NewGuid():N}@example.se";
        await Page.FillAsync("#s-email", email);
        await Page.FillAsync("#s-email-confirm", email);

        var posts = RecordPosts(Page, "/boka");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka bokningsförfrågan" }).ClickAsync();

        // The thank-you is its own address, reached by GET — the structural half of the claim. While
        // it was the POST response the URL stayed /boka, and that is what made Back re-send.
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/boka/tack$"));
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByText("Vi har tagit emot din bokningsförfrågan")).ToBeVisibleAsync();

        await Page.ReloadAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Tack!" })).ToBeVisibleAsync();

        // Back lands on the form the guest came from, usable. Whether Chromium restored it from its
        // page cache or reloaded it is not controlled here — the unlock on restore has its own test.
        await Page.GoBackAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/boka$"));
        await Assertions.Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Skicka bokningsförfrågan" }))
            .ToBeEnabledAsync();

        await Page.GoForwardAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/boka/tack$"));

        Assert.Single(posts);
        await using var db = Fixture.NewDbContext();
        Assert.Equal(1, await db.Bookings.CountAsync(b => b.ContactEmail == email));
    }

    /// <summary>
    /// Keeps the page in place when the form is submitted, and counts the submissions the browser
    /// would have sent. The listener sits on <c>window</c>, which the submit event reaches only after
    /// <c>document</c> — where submit-once.js decides — so it sees that decision (a submission the
    /// script blocked arrives already cancelled) and then cancels the rest itself, so no page load
    /// starts. Why not hold a real POST in flight instead: while a page load is pending, Playwright
    /// makes every element lookup wait for it to finish, so nothing on the page can be checked
    /// (tried 2026-10-09 — both tests timed out with the script present and absent alike).
    /// </summary>
    private static Task StaySubmittingInPlaceAsync(IPage page) => page.EvaluateAsync(@"() => {
            window.__sent = 0;
            window.addEventListener('submit', e => {
                if (!e.defaultPrevented) window.__sent++;
                e.preventDefault();
            });
        }");

    [Fact]
    public async Task A_second_click_while_the_first_submission_is_on_its_way_sends_nothing()
    {
        // The window the redirect cannot close: the guest clicks again while the first post is on its
        // way (1–2.5 s in production). A Playwright double-click does NOT test this: its two clicks are
        // milliseconds apart and Chromium replaces the first submission before sending it, so it stayed
        // green with submit-once.js removed (2026-10-09). Here both clicks are real and the page stays
        // put between them, as it does while a post is on its way; this test goes red without the script.
        await Page.GotoAsync("/boka");
        await FillValidStayFormAsync(Page);
        await StaySubmittingInPlaceAsync(Page);

        var submit = Page.GetByRole(AriaRole.Button, new() { Name = "Skicka bokningsförfrågan" });
        await submit.ClickAsync();
        // Measured after the first click, which scrolls the button into view.
        var box = await submit.BoundingBoxAsync();
        Assert.NotNull(box);

        // A plain mouse click at the button, as a person makes it — no waiting for it to be clickable,
        // because a greyed-out button is exactly what a person would click on here.
        await Page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2);

        Assert.Equal(1, await Page.EvaluateAsync<int>("() => window.__sent"));
    }

    [Fact]
    public async Task A_sent_general_inquiry_lands_on_the_message_thank_you_and_is_not_sent_again()
    {
        // The general track had no end-to-end submit test at all before this; it shares the page and
        // so shared the bug.
        await Page.GotoAsync("/boka");
        await Page.Locator(".segmented__btn[for='track-general']").ClickAsync();
        var email = $"meddelande-{Guid.NewGuid():N}@example.se";
        await Page.FillAsync("#g-name", "Anna Andersson");
        await Page.FillAsync("#g-email", email);
        await Page.FillAsync("#g-email-confirm", email);
        await Page.FillAsync("#g-message", "Har ni plats för en konfirmationsgrupp i maj?");
        await Page.Locator(".track--general label:has-text('integritetspolicyn') input[type=checkbox]").CheckAsync();

        var posts = RecordPosts(Page, "/boka");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Skicka meddelande" }).ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/kontakt/tack$"));
        await Assertions.Expect(Page.GetByText("Vi har tagit emot ditt meddelande")).ToBeVisibleAsync();

        await Page.ReloadAsync();
        await Page.GoBackAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/boka$"));

        Assert.Single(posts);
        await using var db = Fixture.NewDbContext();
        Assert.Equal(1, await db.Bookings.CountAsync(b => b.ContactEmail == email));
    }

    [Fact]
    public async Task A_sent_form_restored_from_the_page_cache_is_unlocked()
    {
        // submit-once.js locks a form the moment it is sent. A page the browser later restores from its
        // back/forward cache comes back exactly as it was left — locked — and the guest could not send
        // the form at all. The lock here is the real one, set by a real submit (kept in place, see
        // StaySubmittingInPlaceAsync). The restore is simulated with the event the browser fires on it
        // (pageshow, persisted), because a test cannot make Chromium choose its cache over a reload.
        await Page.GotoAsync("/boka");
        await FillValidStayFormAsync(Page);
        await StaySubmittingInPlaceAsync(Page);

        var submit = Page.GetByRole(AriaRole.Button, new() { Name = "Skicka bokningsförfrågan" });
        await submit.ClickAsync();
        await Assertions.Expect(submit).ToBeDisabledAsync();

        await Page.EvaluateAsync("() => window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }))");

        await Assertions.Expect(submit).ToBeEnabledAsync();
        Assert.Null(await Page.Locator(".track--stay form").GetAttributeAsync("data-submitting"));
    }

    [Fact]
    public async Task A_submit_stopped_by_validation_leaves_the_form_sendable()
    {
        // The validation script cancels an invalid submit; submit-once.js must not count that as sent.
        // If it did, the form would lock on the first mistake and the guest could never send it.
        await Page.GotoAsync("/boka");
        await FillValidStayFormAsync(Page);
        var email = $"rattad-{Guid.NewGuid():N}@example.se";
        await Page.FillAsync("#s-email", email);
        await Page.FillAsync("#s-email-confirm", "fel@example.se");

        var submit = Page.GetByRole(AriaRole.Button, new() { Name = "Skicka bokningsförfrågan" });
        await submit.ClickAsync();
        await Assertions.Expect(Page.GetByText("E-postadresserna stämmer inte överens.")).ToBeVisibleAsync();

        await Page.FillAsync("#s-email-confirm", email);
        await submit.ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/boka/tack$"));
        await using var db = Fixture.NewDbContext();
        Assert.Equal(1, await db.Bookings.CountAsync(b => b.ContactEmail == email));
    }

    [Theory]
    [InlineData("/boka/tack/okand")]
    [InlineData("/kontakt/tack/okand")]
    public async Task Only_the_two_fixed_thank_you_addresses_exist(string path)
    {
        // Which form was sent is the address itself (NFR-41), so there is no default text to fall back
        // on: anything but the two fixed addresses is the ordinary 404.
        var response = await Page.GotoAsync(path);

        Assert.Equal(404, response!.Status);
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sidan hittades inte" })).ToBeVisibleAsync();
    }
}
