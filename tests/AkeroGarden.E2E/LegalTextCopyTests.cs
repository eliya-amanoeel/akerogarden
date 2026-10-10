using System.Text.RegularExpressions;
using AkeroGarden.E2E.Infrastructure;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// The two legal texts exist twice: as the published page (<c>PrivacyPolicy.razor</c>,
/// <c>BookingTerms.razor</c>) and as a copy in <c>docs/legal/</c>, which is what anyone reviewing
/// the terms or the policy reads. Nothing kept the two together but discipline, and that failed:
/// by 2026-10-10 the privacy policy file differed from the published page in some thirty places,
/// while the page's own comment said it was generated from the file. This test makes a divergence a
/// red build instead of a silent one.
///
/// <para>It compares <em>words</em>, not formatting: the page's rendered text against the Markdown
/// with its syntax removed. Line wrapping, bold and table layout may differ freely; a changed,
/// added or missing word may not. The file's repo-only status header (the quoted block straight
/// under the title) is not part of the published text and is left out — and must be present.</para>
/// </summary>
public class LegalTextCopyTests(E2EFixture fixture) : PublicE2ETestBase(fixture)
{
    [Theory]
    [InlineData("/integritetspolicy", "Integritetspolicy.md")]
    [InlineData("/bokningsvillkor", "Bokningsvillkor.md")]
    public async Task The_published_text_is_word_for_word_the_copy_in_docs_legal(string path, string file)
    {
        await Page.GotoAsync(path);

        // Every text node of <main>, joined with spaces, so adjacent elements never fuse into one
        // word. The eyebrow above the title ("Om sidan") is page chrome, not part of the text.
        var rendered = await Page.EvaluateAsync<string>(@"() => {
            const main = document.querySelector('main').cloneNode(true);
            main.querySelectorAll('.eyebrow').forEach(e => e.remove());
            const walker = document.createTreeWalker(main, NodeFilter.SHOW_TEXT);
            const parts = [];
            while (walker.nextNode()) parts.push(walker.currentNode.data);
            return parts.join(' ');
        }");

        var markdown = File.ReadAllText(Path.Combine(E2EFixture.RepositoryRoot(), "docs", "legal", file));

        var published = Words(rendered);
        var documented = Words(PlainText(markdown, file));

        var at = FirstDifference(published, documented);
        Assert.True(at < 0,
            $"docs/legal/{file} and {path} differ — change both together (the file is an exact copy of the page).\n"
            + $"  page: …{Excerpt(published, at)}\n"
            + $"  file: …{Excerpt(documented, at)}");
    }

    /// <summary>The Markdown with its syntax removed, and without the repo-only status header.</summary>
    private static string PlainText(string markdown, string file)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n').ToList();
        Assert.True(lines.Count > 2 && lines[0].StartsWith("# ", StringComparison.Ordinal)
                && lines[2].StartsWith("> **", StringComparison.Ordinal),
            $"{file} must open with its title and then its status header (a quoted block).");
        lines.RemoveAt(2);

        var text = string.Join('\n', lines);
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1");                 // links → their text
        text = Regex.Replace(text, @"^\s*\|?[-|\s]+\|\s*$", "", RegexOptions.Multiline); // table rules
        text = Regex.Replace(text, @"^(#+ |> |- )", "", RegexOptions.Multiline);     // heading, quote, list
        text = text.Replace("**", "").Replace("\\|", "\u0001").Replace("|", " ").Replace("\u0001", "|");
        return Regex.Replace(text, @"(?<!\w)\*|\*(?!\w)", " ");                      // *emphasis*
    }

    /// <summary>Whitespace-separated words, with punctuation re-attached where an element boundary
    /// (e.g. <c>&lt;/b&gt;,</c>) put a space before it.</summary>
    private static string[] Words(string text) =>
        Regex.Replace(text, @"\s+([,.;:])", "$1")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static int FirstDifference(string[] a, string[] b)
    {
        var shared = Math.Min(a.Length, b.Length);
        for (var i = 0; i < shared; i++)
        {
            if (a[i] != b[i]) return i;
        }

        return a.Length == b.Length ? -1 : shared;
    }

    private static string Excerpt(string[] words, int at) =>
        string.Join(' ', words.Skip(Math.Max(0, at - 6)).Take(14));
}
