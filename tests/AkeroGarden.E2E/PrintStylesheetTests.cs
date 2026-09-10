using AkeroGarden.E2E.Infrastructure;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// The print stylesheet (Phase 11). Until 2026-09-08 the site shipped none, so printing any page
/// carried the header, the footer, the docked mobile CTA and the "Vill du boka?" card.
///
/// <para>Every assertion here is made under <c>Media.Print</c> — Playwright's media emulation, which
/// is what the browser itself applies when printing — and each is paired with the same assertion
/// under <c>Media.Screen</c>. The pairing is the point: a test that only checked "the header is
/// hidden when printing" would pass just as happily if the header were hidden always, which would be
/// a far worse defect than the one being fixed.</para>
/// </summary>
public class PrintStylesheetTests(E2EFixture fixture) : PublicE2ETestBase(fixture)
{
    [Fact]
    public async Task Site_chrome_is_hidden_when_printing_and_visible_on_screen()
    {
        await Page.GotoAsync("/kontakt");

        // Screen: the control. If these ever fail, the print rules have leaked out of @media print.
        await Page.EmulateMediaAsync(new() { Media = Media.Screen });
        await Assertions.Expect(Page.Locator(".site-header")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".site-footer")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".cta-card")).ToBeVisibleAsync();

        // Print: the chrome a reader cannot act on once the page is paper.
        await Page.EmulateMediaAsync(new() { Media = Media.Print });
        await Assertions.Expect(Page.Locator(".site-header")).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator(".site-footer")).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator(".cta-card")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task The_page_content_itself_still_prints()
    {
        // The failure this guards against is over-hiding: a print stylesheet that removes the
        // chrome and takes the page with it. /kontakt is the case that matters most, because the
        // reason to print it at all is the directions and the drawn map.
        await Page.GotoAsync("/kontakt");
        await Page.EmulateMediaAsync(new() { Media = Media.Print });

        await Assertions.Expect(Page.Locator("main")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".findmap-figure")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".contact-dl").First).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Printing_puts_black_on_white()
    {
        // The warm off-white ground costs toner across a whole sheet and loses contrast in
        // greyscale. Asserted as the composited value the browser would actually paint, rather
        // than by reading the token, since the token is only meaningful once it cascades.
        await Page.GotoAsync("/integritetspolicy");

        // ToHaveCSSAsync rather than a one-shot EvaluateAsync of getComputedStyle: switching the
        // emulated media triggers a style recalc that is not complete when the emulation call
        // returns, so a single immediate read observes the *previous* media's value. The polling
        // assertion waits for the recalc. (This is why the other tests here never saw the problem —
        // Playwright's web-first assertions poll; a raw EvaluateAsync does not.)
        await Page.EmulateMediaAsync(new() { Media = Media.Screen });
        await Assertions.Expect(Page.Locator("body"))
            .ToHaveCSSAsync("background-color", "rgb(250, 248, 244)");

        await Page.EmulateMediaAsync(new() { Media = Media.Print });
        await Assertions.Expect(Page.Locator("body"))
            .ToHaveCSSAsync("background-color", "rgb(255, 255, 255)");
        await Assertions.Expect(Page.Locator("body")).ToHaveCSSAsync("color", "rgb(0, 0, 0)");
    }

    [Fact]
    public async Task The_docked_mobile_cta_does_not_print()
    {
        // The docked CTA only exists below 40em — 640px at the default root this browser runs at —
        // so this is the one rule that cannot be checked at the desktop viewport the other tests
        // use: it would read as "hidden" there for the wrong reason, and the test would prove
        // nothing.
        await Page.SetViewportSizeAsync(Mobile.Width, Mobile.Height);
        await Page.GotoAsync("/");

        await Page.EmulateMediaAsync(new() { Media = Media.Screen });
        await Assertions.Expect(Page.Locator(".mobile-cta")).ToBeVisibleAsync();

        await Page.EmulateMediaAsync(new() { Media = Media.Print });
        await Assertions.Expect(Page.Locator(".mobile-cta")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/kontakt")]
    [InlineData("/integritetspolicy")]
    [InlineData("/bokningsvillkor")]
    [InlineData("/om-oss")]
    [InlineData("/mat-och-priser")]
    public async Task No_public_page_prints_its_navigation(string path)
    {
        await Page.GotoAsync(path);
        await Page.EmulateMediaAsync(new() { Media = Media.Print });

        await Assertions.Expect(Page.Locator(".site-header")).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator(".site-footer")).ToBeHiddenAsync();
        // …and something survived to be printed.
        await Assertions.Expect(Page.Locator("main")).ToBeVisibleAsync();
    }
}
