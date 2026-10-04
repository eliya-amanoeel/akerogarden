using System.Text.RegularExpressions;

namespace AkeroGarden.Unit;

/// <summary>
/// Guards the decision behind <c>StockholmTime</c>: the App Service runs UTC, so every conversion
/// between a stored instant and a Swedish date or clock time goes through that one class.
///
/// <para>Each pattern below is a way the same bug was actually written before 2026-10. Between 00:00
/// and 02:00 Swedish time they all produced yesterday's date — in the cancellation deposit, the
/// approval link decision, the earliest bookable day — and the link expiry went out as 14:00 UTC in
/// every email while the link really lived to 15:00/16:00.</para>
///
/// <para>A source scan, like <see cref="NoInlineStyleAttributeTests"/>, because the defect only shows
/// in the two hours after midnight and no CI run happens to land there. Comment lines are skipped so
/// a comment can say why a pattern is wrong.</para>
///
/// <para><b>What it cannot see:</b> a UTC value formatted after it has been put in a local variable
/// (<c>var e = booking.CompletionTokenExpiry; … {e:HH:mm}</c>). The second test catches the stored
/// fields formatted directly, which is how every display bug here was written; the variable case
/// rests on review.</para>
/// </summary>
public class NoAmbientTimeTests
{
    private static readonly (Regex Pattern, string Why)[] Banned =
    [
        (new Regex(@"\bDateTime\.(Now|Today)\b"), "the server's clock is UTC — use StockholmTime.Today(clock)"),
        (new Regex(@"\bDateTimeOffset\.Now\b"), "the server's clock is UTC — use StockholmTime.Today(clock)"),
        (new Regex(@"\.ToLocalTime\("), "does nothing on a UTC server — use StockholmTime.ToLocal"),
        (new Regex(@"\bDateOnly\.FromDateTime\("), "takes the UTC date — use StockholmTime.DateOf or Today"),
        (new Regex(@"\.ToDateTime\("), "a date + time is Swedish wall-clock time — use StockholmTime.ToUtc"),
        (new Regex(@"\bTimeZoneInfo\."), "time-zone rules live in StockholmTime only"),
    ];

    // Pages take "now" from the injected TimeProvider, so a test can pin it (CLAUDE.md: the clock is
    // an input). Services already do; a stamp like CreatedAt = DateTime.UtcNow in Core is not a
    // comparison and is allowed there.
    private static readonly Regex UtcNowInMarkup = new(@"\bDateTime\.UtcNow\b");

    // A stored UTC field — or any value named …Utc — formatted for display without going through
    // StockholmTime.ToLocal/DateOf.
    private static readonly Regex StoredInstantFormatted = new(
        @"\b(CreatedAt|UpdatedAt|CompletionTokenExpiry|TokenExpiry|\w+SentAt|CancelledAt|Timestamp|StartedAt|FinishedAt|AssignedAt|SetupCompletedAt|\w*[Uu]tc)"
        + @"(\.Value)?\??(\.ToString\(|:[yMdHhms])");

    [Fact]
    public void No_source_file_converts_time_outside_StockholmTime()
    {
        var offenders = new List<string>();
        foreach (var (path, lineNo, line) in SourceLines("*.cs", "*.razor"))
        {
            if (Path.GetFileName(path) == "StockholmTime.cs")
                continue;
            foreach (var (pattern, why) in Banned)
            {
                if (pattern.IsMatch(line))
                    offenders.Add($"{path}:{lineNo} — {why}");
            }
            if (path.EndsWith(".razor", StringComparison.Ordinal) && UtcNowInMarkup.IsMatch(line))
                offenders.Add($"{path}:{lineNo} — inject TimeProvider and use Clock.GetUtcNow()");
        }

        Assert.True(offenders.Count == 0, "time converted outside StockholmTime:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void No_stored_instant_is_formatted_for_display_as_UTC()
    {
        var offenders = SourceLines("*.cs", "*.razor")
            .Where(l => StoredInstantFormatted.IsMatch(l.Line))
            .Select(l => $"{l.Path}:{l.LineNo}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "a stored UTC instant is formatted directly — wrap it in StockholmTime.ToLocal or DateOf:\n  "
            + string.Join("\n  ", offenders));
    }

    private static IEnumerable<(string Path, int LineNo, string Line)> SourceLines(params string[] patterns)
    {
        var root = RepositoryRoot.Find();
        var sep = Path.DirectorySeparatorChar;
        var files = patterns
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(root, "src"), p, SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}")
                        && !f.Contains($"{sep}Migrations{sep}"))
            .ToList();

        Assert.True(files.Count > 100,
            $"only found {files.Count} source files under {root}/src — the scan is probably pointed at "
            + "the wrong directory, which would make this test meaningless");

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("@*", StringComparison.Ordinal)
                    || trimmed.StartsWith('*'))
                    continue;
                yield return (Path.GetRelativePath(root, file), i + 1, lines[i]);
            }
        }
    }
}
