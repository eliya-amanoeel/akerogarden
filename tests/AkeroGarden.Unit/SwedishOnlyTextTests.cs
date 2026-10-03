using System.Text.RegularExpressions;

namespace AkeroGarden.Unit;

/// <summary>
/// Swedish-only UI (NFR-32) is a critical rule with no automated guard until now — every other rule
/// in <c>.claude/CLAUDE.md</c> has a test, this one was held by review alone. Raised by the Phase 11
/// completion gate, 2026-09-09.
///
/// <para><b>Scope: user-visible strings that live in C#, not in markup.</b> Validation messages and
/// display names reach the guest through the browser exactly like page copy does, but they sit in
/// attribute arguments where no markup scan or rendered-page check would look — a validation message
/// only appears once a form is submitted wrongly, which most tests never do.</para>
///
/// <para><b>Markup is deliberately NOT scanned here.</b> A source scan of <c>.razor</c> cannot
/// reliably tell page copy from C# identifiers: a first attempt flagged 129 "violations" that were
/// all leakage from Razor control flow such as <c>@foreach (var booking in Bookings)</c>. Rendered
/// text has no such ambiguity, so markup is covered by <c>SwedishOnlyRenderedTextTests</c> in the E2E
/// project instead. Between them: this file covers strings that are hard to render, that file covers
/// strings that are hard to parse.</para>
/// </summary>
public class SwedishOnlyTextTests
{
    /// <summary>
    /// Attribute arguments whose values are shown to a user. <c>Name=</c> is deliberately broad and
    /// also catches a few strings that are not UI copy — the Data Protection application name, seed
    /// person names — which is harmless: they trip no English marker, and a wider net is the right
    /// error to make for a guard whose failure mode is missing something.
    /// </summary>
    private static readonly Regex UserVisibleAttributeValue = new(
        @"(?:ErrorMessage|Prompt|Description|Name)\s*=\s*""([^""]{3,})""",
        RegexOptions.Compiled);

    /// <summary>
    /// English function words. Chosen because none is a Swedish word, so a match is unambiguous —
    /// unlike content words such as "information", "total", "problem", "start" or "lunch", which are
    /// spelled identically in both languages and would make this test lie in either direction.
    /// </summary>
    private static readonly Regex EnglishMarker = new(
        @"\b(the|is|are|was|were|must|should|and|or|of|to|for|with|this|that|these|those|your|you|"
        + @"please|required|invalid|cannot|can't|don't|does|not allowed|between|at least|at most)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void User_visible_strings_in_csharp_are_not_english()
    {
        var strings = CollectUserVisibleStrings();

        // Positive control: if the scan stops finding anything — an attribute renamed, the glob
        // pointed at the wrong tree — every assertion below passes trivially and the guard becomes a
        // green tick that means nothing.
        Assert.True(strings.Count > 40,
            $"only found {strings.Count} user-visible strings; the scan is probably broken, which "
            + "would make this test pass for the wrong reason");

        var offenders = strings
            .Where(s => EnglishMarker.IsMatch(s.Value))
            .Select(s => $"{s.File}: \"{s.Value}\"")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Swedish-only UI (NFR-32) — these user-visible strings look English:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The Swedish alphabet is the other half of the claim: a string can avoid every English marker
    /// above and still not be Swedish. This does not prove the copy is Swedish — it proves the body
    /// of it is, which is enough to catch a page or a form quietly written in English.
    /// </summary>
    [Fact]
    public void The_body_of_user_visible_text_is_recognisably_swedish()
    {
        var strings = CollectUserVisibleStrings();

        var swedish = strings.Count(s =>
            s.Value.Any(c => c is 'å' or 'ä' or 'ö' or 'Å' or 'Ä' or 'Ö')
            || Regex.IsMatch(s.Value, @"\b(och|eller|som|för|med|att|inte|ange|välj|måste|minst|max(imalt)?|kan|ska)\b",
                RegexOptions.IgnoreCase));

        var share = (double)swedish / strings.Count;
        Assert.True(share > 0.5,
            $"only {swedish} of {strings.Count} user-visible strings ({share:P0}) carry a Swedish "
            + "letter or function word — the UI text may have drifted to English");
    }

    private static List<(string File, string Value)> CollectUserVisibleStrings()
    {
        var root = RepositoryRoot.Find();
        var found = new List<(string, string)>();

        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || path.Contains("Migrations"))
            {
                continue;
            }

            foreach (Match match in UserVisibleAttributeValue.Matches(File.ReadAllText(path)))
            {
                found.Add((Path.GetFileName(path), match.Groups[1].Value));
            }
        }

        return found;
    }
}
