using AkeroGarden.E2E.Infrastructure;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// The text-scaling work (PR !51) measured 540 public and 165 admin combinations and found zero
/// overflow — <em>in Chromium</em>. This runs the same class of assertion in the other two engines,
/// which is the hole that mattered most, for two reasons stated in the Phase 11 item:
///
/// <list type="bullet">
///   <item>Chromium's own font-size control stops at 24px (150%), so the <strong>200% that WCAG 2.1
///     SC 1.4.4 names is reachable by a real reader mainly through Firefox's text-only zoom</strong> —
///     the exact mode that work exists to serve, on the one engine never tested.</item>
///   <item>Gecko and WebKit differ from Blink precisely on the two mechanisms the fix leans on
///     hardest: min-content track sizing via <c>minmax(0, 1fr)</c>, and <c>overflow-wrap: anywhere</c>
///     reducing min-content size.</item>
/// </list>
///
/// <para><strong>What is and is not covered, precisely.</strong> Firefox is driven at a raised root
/// size, set through the browser's own default-font-size preference — the real setting, not an
/// injected style. WebKit is driven at the <em>default</em> text size only: Playwright exposes no
/// way to change WebKit's default font size, and the CDP route the Chromium tests use does not
/// exist there. So WebKit here tests engine layout differences at 100%, and says nothing about
/// 200%. That gap is real and is recorded rather than papered over — see the note on
/// <see cref="WebKit_layout_holds_at_the_default_text_size"/>.</para>
/// </summary>
[Collection(E2ECollection.Name)]
public class CrossBrowserTextScalingTests(E2EFixture fixture) : IAsyncLifetime
{
    private IBrowser? _browser;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
    }

    /// <summary>
    /// Launches Firefox with the reader's default font size set to <paramref name="rootPx"/>.
    ///
    /// <para><c>font.size.variable.x-western</c> is the preference behind Firefox's "Default font
    /// size" setting, and it is a <em>launch-time</em> preference — hence a browser per size rather
    /// than one browser reconfigured per test. That is the whole reason this could not simply reuse
    /// the existing suite's browser, and why the Phase 11 item called the harness the blocker.</para>
    /// </summary>
    private async Task<IPage> FirefoxPageAsync(int rootPx, int width, int height)
    {
        _browser = await fixture.Playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            FirefoxUserPrefs = new Dictionary<string, object>
            {
                ["font.size.variable.x-western"] = rootPx,
                ["font.size.fixed.x-western"] = rootPx,
            },
        });

        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = fixture.PublicBaseUrl,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
        });

        return await context.NewPageAsync();
    }

    [Fact]
    public async Task Firefox_actually_applies_the_readers_font_size()
    {
        // This test exists to protect every other test in the file. If the preference silently
        // stopped working, the overflow assertions below would all pass trivially — at a 16px root
        // nothing overflows — and the suite would report cross-browser coverage it did not have.
        // So the harness is asserted before anything is measured with it.
        var page = await FirefoxPageAsync(TextScaling.MaxRootPx, 1280, 900);
        await page.GotoAsync("/");

        var root = await TextScaling.RootFontSizePxAsync(page);
        Assert.Equal(TextScaling.MaxRootPx, root, 0.5);
    }

    [Fact]
    public async Task The_overflow_detector_actually_fires_in_firefox()
    {
        // The companion to the harness test above. That one proves the font preference applies;
        // this proves the *measurement* works in Gecko. Between them, a green run in this file
        // means something — without them, every assertion here could be passing because nothing is
        // being measured at all, in an engine whose only evidence is that it printed no failure.
        var page = await FirefoxPageAsync(TextScaling.MaxRootPx, 360, 780);
        await page.GotoAsync("/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var (clean, _) = await TextScaling.MeasureSidewaysOverflowAsync(page);
        Assert.True(clean <= 1, "precondition: the page is expected to be clean before the probe");

        await page.EvaluateAsync(@"() => {
            const probe = document.createElement('div');
            probe.className = 'overflow-probe';
            probe.style.width = '3000px';
            probe.style.height = '10px';
            document.body.appendChild(probe);
        }");

        var (overflow, detail) = await TextScaling.MeasureSidewaysOverflowAsync(page);
        Assert.True(overflow > 1,
            "the overflow detector reported nothing after a 3000px element was added to a 360px "
            + "viewport — it is not measuring anything in Firefox, and every other assertion in "
            + "this file would be vacuous");
        // The reporter identifies an offender by tag and class (not id), and by measured width —
        // so this asserts what it actually reports, which is also the thing a failing layout
        // test needs it to report.
        Assert.Contains("overflow-probe", detail);
        Assert.Contains("w=3000", detail);
    }

    [Theory]
    // 200% (32px) is the size SC 1.4.4 names and the one Chromium cannot reach; 24px is the
    // Chromium-verified size, repeated here so a Gecko-only regression at 150% is caught too.
    [InlineData("/", 32)]
    [InlineData("/", 24)]
    [InlineData("/boka", 32)]
    [InlineData("/kontakt", 32)]
    [InlineData("/faciliteter", 32)]
    [InlineData("/mat-och-priser", 32)]
    [InlineData("/kalender", 32)]
    [InlineData("/om-oss", 32)]
    [InlineData("/integritetspolicy", 32)]
    [InlineData("/bokningsvillkor", 32)]
    public async Task Firefox_page_does_not_scroll_sideways_at_a_large_text_size(string path, int rootPx)
    {
        // 360px is the narrowest supported width and where every defect in this class has hidden.
        var page = await FirefoxPageAsync(rootPx, 360, 780);
        await page.GotoAsync(path);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Guard against a silently-failing preference on every case, not just the harness test:
        // a page measured at 16px proves nothing about 32px.
        var root = await TextScaling.RootFontSizePxAsync(page);
        Assert.True(Math.Abs(root - rootPx) < 0.5,
            $"Firefox root font size is {root}px, expected {rootPx}px — the preference did not apply, "
            + "so this measurement would have been meaningless");

        var (overflow, detail) = await TextScaling.MeasureSidewaysOverflowAsync(page);
        Assert.True(overflow <= 1, $"{path} scrolls sideways by {overflow}px in Firefox at {rootPx}px root.\n{detail}");
    }

    [Theory]
    [InlineData(768, 1024)]
    [InlineData(1280, 900)]
    public async Task Firefox_wider_viewports_hold_at_200_percent(int width, int height)
    {
        // The em-based breakpoints are the mechanism under test: at a 32px root a "40em" query
        // resolves to 640 CSS px, so a 768px viewport lands in a different column count than it
        // does at 16px. Gecko computes em in media queries against the *initial* font size, which
        // is exactly the behaviour the conversion relies on — worth asserting outside Blink.
        var page = await FirefoxPageAsync(TextScaling.MaxRootPx, width, height);
        await page.GotoAsync("/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var (overflow, detail) = await TextScaling.MeasureSidewaysOverflowAsync(page);
        Assert.True(overflow <= 1, $"home scrolls sideways by {overflow}px in Firefox at {width}px / 200%.\n{detail}");
    }

    [Theory]
    [InlineData("/", 360)]
    [InlineData("/", 1280)]
    [InlineData("/boka", 360)]
    [InlineData("/kontakt", 360)]
    [InlineData("/faciliteter", 360)]
    [InlineData("/kalender", 360)]
    public async Task WebKit_layout_holds_at_the_default_text_size(string path, int width)
    {
        // ⚠️ DEFAULT TEXT SIZE ONLY, and deliberately so: Playwright exposes no way to set WebKit's
        // default font size, and the CDP call the Chromium tests use does not exist there. This
        // therefore covers the *engine* differences that matter — min-content track sizing via
        // minmax(0, 1fr), and overflow-wrap: anywhere reducing min-content, both of which WebKit
        // implements differently from Blink — but it says NOTHING about 200% in WebKit. Do not
        // read a green run here as "WebKit is verified at 200%". If that is ever needed, it needs
        // a real Safari, driven outside Playwright.
        _browser = await fixture.Playwright.Webkit.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = fixture.PublicBaseUrl,
            ViewportSize = new ViewportSize { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce,
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(path);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var (overflow, detail) = await TextScaling.MeasureSidewaysOverflowAsync(page);
        Assert.True(overflow <= 1, $"{path} scrolls sideways by {overflow}px in WebKit at {width}px.\n{detail}");
    }
}
