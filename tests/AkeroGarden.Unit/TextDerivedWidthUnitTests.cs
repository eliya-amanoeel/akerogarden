using System.Text.RegularExpressions;

namespace AkeroGarden.Unit;

/// <summary>
/// Pins the outcome of the 2026-09-09 <c>px</c> sweep over <c>site.css</c>: which widths were
/// classified <b>text-derived</b> (must be <c>rem</c> — they hold text and must follow the reader's
/// text size) and which were classified <b>physical</b> (must stay <c>px</c>).
///
/// <para><b>Why a source scan and not only the browser test.</b> Three of the five converted values
/// cannot be honestly asserted in a browser. <c>.kv</c>'s <c>max-width</c> is bound by its parent
/// column before its own value applies at large text; <c>.admin-editcard</c> sits inside the same
/// constrained column; and <c>.admin-panel</c> only exists while a slide-over is open. Asserting a
/// width ratio on those would be asserting the parent, not the rule — so they are pinned here as
/// units instead, and <c>TextDerivedWidthTests</c> in the E2E project covers the two whose behaviour
/// is genuinely observable. That split is deliberate and is the honest coverage boundary:
/// this file proves the declaration, that file proves the effect.</para>
///
/// <para>The <c>px</c> assertions matter as much as the <c>rem</c> ones. <c>--max-width</c> and
/// <c>.admin-body</c> look exactly like values a future sweep should convert, and converting them
/// would be wrong — every reading measure on this site is already in <c>ch</c> and already scales,
/// so widening the frame adds whitespace without shortening a single line.</para>
/// </summary>
public class TextDerivedWidthUnitTests
{
    /// <summary>Declarations that hold text: converted to rem on 2026-09-09.</summary>
    public static TheoryData<string, string> TextDerived() => new()
    {
        // selector fragment to find,                    the value that must now be rem
        { ".detail-grid {",     "grid-template-columns: 1fr 20rem" },
        { ".kv {",              "max-width: 30rem" },
        { ".admin-panel {",     "width: min(23.75rem, 92vw)" },
        { ".admin-editcard {",  "max-width: 40rem" },
        { ".login-card {",      "max-width: 25rem" },
    };

    /// <summary>Declarations that are page frames, not measures: deliberately still px.</summary>
    public static TheoryData<string, string> DeliberatelyPhysical() => new()
    {
        { "--max-width:",   "1200px" },
        { ".admin-body {",  "max-width: 1160px" },
    };

    [Theory]
    [MemberData(nameof(TextDerived))]
    public void Text_derived_widths_are_expressed_in_rem(string anchor, string expected)
    {
        var rule = RuleContaining(anchor);

        // The exact declaration, not "this rule contains no px". Several of these rules carry
        // legitimately physical px alongside the width — `.admin-panel` has a 1px border and a
        // 12px shadow offset, `.login-card` a 1px border — and a blanket scan would fail on values
        // the sweep deliberately kept.
        Assert.Contains(expected, rule);
    }

    [Theory]
    [MemberData(nameof(DeliberatelyPhysical))]
    public void Page_frames_stay_in_px_and_say_why(string anchor, string expected)
    {
        var rule = RuleContaining(anchor);

        Assert.Contains(expected, rule);
    }

    /// <summary>
    /// The reading measures the frame decision depends on. If these ever stop being <c>ch</c>, the
    /// argument for keeping <c>--max-width</c> in <c>px</c> collapses with them — the frame would
    /// then be the only thing governing line length, and it would need to scale after all.
    /// </summary>
    [Fact]
    public void Reading_measures_are_expressed_in_ch()
    {
        var css = StripComments(ReadStylesheet());

        foreach (var (selector, measure) in new[]
                 {
                     (".prose {", "65ch"),
                     (".section__head {", "60ch"),
                     (".hero__sub {", "44ch"),
                     (".page-head__intro {", "56ch"),
                 })
        {
            var rule = RuleContaining(selector, css);
            Assert.Contains(measure, rule);
        }
    }

    private static string ReadStylesheet()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "src", "AkeroGarden.Web.Core", "wwwroot", "css", "site.css");
        Assert.True(File.Exists(path), $"could not find site.css at {path}");
        return File.ReadAllText(path);
    }

    private static string StripComments(string css) =>
        Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

    /// <summary>
    /// The declaration block following <paramref name="anchor"/>, comments removed. Anchored on the
    /// opening brace so a selector cannot match its own mention inside a neighbouring comment.
    /// </summary>
    private static string RuleContaining(string anchor, string? source = null)
    {
        var css = StripComments(source ?? ReadStylesheet());

        var start = css.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(start >= 0,
            $"'{anchor}' is not in site.css — it was renamed or deleted. This guard records a "
            + "deliberate decision, so update the decision here rather than dropping the case.");

        var end = css.IndexOf('}', start);
        Assert.True(end > start, $"unterminated rule for '{anchor}'");
        return css[start..end];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src"))
                && Directory.Exists(Path.Combine(directory.FullName, "tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"could not find the repository root above {AppContext.BaseDirectory}");
    }
}
