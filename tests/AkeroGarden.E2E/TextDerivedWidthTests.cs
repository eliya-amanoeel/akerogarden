using AkeroGarden.E2E.Infrastructure;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// The half of the fluid-typography sweep that the overflow guards cannot see.
///
/// <para><see cref="TextScalingTests"/> and <see cref="CrossBrowserTextScalingTests"/> ask "does the
/// page scroll sideways". That question is blind to the defect this file exists for: a container
/// pinned in <c>px</c> around text that scales. A <c>max-width: 480px</c> box at a 32px root does not
/// overflow anything — it simply fits half as many characters per line. 540 public and 165 admin
/// combinations came back clean while five such values were still in the stylesheet, so "zero
/// overflow" was never evidence that this work was done.</para>
///
/// <para>What is asserted here instead is the actual requirement: <b>a container built around text
/// must follow the reader's text size.</b> Double the root, and the box doubles with it.</para>
///
/// <para><b>Why a 1920 viewport and not the shared <c>Desktop</c> 1280.</b> The admin detail layout
/// collapses to one column at <c>max-width: 53.75em</c>, and <c>em</c> in a media query resolves
/// against the browser's default font size — the very thing these tests change. At a 32px default,
/// 53.75em is <b>1720px</b>, so on a 1280 viewport the two-column layout is already gone and
/// <c>.detail-rail</c> is full width. Measuring there would compare a two-column layout against a
/// collapsed one and call the ratio a failure. That collapse is correct behaviour, and it is also
/// exactly the trap that makes a naive version of this test lie.</para>
/// </summary>
public class TextDerivedWidthTests(E2EFixture fixture) : AdminE2ETestBase(fixture)
{
    /// <summary>Wide enough that 53.75em (1720px at a 32px default) still leaves the desktop layout.</summary>
    private static readonly ViewportSize Wide = new() { Width = 1920, Height = 1080 };

    /// <summary>Ratio tolerance — sub-pixel layout rounding, not a licence for drift.</summary>
    private const double Tolerance = 0.04;

    private async Task LoginAsync()
    {
        await Page.GotoAsync("/login");
        await Page.FillAsync("#email", E2EFixture.AdminEmail);
        await Page.FillAsync("#password", E2EFixture.AdminPassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Logga in" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/$"));
    }

    /// <summary>
    /// Measures through a <b>locator</b>, not <c>EvaluateAsync</c> + <c>querySelector</c>.
    ///
    /// <para>The first version of this used a one-shot <c>querySelector</c> and failed on the admin
    /// host with "missing: .detail-grid .detail-rail" — on the second measurement, after a reload,
    /// with an <c>Expect(...).ToBeVisibleAsync()</c> passing on the line immediately above it. The
    /// admin panel is Interactive Server: the reload serves HTML, the assertion matched that, and
    /// then the circuit reconnected and re-rendered the DOM out from under the snapshot. A locator
    /// re-resolves and auto-waits, so it measures the settled render rather than whichever one
    /// happened to be mounted at that instant.</para>
    /// </summary>
    private static async Task<double> WidthAsync(IPage page, string selector)
    {
        var locator = page.Locator(selector);
        await Assertions.Expect(locator).ToBeVisibleAsync();

        var box = await locator.BoundingBoxAsync();
        Assert.True(box is not null, $"'{selector}' resolved but has no box — it is not laid out.");
        return box!.Width;
    }

    /// <summary>
    /// The login card is the cleanest subject in the app: its parent is the full-width
    /// <c>.login-page</c>, so its own <c>max-width</c> is what decides its width at both text sizes —
    /// nothing else can bind first and mask the result.
    /// </summary>
    [Fact]
    public async Task Login_card_width_follows_the_reader_text_size()
    {
        await Page.SetViewportSizeAsync(Wide.Width, Wide.Height);

        await Page.GotoAsync("/login");
        var atDefault = await WidthAsync(Page, ".login-card");

        await TextScaling.SetBrowserFontSizeAsync(Context, Page, TextScaling.MaxRootPx);
        await Page.GotoAsync("/login");
        // Guard: a silently-failing font preference makes every ratio below trivially 1.0 and the
        // whole file green for the wrong reason.
        Assert.Equal(TextScaling.MaxRootPx, await TextScaling.RootFontSizePxAsync(Page), 0);
        var atLarge = await WidthAsync(Page, ".login-card");

        var ratio = atLarge / atDefault;
        Assert.True(
            Math.Abs(ratio - 2.0) < Tolerance,
            $".login-card was {atDefault}px at a 16px root and {atLarge}px at {TextScaling.MaxRootPx}px "
            + $"(ratio {ratio:0.000}, expected ~2.0). A ratio near 1.0 means max-width went back to px.");
    }

    /// <summary>
    /// <c>.detail-grid</c>'s rail is the only <b>fixed grid track</b> in the admin stylesheet — every
    /// other converted value is a <c>max-width</c>, which merely cramps. A fixed track holds its
    /// width and pushes the layout, and this one carries the action rail: approval buttons, the
    /// completion-link expiry, the on-behalf-of link (FR-112).
    /// </summary>
    [Fact]
    public async Task Booking_detail_rail_follows_the_reader_text_size()
    {
        await Page.SetViewportSizeAsync(Wide.Width, Wide.Height);

        await LoginAsync();
        await Page.GotoAsync("/bokningar");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Familjen Pendlar" }).First.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var atDefault = await WidthAsync(Page, ".detail-grid .detail-rail");

        await TextScaling.SetBrowserFontSizeAsync(Context, Page, TextScaling.MaxRootPx);
        await Page.ReloadAsync();
        // The circuit has to finish reconnecting and re-rendering before anything is measured —
        // this page is Interactive Server, and its first paint after a reload is not its last.
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        Assert.Equal(TextScaling.MaxRootPx, await TextScaling.RootFontSizePxAsync(Page), 0);
        var atLarge = await WidthAsync(Page, ".detail-grid .detail-rail");

        var ratio = atLarge / atDefault;
        Assert.True(
            Math.Abs(ratio - 2.0) < Tolerance,
            $".detail-rail was {atDefault}px at a 16px root and {atLarge}px at {TextScaling.MaxRootPx}px "
            + $"(ratio {ratio:0.000}, expected ~2.0). If this is ~1.0 the track is px again; if it is "
            + "much larger, the two-column layout collapsed and the rail went full width — check the "
            + "53.75em breakpoint against this test's viewport before assuming the CSS regressed.");
    }

    /// <summary>
    /// The frame is NOT a measure, and must not scale. Every reading measure on the public site is
    /// already in <c>ch</c> (<c>.prose</c> 65ch, <c>.section__head</c> 60ch, <c>.hero__sub</c> 44ch,
    /// …), so those line lengths follow the reader's text size on their own. Converting
    /// <c>--max-width</c> to rem would widen the page to 2400px at a 32px root without changing a
    /// single line length — more whitespace and a longer scroll, no better reading.
    ///
    /// <para>This asserts the decision rather than describing it, because <c>--max-width</c> looks
    /// exactly like the thing a future sweep ought to convert.</para>
    /// </summary>
    [Fact]
    public async Task Page_frame_deliberately_does_not_scale()
    {
        await Page.SetViewportSizeAsync(Wide.Width, Wide.Height);

        // /login rather than the dashboard: it needs no session, and --max-width is declared on
        // :root in the shared site.css, so it resolves identically on any page of either host.
        await TextScaling.SetBrowserFontSizeAsync(Context, Page, TextScaling.MaxRootPx);
        await Page.GotoAsync("/login");
        Assert.Equal(TextScaling.MaxRootPx, await TextScaling.RootFontSizePxAsync(Page), 0);

        var frame = await Page.EvaluateAsync<double>(
            "() => parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--max-width'))");

        Assert.Equal(1200, frame, 0);
    }
}
