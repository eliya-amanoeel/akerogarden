namespace AkeroGarden.Unit;

/// <summary>
/// The repository root, for the tests that scan source files (markup, site.css) rather than run
/// code. One copy — it was pasted into three test classes before. Fails loudly if it cannot find
/// the repository: a source guard that quietly passes when pointed at nothing is worse than none.
/// </summary>
internal static class RepositoryRoot
{
    public static string Find()
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

    /// <summary>The shared stylesheet both sites load.</summary>
    public static string SiteCss() =>
        Path.Combine(Find(), "src", "AkeroGarden.Web.Core", "wwwroot", "css", "site.css");
}
