using System.Text.RegularExpressions;

namespace AkeroGarden.Unit;

/// <summary>
/// Selecting an image must never upload it — only saving the form may (raised by the maintainer,
/// 2026-09-09). One field does this for facilities, lodging buildings and rooms alike
/// (<c>Shared/ImageField.razor</c>); these pin both the field and every page that uses it.
///
/// <para>Uploading on selection meant that picking a file and then closing the panel left a blob in
/// storage that nothing referenced, created by an action the admin had explicitly cancelled. It was
/// also inconsistent with removal, which waits for <em>Spara</em>: the same gesture had two different
/// contracts depending on direction.</para>
///
/// <para><strong>Why this is a source scan rather than a behavioural test.</strong> The rule lives in
/// Blazor component flow. The E2E tier runs the hosts out-of-process with no blob storage, so a test
/// there cannot see what reached storage, and the component's state is not reachable from a unit
/// test. What CAN be pinned exactly is the invariant that matters: the upload call appears in the
/// field's <c>CommitAsync</c> and nowhere else, and every page commits on save and resets on close.
/// That is the same approach <see cref="NoInlineStyleAttributeTests"/> takes for a rule the CSP
/// enforces at runtime but which is cheaper to catch in source.</para>
/// </summary>
public class DeferredImageUploadTests
{
    private const string FieldPath = "src/AkeroGarden.Web.Admin/Components/Shared/ImageField.razor";
    private const string AdminPages = "src/AkeroGarden.Web.Admin/Components";

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string Read(string relativePath)
    {
        var path = Path.Combine(Root(), relativePath);
        Assert.True(File.Exists(path), $"Could not find {relativePath}");
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
        var body = MethodBody(Read(FieldPath), "private async Task OnImageSelected(");

        Assert.DoesNotContain("UploadImageAsync", body);
    }

    [Fact]
    public void Committing_is_what_uploads()
    {
        // The other half: if the call disappeared from CommitAsync entirely, the first test would
        // still pass while images silently stopped being stored at all.
        var body = MethodBody(Read(FieldPath), "public async Task<bool> CommitAsync(");

        Assert.Contains("UploadImageAsync", body);
    }

    [Fact]
    public void The_upload_call_appears_exactly_once_in_all_of_admin()
    {
        // Guards against a second upload path being added later — in the field or on a page — that
        // skips the deferral.
        var occurrences = Directory
            .EnumerateFiles(Path.Combine(Root(), AdminPages), "*.razor", SearchOption.AllDirectories)
            .Sum(path => Regex.Matches(File.ReadAllText(path), @"UploadImageAsync\s*\(").Count);

        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void Resetting_the_field_discards_the_pending_selection()
    {
        // Without this, cancelling would leave the chosen file staged and the NEXT save would upload
        // an image the admin thought they had abandoned.
        var body = MethodBody(Read(FieldPath), "public void Reset(");

        Assert.Contains("_pending = null", body);
    }

    [Theory]
    [InlineData("Pages/Facilities.razor", "private async Task Save(", "_image")]
    [InlineData("Pages/Buildings.razor", "private async Task SaveBuilding(", "_buildingImage")]
    [InlineData("Pages/Buildings.razor", "private async Task SaveRoom(", "_roomImage")]
    public void Each_save_commits_its_image_before_saving(string page, string save, string field)
    {
        var body = MethodBody(Read($"{AdminPages}/{page}"), save);

        Assert.Contains($"await {field}!.CommitAsync()", body);
    }

    [Theory]
    [InlineData("Pages/Facilities.razor", "_image")]
    [InlineData("Pages/Buildings.razor", "_buildingImage")]
    [InlineData("Pages/Buildings.razor", "_roomImage")]
    public void Closing_the_panel_resets_the_field(string page, string field)
    {
        var body = MethodBody(Read($"{AdminPages}/{page}"), "private void ClosePanel(");

        Assert.Contains($"{field}?.Reset()", body);
    }
}
