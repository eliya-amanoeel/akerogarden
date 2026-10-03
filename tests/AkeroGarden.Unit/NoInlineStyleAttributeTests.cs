using System.Text.RegularExpressions;

namespace AkeroGarden.Unit;

/// <summary>
/// Guards the decision that came with the CSP (Phase 11): <c>style-src 'self'</c> admits no inline
/// styling, so a <c>style="…"</c> attribute in markup is silently dropped by the browser.
///
/// <para>This scans the <c>.razor</c> sources rather than rendered pages on purpose. The E2E suite
/// catches a CSP violation only on the pages it visits, and the pages most likely to grow a quick
/// inline style — an admin detail view, an error page — are the ones least likely to be in that
/// list. A source scan covers every page including the ones nothing renders in CI.</para>
///
/// <para>It fails loudly rather than skipping if it cannot find the repository: a guard that quietly
/// passes when it cannot do its job is worse than no guard, because the green tick is read as
/// evidence.</para>
/// </summary>
public class NoInlineStyleAttributeTests
{
    // Matches a literal style attribute in markup. Razor expressions that *compute* a class list are
    // unaffected; what is being forbidden is the attribute itself reaching the browser.
    private static readonly Regex InlineStyleAttribute = new(@"\sstyle\s*=\s*""", RegexOptions.Compiled);

    [Fact]
    public void No_razor_component_carries_an_inline_style_attribute()
    {
        var repositoryRoot = RepositoryRoot.Find();
        var components = Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, "src"), "*.razor", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();

        Assert.True(components.Count > 20,
            $"only found {components.Count} .razor files under {repositoryRoot}/src — the scan is "
            + "probably pointed at the wrong directory, which would make this test meaningless");

        var offenders = new List<string>();
        foreach (var path in components)
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (InlineStyleAttribute.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetRelativePath(repositoryRoot, path)}:{i + 1}");
            }
        }

        Assert.True(offenders.Count == 0,
            "inline style attributes are blocked by the Content-Security-Policy and will not render. "
            + "Add a modifier class in site.css beside the component instead:\n  "
            + string.Join("\n  ", offenders));
    }
}
