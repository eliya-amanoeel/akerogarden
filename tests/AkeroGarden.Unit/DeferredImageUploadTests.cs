using System.Text.RegularExpressions;

namespace AkeroGarden.Unit;

/// <summary>
/// Selecting a facility image must never upload it — only saving the form may (raised by the
/// maintainer, 2026-09-09).
///
/// <para>Uploading on selection meant that picking a file and then closing the panel left a blob in
/// storage that nothing referenced, created by an action the admin had explicitly cancelled. It was
/// also inconsistent with removal, which waits for <em>Spara</em>: the same gesture had two different
/// contracts depending on direction.</para>
///
/// <para><strong>Why this is a source scan rather than a behavioural test.</strong> The rule lives in
/// Blazor component flow. The E2E tier runs the hosts out-of-process, so a test there cannot see what
/// reached storage, and the component's state is not reachable from a unit test. What CAN be pinned
/// exactly is the invariant that matters: the upload call appears in <c>Save</c> and nowhere else.
/// That is the same approach <see cref="NoInlineStyleAttributeTests"/> takes for a rule the CSP
/// enforces at runtime but which is cheaper to catch in source.</para>
/// </summary>
public class DeferredImageUploadTests
{
    private const string RelativePath =
        "src/AkeroGarden.Web.Admin/Components/Pages/Facilities.razor";

    private static string ReadComponent()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, RelativePath);
        Assert.True(File.Exists(path), $"Could not find {RelativePath}");
        return File.ReadAllText(path);
    }

    /// <summary>Extracts one C# method body from the component's @code block, braces balanced.</summary>
    private static string MethodBody(string source, string signatureFragment)
    {
        var start = source.IndexOf(signatureFragment, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method '{signatureFragment}' not found — has it been renamed?");

        var open = source.IndexOf('{', start);
        Assert.True(open > 0);

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) return source[open..(i + 1)];
            }
        }

        throw new Xunit.Sdk.XunitException($"Unbalanced braces reading '{signatureFragment}'.");
    }

    [Fact]
    public void Selecting_a_file_does_not_upload_it()
    {
        var body = MethodBody(ReadComponent(), "private async Task OnImageSelected(");

        Assert.DoesNotContain("UploadFacilityImageAsync", body);
    }

    [Fact]
    public void Saving_the_form_is_what_uploads()
    {
        // The other half: if the call disappeared from Save entirely, the first test would still pass
        // while images silently stopped being stored at all.
        var body = MethodBody(ReadComponent(), "private async Task Save(");

        Assert.Contains("UploadFacilityImageAsync", body);
    }

    [Fact]
    public void The_upload_call_appears_exactly_once_in_the_component()
    {
        // Guards against a second upload path being added later that skips the deferral.
        var occurrences = Regex.Matches(ReadComponent(), @"UploadFacilityImageAsync\s*\(").Count;

        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void Closing_the_panel_discards_the_pending_selection()
    {
        // Without this, cancelling would leave the chosen file staged and the NEXT save would upload
        // an image the admin thought they had abandoned.
        var body = MethodBody(ReadComponent(), "private void ClosePanel(");

        Assert.Contains("_pendingImage = null", body);
    }
}
