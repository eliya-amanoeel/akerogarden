using AkeroGarden.E2E.Infrastructure;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// Shared helpers for driving the browser's own text-size setting.
/// </summary>
internal static class TextScaling
{
    /// <summary>The large end of Chrome's own font-size setting: a 16px default raised to 24px
    /// (150%). This is as far as that control goes, so it is the realistic ceiling in Chrome.</summary>
    public const int LargeRootPx = 24;

    /// <summary>200% — the figure WCAG 2.1 SC 1.4.4 actually names. Not reachable from Chrome's
    /// font-size menu, but it is from Firefox's text-only zoom, so it is a real user state and
    /// not a synthetic one.</summary>
    public const int MaxRootPx = 32;

    /// <summary>
    /// Emulates the reader raising their browser's default font size, via CDP
    /// <c>Page.setFontSizes</c>.
    ///
    /// <para>Deliberately not done by injecting a stylesheet or setting an inline style on
    /// <c>&lt;html&gt;</c>: that has been tried against this app and silently does not take
    /// effect — recorded in <c>design.md</c> under the find-here map, whose guard test had to
    /// fall back to asserting the CSS *unit* rather than the behaviour for exactly this reason.
    /// Emulating the browser setting is also the more honest subject: the setting is the thing
    /// the site used to override.</para>
    /// </summary>
    public static async Task SetBrowserFontSizeAsync(IBrowserContext context, IPage page, int px)
    {
        var cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Page.enable");
        await cdp.SendAsync("Page.setFontSizes", new Dictionary<string, object>
        {
            ["fontSizes"] = new Dictionary<string, object> { ["standard"] = px, ["fixed"] = px },
        });
    }

    public static async Task<double> RootFontSizePxAsync(IPage page) =>
        await page.EvaluateAsync<double>(
            "() => parseFloat(getComputedStyle(document.documentElement).fontSize)");

    /// <summary>
    /// How far the document scrolls sideways, plus a description of what is causing it.
    ///
    /// <para>The width alone says a page is broken but not why, and these failures are almost
    /// always one specific element or one unbreakable Swedish word. Reporting the offenders in
    /// the failure message turns a re-run-and-bisect into a single read — worth the few extra
    /// lines, because the seeded data differs between environments and the offender with it.</para>
    /// </summary>
    public static async Task<(double Overflow, string Detail)> MeasureSidewaysOverflowAsync(IPage page)
    {
        var result = await page.EvaluateAsync<OverflowReport>(@"() => {
            const lim = window.innerWidth;
            const boxes = [];
            document.querySelectorAll('*').forEach(el => {
                const r = el.getBoundingClientRect();
                if (!(r.width || r.height)) return;
                if (getComputedStyle(el).position === 'fixed') return;   // off-canvas panels etc.
                if (r.right > lim + 1)
                    boxes.push(el.tagName.toLowerCase() + '.' + (el.className || '').toString().trim().slice(0, 32) +
                               ' w=' + r.width.toFixed(0) + ' right=' + r.right.toFixed(0));
            });
            // A line box can overflow without any element box doing so — a single long word.
            const texts = [];
            const walk = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
            const range = document.createRange();
            let n;
            while ((n = walk.nextNode())) {
                if (!n.textContent.trim()) continue;
                if (getComputedStyle(n.parentElement).position === 'fixed') continue;
                range.selectNodeContents(n);
                for (const rect of range.getClientRects())
                    if (rect.right > lim + 1) {
                        texts.push(n.parentElement.tagName.toLowerCase() + '.' +
                                   (n.parentElement.className || '').toString().trim().slice(0, 24) +
                                   ' :: ' + n.textContent.trim().slice(0, 40));
                        break;
                    }
            }
            return {
                overflow: document.documentElement.scrollWidth - lim,
                boxes: [...new Set(boxes)].slice(0, 5),
                texts: [...new Set(texts)].slice(0, 5),
            };
        }");

        var detail = result.Boxes.Length == 0 && result.Texts.Length == 0
            ? "(no element or text box exceeds the viewport — look for a transform or a negative margin)"
            : string.Join("; ", result.Boxes.Concat(result.Texts));
        return (result.Overflow, detail);
    }

    private sealed class OverflowReport
    {
        public double Overflow { get; set; }
        public string[] Boxes { get; set; } = [];
        public string[] Texts { get; set; } = [];
    }

    /// <summary>Reads a custom property off &lt;html&gt; as a number of px.</summary>
    public static async Task<double> CustomPropertyPxAsync(IPage page, string property) =>
        await page.EvaluateAsync<double>(
            $"() => parseFloat(getComputedStyle(document.documentElement).getPropertyValue('{property}'))");
}

/// <summary>
/// The reader's browser text-size setting is an input the site must honour, not one it may
/// override — WCAG 2.1 SC 1.4.4 "Resize text" (AA), inside NFR-38's scope.
///
/// <para><c>site.css</c> pinned <c>html { font-size: 16px }</c> until 2026-08-19, which ignored
/// that setting outright. Honouring it then exposed a second defect underneath: the header was
/// pinned to a fixed <c>72px</c> height while its contents are type, so the CTA button spilled
/// out of the bar at a 24px root.</para>
/// </summary>
public class TextScalingTests(E2EFixture fixture) : PublicE2ETestBase(fixture)
{
    [Fact]
    public async Task Root_font_size_follows_the_readers_browser_setting()
    {
        await TextScaling.SetBrowserFontSizeAsync(Context, Page, TextScaling.LargeRootPx);
        await Page.GotoAsync("/");

        // `html { font-size: 100% }` resolves to whatever the reader chose. An absolute value
        // here would report 16 no matter what the setting is — which was the bug.
        Assert.Equal(TextScaling.LargeRootPx, await TextScaling.RootFontSizePxAsync(Page), 0);
    }

    [Theory]
    [InlineData(360, 780)]
    [InlineData(768, 1024)]
    [InlineData(1280, 900)]
    public async Task Header_contents_stay_inside_the_bar_at_a_large_text_size(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await TextScaling.SetBrowserFontSizeAsync(Context, Page, TextScaling.LargeRootPx);
        await Page.GotoAsync("/");

        // Every in-flow child of the bar must be vertically contained by it. Out-of-flow
        // children are excluded on purpose and only when they really are out of flow: the
        // off-screen toggle checkbox, and the dropdown panel, which is absolutely positioned
        // *below* the bar under 1024px and is supposed to be outside it.
        var spilling = await Page.EvaluateAsync<string[]>(@"() => {
            const bar = document.querySelector('.site-header .container');
            const b = bar.getBoundingClientRect();
            const bad = [];
            for (const el of bar.children) {
                const cs = getComputedStyle(el);
                if (cs.display === 'none' || cs.position === 'absolute' || cs.position === 'fixed') continue;
                const r = el.getBoundingClientRect();
                if (r.width === 0 && r.height === 0) continue;
                if (r.top < b.top - 1 || r.bottom > b.bottom + 1) {
                    bad.push(`${el.className || el.tagName} ${Math.round(r.top)}..${Math.round(r.bottom)} ` +
                             `outside bar ${Math.round(b.top)}..${Math.round(b.bottom)}`);
                }
            }
            return bad;
        }");

        Assert.Empty(spilling);

        // …and the bar must not overflow itself horizontally either. Scoped to the header on
        // purpose: page-wide overflow at a raised text size is the Phase 11 fluid-typography
        // sweep's subject, and folding it in here would fail this test for reasons outside it.
        var headerOverflowsSideways = await Page.EvaluateAsync<bool>(@"() => {
            const bar = document.querySelector('.site-header .container');
            return bar.scrollWidth > bar.clientWidth + 1;
        }");
        Assert.False(headerOverflowsSideways, "the header row overflows its own width");
    }

    /// <summary>
    /// No public page may scroll sideways at the largest text size on the narrowest screen.
    ///
    /// <para>This is the regression guard for a whole family of defects found on 2026-08-19,
    /// every one of which was a layout value that could not shrink below its content: `1fr`
    /// tracks in the footer, `.facts` and the trust strip (a track cannot go under its longest
    /// word, and Swedish compounds are long), a flex label on /boka whose text and link were
    /// two unwrappable items, and a &lt;p&gt; on /integritetspolicy holding a word wider than
    /// the column. None of them was reachable while `html` pinned the root to 16px.</para>
    /// </summary>
    [Theory]
    [InlineData("/", 24)]
    [InlineData("/om-oss", 24)]
    [InlineData("/faciliteter", 24)]
    [InlineData("/mat-och-priser", 24)]
    [InlineData("/kontakt", 24)]
    [InlineData("/kalender", 24)]
    [InlineData("/boka", 24)]
    [InlineData("/integritetspolicy", 24)]
    [InlineData("/bokningsvillkor", 24)]
    // 200%, the figure SC 1.4.4 names. Every one of these overflowed here before the layout
    // breakpoints that add a column were moved from `px` to `em`.
    [InlineData("/", 32)]
    [InlineData("/om-oss", 32)]
    [InlineData("/boka", 32)]
    [InlineData("/integritetspolicy", 32)]
    [InlineData("/kalender", 32)]
    // These three carry grids (.fac-grid, .lodging-list, .menu-cols) that render empty against a
    // dev database, so they could only be measured here, where the fixture seeds reference data.
    [InlineData("/faciliteter", 32)]
    [InlineData("/mat-och-priser", 32)]
    [InlineData("/kontakt", 32)]
    [InlineData("/bokningsvillkor", 32)]
    public async Task Page_does_not_scroll_sideways_at_a_large_text_size(string path, int rootPx)
    {
        await Page.SetViewportSizeAsync(Mobile.Width, Mobile.Height);
        await TextScaling.SetBrowserFontSizeAsync(Context, Page, rootPx);
        await Page.GotoAsync(path);

        var (overflow, detail) = await TextScaling.MeasureSidewaysOverflowAsync(Page);

        Assert.True(overflow <= 1, $"{path} overflows its viewport by {overflow:F0}px at a " +
            $"{rootPx}px root on a {Mobile.Width}px screen — {detail}");
    }

    /// <summary>
    /// The inline desktop nav must give way to the hamburger when the reader's text no longer
    /// fits, which is what the `64em` breakpoint expresses. Pinned to `1024px` the nav row ran
    /// ~101px past the bar on every page at a 32px root.
    /// </summary>
    [Fact]
    public async Task Desktop_nav_falls_back_to_the_hamburger_when_text_no_longer_fits()
    {
        await Page.SetViewportSizeAsync(Desktop.Width, Desktop.Height);

        await TextScaling.SetBrowserFontSizeAsync(Context, Page, 16);
        await Page.GotoAsync("/");
        // A default reader on a desktop viewport keeps the inline nav — the breakpoint change
        // must be invisible at 100%.
        await Assertions.Expect(Page.Locator(".site-header__burger")).ToBeHiddenAsync();

        await TextScaling.SetBrowserFontSizeAsync(Context, Page, TextScaling.MaxRootPx);
        await Page.GotoAsync("/");
        await Assertions.Expect(Page.Locator(".site-header__burger")).ToBeVisibleAsync();

        var headerOverflows = await Page.EvaluateAsync<bool>(
            "() => { const b = document.querySelector('.site-header .container');" +
            "        return b.scrollWidth > b.clientWidth + 1; }");
        Assert.False(headerOverflows, "the header row overflows its own width at 200% text");
    }

    /// <summary>
    /// The completion form is the one public page that cannot be reached without a live token, so
    /// it is unreachable from a dev database and can only be measured here.
    /// </summary>
    [Theory]
    [InlineData(24)]
    [InlineData(32)]
    public async Task Completion_form_does_not_scroll_sideways_at_a_large_text_size(int rootPx)
    {
        await Page.SetViewportSizeAsync(Mobile.Width, Mobile.Height);
        await TextScaling.SetBrowserFontSizeAsync(Context, Page, rootPx);
        await Page.GotoAsync($"/slutfor/{E2EFixture.CompletionToken}");

        var (overflow, detail) = await TextScaling.MeasureSidewaysOverflowAsync(Page);

        Assert.True(overflow <= 1, $"/slutfor overflows its viewport by {overflow:F0}px at a " +
            $"{rootPx}px root on a {Mobile.Width}px screen — {detail}");
    }

    [Fact]
    public async Task Mobile_menu_panel_sits_flush_under_the_header_at_a_large_text_size()
    {
        await Page.SetViewportSizeAsync(Mobile.Width, Mobile.Height);
        await TextScaling.SetBrowserFontSizeAsync(Context, Page, TextScaling.LargeRootPx);
        await Page.GotoAsync("/");

        await Page.Locator(".site-header__burger").ClickAsync();
        var panel = Page.Locator(".site-header__nav");
        await Assertions.Expect(panel).ToBeVisibleAsync();

        var header = await Page.Locator(".site-header").BoundingBoxAsync();
        var box = await panel.BoundingBoxAsync();
        Assert.NotNull(header);
        Assert.NotNull(box);

        // `top: 100%` resolves against .site-header's padding box, so the panel starts exactly
        // at the bar's bottom border (hence the ~1px tolerance) whatever height the bar has
        // grown to. This is the one offset with no constant left in it at all.
        var gap = box!.Y - (header!.Y + header.Height);
        Assert.InRange(gap, -1.5, 1.5);
    }

    [Fact]
    public async Task Calendar_selection_bar_clears_the_header_when_stuck()
    {
        await TextScaling.SetBrowserFontSizeAsync(Context, Page, TextScaling.LargeRootPx);
        await Page.GotoAsync("/kalender");

        // sticky-offsets.js must have replaced the rem fallback with the bar's real height.
        var header = await Page.Locator(".site-header").BoundingBoxAsync();
        Assert.NotNull(header);
        Assert.Equal(header!.Height, await TextScaling.CustomPropertyPxAsync(Page, "--header-h"), 0);

        // The bar is `hidden` until a range is chosen, so make a real selection rather than
        // asserting against an element that never renders.
        var days = Page.Locator(".cal-day a[data-date]");
        await days.Nth(0).ClickAsync();
        await days.Nth(2).ClickAsync();
        var selection = Page.Locator("#cal-selection");
        await Assertions.Expect(selection).ToBeVisibleAsync();

        // Scroll until it is genuinely stuck, then check it clears the header rather than
        // sliding under it.
        await Page.Mouse.WheelAsync(0, 1500);
        await Page.WaitForTimeoutAsync(250);

        var headerNow = await Page.Locator(".site-header").BoundingBoxAsync();
        var bar = await selection.BoundingBoxAsync();
        Assert.NotNull(headerNow);
        Assert.NotNull(bar);
        Assert.True(
            bar!.Y >= headerNow!.Y + headerNow.Height - 1.5,
            $"selection bar top {bar.Y} sits under the header bottom {headerNow.Y + headerNow.Height}");
    }
}

/// <summary>
/// The admin half of the same concern. <c>.detail-rail</c> sticks below <c>.admin-topbar</c> —
/// a different element, on a different host, from the public header whose height it used to
/// borrow.
/// </summary>
public class AdminTextScalingTests(E2EFixture fixture) : AdminE2ETestBase(fixture)
{
    private async Task LoginAsync()
    {
        await Page.GotoAsync("/login");
        await Page.FillAsync("#email", E2EFixture.AdminEmail);
        await Page.FillAsync("#password", E2EFixture.AdminPassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Logga in" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/$"));
    }

    /// <summary>
    /// The admin equivalent of the public sideways-scroll guard. /konton is the page that broke:
    /// its &lt;h1&gt; is the single compound "Administratörskonton" (a 360px word at a 24px root)
    /// and its cards carry a `white-space: nowrap` status badge in a row that could not wrap.
    /// </summary>
    [Theory]
    [InlineData("/konton")]
    [InlineData("/bokningar")]
    [InlineData("/byggnader")]
    public async Task Admin_page_does_not_scroll_sideways_at_a_large_text_size(string path)
    {
        await TextScaling.SetBrowserFontSizeAsync(Context, Page, TextScaling.LargeRootPx);
        await LoginAsync();
        await Page.SetViewportSizeAsync(Mobile.Width, Mobile.Height);
        await Page.GotoAsync(path);
        await Assertions.Expect(Page.Locator(".admin-topbar")).ToBeVisibleAsync();

        var (overflow, detail) = await TextScaling.MeasureSidewaysOverflowAsync(Page);

        Assert.True(overflow <= 1, $"{path} overflows its viewport by {overflow:F0}px at a " +
            $"{TextScaling.LargeRootPx}px root on a {Mobile.Width}px screen — {detail}");
    }

    [Fact]
    public async Task Detail_rail_clears_the_admin_topbar_at_a_large_text_size()
    {
        // Set before any navigation so the whole session renders at the raised size.
        await TextScaling.SetBrowserFontSizeAsync(Context, Page, TextScaling.LargeRootPx);
        await LoginAsync();

        // A short viewport on purpose: the rail is clamped to `100vh - topbar - 1rem`, so this
        // exercises that clamp and helps the main column out-grow the rail, which is what gives
        // the rail sticky travel at all (see the guard below).
        //
        // 1440 wide, not 1280: the admin shell collapses its sidebar below `53.75em`, and
        // `.detail-rail` is `position: static` in that collapsed state. `em` tracks the reader's
        // text size, so at a 24px root that breakpoint is 1290px — a 1280px viewport would put
        // this page in the collapsed layout and there would be no sticky rail to assert on.
        await Page.SetViewportSizeAsync(1440, 520);

        await Page.GotoAsync("/bokningar");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Familjen Pendlar" }).First.ClickAsync();

        var rail = Page.Locator(".detail-grid .detail-rail");
        await Assertions.Expect(rail).ToBeVisibleAsync();

        // Wait for the interactive render to SETTLE before reading any layout. This is the admin
        // host — Interactive Server — so the prerendered markup is thrown away and replaced when
        // the circuit connects, and `ToBeVisibleAsync` above can be satisfied by the node that is
        // about to be discarded. Both failure modes seen on 2026-09-07 are that one race:
        //
        //   • CI, reading via `document.querySelector` at eval time: the query landed during the
        //     swap and returned null — "Failed to execute 'getComputedStyle' on 'Window':
        //     parameter 1 is not of type 'Element'".
        //   • Locally, reading via a locator handle: the handle resolved to the pre-swap node,
        //     which was detached by the time the script ran, and a detached node's computed style
        //     is "" for every property — so the assertion saw an empty string, not "sticky".
        //
        // Holding a handle and re-querying are therefore both wrong; the fix is to not read until
        // the DOM has stopped changing. This mirrors the lesson already recorded in
        // AdminTests.cs ("NetworkIdle is not a readiness signal on an Interactive Server page…
        // wait for the placeholder to clear — the actual condition"). Same signature there and
        // here: passes locally, fails on the slower CI agent.
        //
        // The wait condition is deliberately *attachment and styling* — a non-empty computed
        // position — and NOT `position === 'sticky'`. Waiting for the value this test exists to
        // assert would make the assertion vacuous: it would spin until true, then "verify" it.
        await Assertions.Expect(Page.Locator(".admin-empty", new() { HasText = "Laddar…" }))
            .ToHaveCountAsync(0, new() { Timeout = 15000 });
        await Page.WaitForFunctionAsync(
            @"() => {
                const el = document.querySelector('.detail-grid .detail-rail');
                return !!el && el.isConnected && getComputedStyle(el).position !== '';
            }",
            null,
            new() { Timeout = 15000 });

        // Guard the viewport choice above: if the shell has collapsed, the rail is static by
        // design and every assertion below would be testing the wrong layout. Read with a fresh
        // query at eval time (not a stored handle), now that the DOM has settled.
        Assert.Equal("sticky", await Page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('.detail-grid .detail-rail')).position"));

        // The browser setting really did apply to this host, not just to the public one.
        Assert.Equal(TextScaling.LargeRootPx, await TextScaling.RootFontSizePxAsync(Page), 0);

        // The property holds the topbar's measured height — not the public header's, and not
        // the rem fallback.
        var topbar = await Page.Locator(".admin-topbar").BoundingBoxAsync();
        Assert.NotNull(topbar);
        Assert.Equal(topbar!.Height, await TextScaling.CustomPropertyPxAsync(Page, "--admin-topbar-h"), 0);

        // A sticky element can only be observed clearing the bar while its containing block
        // still has somewhere to travel. If .detail-grid is no taller than the rail there is no
        // sticky range at all, the rail simply scrolls away with the page, and any clearance
        // assertion below would pass or fail for reasons that have nothing to do with the
        // offset. Fail loudly rather than verify nothing — this is exactly what made the first
        // version of this test report a defect that was not there.
        var travel = await Page.EvaluateAsync<double>(@"() => {
            const rail = document.querySelector('.detail-grid .detail-rail');
            const grid = rail.closest('.detail-grid');
            return grid.getBoundingClientRect().height - rail.getBoundingClientRect().height;
        }");
        Assert.True(travel > 40,
            $"this booking gives the rail only {travel:F0}px of sticky travel, so the page cannot " +
            "demonstrate sticky behaviour and the assertion below would be vacuous");

        // Scroll to the middle of the sticky range: past the point where sticking begins, but
        // well short of exhausting it.
        await Page.EvaluateAsync(@"() => {
            const rail = document.querySelector('.detail-grid .detail-rail');
            const se = document.scrollingElement;
            const stickyTop = parseFloat(getComputedStyle(rail).top);
            const railDocTop = rail.getBoundingClientRect().top + se.scrollTop;
            const grid = rail.closest('.detail-grid');
            const travel = grid.getBoundingClientRect().height - rail.getBoundingClientRect().height;
            se.scrollTop = railDocTop - stickyTop + travel / 2;
        }");
        await Page.WaitForTimeoutAsync(250);

        var topbarNow = await Page.Locator(".admin-topbar").BoundingBoxAsync();
        var box = await rail.BoundingBoxAsync();
        Assert.NotNull(topbarNow);
        Assert.NotNull(box);

        // It is genuinely stuck (pinned at its sticky offset, not merely scrolled past)…
        var stickyTop = await Page.EvaluateAsync<double>(
            "() => parseFloat(getComputedStyle(document.querySelector('.detail-grid .detail-rail')).top)");
        Assert.InRange(box!.Y, stickyTop - 1.5, stickyTop + 1.5);

        // …and while stuck it sits clear of the topbar rather than under it.
        Assert.True(
            box.Y >= topbarNow!.Y + topbarNow.Height - 1.5,
            $"detail rail top {box.Y} sits under the topbar bottom {topbarNow.Y + topbarNow.Height}");
    }
}
