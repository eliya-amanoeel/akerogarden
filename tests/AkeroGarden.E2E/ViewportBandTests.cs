using AkeroGarden.E2E.Infrastructure;

namespace AkeroGarden.E2E;

/// <summary>
/// Closes the one gap the fluid-typography sweep left open: **the 360–768px viewport band had never
/// been measured.** Every earlier pass tested 360px and then jumped to 768px, so the widths where
/// most real phones actually sit — and where two layout breakpoints live — sat between two measured
/// points with nothing checking them.
///
/// <para><b>Why the widest root only, and not the full matrix.</b> 360px and 768px are already
/// covered at five root sizes each (100/125/150/175/200%), and every media query that adds a column
/// is in <c>em</c>, so the untested band lies between two verified points with no <c>px</c>
/// breakpoint between them. What this adds is the widths themselves, at the size where overflow is
/// hardest to avoid. It is a targeted pass, not a full sweep — a mid-root defect that resolves by
/// 200% would still slip through, which is judged unlikely enough to accept and is written down here
/// rather than left implied.</para>
///
/// <para>The widths are chosen, not round: 390 and 414 are the two commonest modern phone widths,
/// 480 and 640 are real breakpoints in <c>site.css</c> (the container's padding step and
/// <c>.mobile-cta</c>), and 540 and 720 sit deliberately between breakpoints, where a layout is
/// mid-collapse and least likely to have been eyeballed.</para>
/// </summary>
public class ViewportBandTests(E2EFixture fixture) : PublicE2ETestBase(fixture)
{
    private static readonly int[] BandWidths = [390, 414, 480, 540, 640, 720];

    private static readonly string[] PublicPages =
    [
        "/", "/om-oss", "/faciliteter", "/mat-och-priser", "/kontakt",
        "/kalender", "/boka", "/integritetspolicy", "/bokningsvillkor",
    ];

    public static TheoryData<string, int> PagesAcrossTheBand()
    {
        var data = new TheoryData<string, int>();
        foreach (var page in PublicPages)
        {
            foreach (var width in BandWidths)
            {
                data.Add(page, width);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PagesAcrossTheBand))]
    public async Task No_sideways_overflow_across_the_360_to_768_band(string route, int width)
    {
        await Page.SetViewportSizeAsync(width, 900);
        await TextScaling.SetBrowserFontSizeAsync(Context, Page, TextScaling.MaxRootPx);
        await Page.GotoAsync(route);

        // Guard: a silently-failing font preference makes every assertion here pass at 16px, which
        // is the one way this whole file could go green while measuring nothing.
        Assert.Equal(TextScaling.MaxRootPx, await TextScaling.RootFontSizePxAsync(Page), 0);

        var (overflow, detail) = await TextScaling.MeasureSidewaysOverflowAsync(Page);

        Assert.True(overflow <= 1,
            $"{route} at {width}px with a {TextScaling.MaxRootPx}px root scrolls sideways by "
            + $"{overflow:0.#}px — {detail}");
    }
}
