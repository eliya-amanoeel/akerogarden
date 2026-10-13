using System.Text.RegularExpressions;
using AkeroGarden.E2E.Infrastructure;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// Swedish-only UI (NFR-32), asserted against what the browser actually renders.
///
/// <para><b>Why rendered text and not a source scan.</b> A scan of <c>.razor</c> cannot separate page
/// copy from C# identifiers — a first attempt flagged 129 "violations" that were all Razor control
/// flow, <c>@foreach (var booking in Bookings)</c> and the like. Rendered text contains no
/// identifiers, so a match here is a real string a guest or an admin can read. The complement is
/// <c>SwedishOnlyTextTests</c> in the unit project, which covers validation messages and display
/// names — user-visible strings that live in C# attributes and would only render after a failed
/// submit.</para>
///
/// <para><b>What this does not cover, stated rather than implied:</b> the outbound email templates
/// (never rendered in a browser), pages needing state these tests do not create, and any string
/// behind an error path that is not exercised here.</para>
/// </summary>
internal static class SwedishOnlyText
{
    /// <summary>
    /// English words that are **not** also Swedish, so a match is unambiguous. Words spelled the same
    /// in both languages are deliberately absent — "information", "total", "problem", "start",
    /// "lunch", "bank", "person", "standard" — because including them would make this test fail on
    /// correct Swedish copy, and excluding the ambiguous cases is what keeps a failure meaningful.
    /// </summary>
    private static readonly string[] EnglishOnlyWords =
    [
        "save", "cancel", "delete", "submit", "search", "loading", "password", "username",
        "settings", "welcome", "please", "required", "back", "next", "close", "edit", "remove",
        "success", "warning", "confirm", "update", "create", "booking", "bookings", "guest",
        "guests", "room", "rooms", "building", "buildings", "meal", "meals", "price", "prices",
        "cleaning", "facility", "facilities", "participant", "participants", "arrival",
        "departure", "breakfast", "dinner", "date", "time", "phone", "address", "home", "send",
        "sign in", "sign out", "log in", "log out",
    ];

    /// <summary>
    /// Any Swedish letter, or a Swedish function word. Used only as a "did we really get the page"
    /// control — not as proof the copy is Swedish, which is what the deny-list below is for.
    /// </summary>
    private static readonly Regex SwedishMarker = new(
        @"[åäöÅÄÖ]|\b(och|eller|som|för|med|att|inte|den|det|här|är|på|till)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EnglishOnly = new(
        @"\b(" + string.Join("|", EnglishOnlyWords.Select(Regex.Escape)) + @")\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static async Task AssertPageIsSwedishAsync(IPage page, string route)
    {
        var text = await page.EvaluateAsync<string>("() => document.body.innerText") ?? string.Empty;

        // Positive control: a blank or half-rendered page trips no English word and would pass
        // silently, making this guard a green tick that proves nothing.
        //
        // Length alone was the first attempt and it was the wrong measure — /login is legitimately
        // 102 characters (logo, two fields, a button, one link) and failed a 120-character floor
        // while being a perfectly good page. Requiring a Swedish marker is both weaker on size and
        // stronger on substance: it holds for the smallest page in the app and still fails on an
        // empty one.
        Assert.True(text.Length >= 60,
            $"{route} rendered only {text.Length} characters — too little to be the real page, so "
            + "the assertion below would pass for the wrong reason");

        Assert.True(SwedishMarker.IsMatch(text),
            $"{route} rendered no Swedish letter or function word at all, so it is probably not the "
            + "page this test means to check");

        var found = EnglishOnly.Matches(text)
            .Select(m => m.Value.ToLowerInvariant())
            .Distinct()
            .ToList();

        Assert.True(found.Count == 0,
            $"Swedish-only UI (NFR-32) — {route} renders English word(s): {string.Join(", ", found)}");
    }
}

public class PublicSwedishOnlyTests(E2EFixture fixture)
    : PublicE2ETestBase(fixture)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/boka")]
    [InlineData("/boka/tack")]
    [InlineData("/kontakt/tack")]
    [InlineData("/bokningsvillkor")]
    [InlineData("/faciliteter")]
    [InlineData("/integritetspolicy")]
    [InlineData("/kalender")]
    [InlineData("/kontakt")]
    [InlineData("/mat-och-priser")]
    [InlineData("/om-oss")]
    [InlineData("/slutfor/tack")]
    public async Task Public_pages_render_swedish_only(string route)
    {
        await Page.GotoAsync(route);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await SwedishOnlyText.AssertPageIsSwedishAsync(Page, route);
    }
}

public class AdminSwedishOnlyTests(E2EFixture fixture)
    : AdminE2ETestBase(fixture)
{
    private async Task LoginAsync()
    {
        await Page.GotoAsync("/login");
        await Page.FillAsync("#email", E2EFixture.AdminEmail);
        await Page.FillAsync("#password", E2EFixture.AdminPassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Logga in" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/$"));
    }

    /// <summary>The login page is the one admin surface reachable without a session.</summary>
    [Fact]
    public async Task Login_page_renders_swedish_only()
    {
        await Page.GotoAsync("/login");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await SwedishOnlyText.AssertPageIsSwedishAsync(Page, "/login");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/bokningar")]
    [InlineData("/bokningar/ny")]
    [InlineData("/kalender")]
    [InlineData("/blockeringar")]
    [InlineData("/byggnader")]
    [InlineData("/faciliteter")]
    [InlineData("/matalternativ")]
    [InlineData("/priser")]
    [InlineData("/stadszoner")]
    [InlineData("/listor")]
    [InlineData("/konton")]
    [InlineData("/jobbhistorik")]
    [InlineData("/handbok")]
    [InlineData("/handbok/samordnare")]
    public async Task Admin_pages_render_swedish_only(string route)
    {
        await LoginAsync();
        await Page.GotoAsync(route);
        // Interactive Server: the first paint after navigation is not the last, and measuring or
        // reading the DOM before the circuit settles reads a half-rendered page.
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await SwedishOnlyText.AssertPageIsSwedishAsync(Page, route);
    }
}
