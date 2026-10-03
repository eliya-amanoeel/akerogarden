using System.Text.RegularExpressions;

namespace AkeroGarden.Unit;

/// <summary>
/// Guards two typography rules of <c>docs/ux/design.md</c> in <c>site.css</c> itself, because both
/// had drifted unnoticed for months (found 2026-10-03):
/// <list type="bullet">
///   <item>Only weights 400 and 700 exist for both fonts — eight rules asked for 500/600, which the
///   browser silently rounded to a face that does exist.</item>
///   <item>The heading font is for headings. It had reached data — building names in the Byggnader
///   tree, the guest's booking summary — where it reads as a heading and its old-style figures make
///   dates uneven.</item>
/// </list>
/// </summary>
public class CssDesignRulesTests
{
    private static readonly Regex Rule = new(@"([^{}]+)\{([^{}]*)\}", RegexOptions.Compiled);

    private static IEnumerable<(string Selector, string Body)> Rules()
    {
        var css = Regex.Replace(File.ReadAllText(RepositoryRoot.SiteCss()), @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Rule.Matches(css).Select(m => (m.Groups[1].Value.Trim(), m.Groups[2].Value));
    }

    [Fact]
    public void Only_the_two_loaded_weights_are_used()
    {
        var offenders = Rules()
            .SelectMany(r => Regex.Matches(r.Body, @"font-weight\s*:\s*([^;]+)")
                .Select(w => (r.Selector, Weight: w.Groups[1].Value.Trim())))
            .Where(x => x.Weight is not ("400" or "700" or "normal" or "bold" or "inherit"))
            .Select(x => $"{x.Selector} → {x.Weight}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "only 400 and 700 are loaded for Roboto and Playfair (design.md, Fonts) — any other weight "
            + "is silently rounded by the browser:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Every rule that sets the heading font, by selector. Adding one is a design decision, so it is
    /// made here deliberately: the list is the record of where Playfair is allowed. The decorative
    /// numerals (trust strip, facts, steps, 404 code) are display type, not data, and kept on purpose.
    /// </summary>
    private static readonly string[] HeadingFontAllowed =
    [
        "h1, h2, h3, h4, h5, h6",
        ".trust-strip__num",
        ".step__n",
        ".site-footer__brand",
        ".form-section > h2",
        ".pcard__title",
        ".group-default h3",
        ".modal__title",
        ".prose h3",
        ".subhead",
        ".facts__num",
        ".lodging-item h4",
        ".closing-line p",
        ".dish h3",
        ".findmap__place",
        ".utility__code",
    ];

    [Fact]
    public void The_heading_font_is_set_only_where_it_was_decided()
    {
        var setting = Rules()
            .Where(r => Regex.IsMatch(r.Body, @"font-family\s*:\s*var\(--font-heading\)"))
            .Select(r => r.Selector)
            .ToList();

        var unexpected = setting.Except(HeadingFontAllowed).ToList();

        Assert.True(unexpected.Count == 0,
            "the heading font is for headings (design.md, Typography). If this really is a heading or "
            + "display type, add it to HeadingFontAllowed with the reason:\n  " + string.Join("\n  ", unexpected));
    }

    [Fact]
    public void All_styling_lives_in_site_css_with_no_component_stylesheets()
    {
        // One stylesheet for both sites (design.md). The last .razor.css — the Blazor template's
        // reconnect dialog, still in template blue — sat outside these guards until 2026-10-03, and
        // its bundle cost the admin a second stylesheet request on every page.
        var root = RepositoryRoot.Find();
        var scoped = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.razor.css", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        Assert.True(scoped.Count == 0,
            "component stylesheets bypass site.css and its tokens — move the rules there:\n  "
            + string.Join("\n  ", scoped));
    }
}
