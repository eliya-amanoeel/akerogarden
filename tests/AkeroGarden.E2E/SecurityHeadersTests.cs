using AkeroGarden.E2E.Infrastructure;
using Microsoft.Playwright;

namespace AkeroGarden.E2E;

/// <summary>
/// The Content-Security-Policy as a real browser applies it (Phase 11).
///
/// <para>The integration suite asserts the header's <em>content</em>; this asserts its
/// <em>consequences</em> — that nothing the app actually needs is blocked by it. Those are different
/// claims, and only this one would catch a policy that is well-formed and wrong. Violations are
/// collected from the DOM's own <c>securitypolicyviolation</c> event rather than by scraping console
/// text, so a browser that changes its console wording cannot quietly turn this test green.</para>
/// </summary>
public class PublicSecurityHeadersTests(E2EFixture fixture) : PublicE2ETestBase(fixture)
{
    /// <summary>
    /// Every public route. Kept as an explicit list rather than crawled: a page that stops being
    /// linked would silently drop out of a crawl, and that is exactly when a policy regression on it
    /// would go unnoticed.
    /// </summary>
    public static TheoryData<string> PublicPages => new()
    {
        "/", "/boka", "/kalender", "/kontakt", "/faciliteter",
        "/mat-och-priser", "/om-oss", "/integritetspolicy", "/bokningsvillkor",
        "/boka/tack", "/kontakt/tack", "/slutfor/tack",
    };

    [Theory]
    [MemberData(nameof(PublicPages))]
    public async Task Public_page_renders_with_no_csp_violation(string path)
    {
        await CspViolations.CollectOn(Page);

        await Page.GotoAsync(path);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var violations = await CspViolations.ReadFrom(Page);
        Assert.True(violations.Count == 0,
            $"{path} raised CSP violations:\n  {string.Join("\n  ", violations)}");
    }

    [Fact]
    public async Task Inquiry_form_still_works_under_the_policy()
    {
        // /boka carries a page script (inquiry-form.js) that reveals fields by writing to the
        // element's style object. That is CSSOM, not an inline style attribute, so CSP permits it —
        // but the distinction is subtle enough to be worth proving in a browser rather than
        // asserting from the spec. Driving the field proves the script ran and its write landed.
        await CspViolations.CollectOn(Page);
        await Page.GotoAsync("/boka");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The panel starts hidden only because inquiry-form.js hid it on load (without JS it stays
        // visible by design), so asserting it is hidden first is itself evidence the script ran.
        await Assertions.Expect(Page.Locator("#org-fields")).ToBeHiddenAsync();
        await Page.Locator("#s-isorg").CheckAsync();

        // inquiry-form.js reveals the organisation fields — a behaviour only the script provides.
        await Assertions.Expect(Page.Locator("#org-fields")).ToBeVisibleAsync();

        var violations = await CspViolations.ReadFrom(Page);
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public async Task Completion_page_renders_and_toggles_with_no_csp_violation()
    {
        // /slutfor is not reachable from the page list above (it needs a live token), and it is the
        // page whose markup changed most for this policy: the per-sitting meal cells had a
        // server-rendered display:none that JavaScript cleared. Both halves moved to a class, so
        // both halves are exercised here — the initial hidden state and the script's toggle.
        await CspViolations.CollectOn(Page);
        await Page.GotoAsync($"/slutfor/{E2EFixture.CompletionToken}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var arrivalLunch = Page.Locator("[data-sitting][data-edge='arrival'][data-meal='Lunch']");
        await Assertions.Expect(arrivalLunch).Not.ToBeVisibleAsync();
        await Page.Locator("input[name='Form.ArrivalLunch'][data-edge-meal]").CheckAsync();
        await Assertions.Expect(arrivalLunch).ToBeVisibleAsync();

        var violations = await CspViolations.ReadFrom(Page);
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public async Task Response_carries_the_companion_security_headers()
    {
        var response = await Page.GotoAsync("/");
        Assert.NotNull(response);

        var headers = await response.AllHeadersAsync();
        Assert.Equal("nosniff", headers["x-content-type-options"]);
        Assert.Equal("DENY", headers["x-frame-options"]);
        Assert.Equal("same-origin", headers["referrer-policy"]);
        Assert.Contains("geolocation=()", headers["permissions-policy"]);
    }
}

/// <summary>
/// The admin host under the same policy. Interactive Server is the half that can actually be broken
/// by <c>connect-src</c>: if the circuit's WebSocket is blocked the panel renders and then does
/// nothing, which is why this drives a real interactive action rather than checking that a page
/// loaded.
/// </summary>
public class AdminSecurityHeadersTests(E2EFixture fixture) : AdminE2ETestBase(fixture)
{
    private async Task LoginAsync()
    {
        await Page.GotoAsync("/login");
        await Page.FillAsync("#email", E2EFixture.AdminEmail);
        await Page.FillAsync("#password", E2EFixture.AdminPassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Logga in" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/$"));
    }

    [Fact]
    public async Task Login_page_renders_with_no_csp_violation()
    {
        await CspViolations.CollectOn(Page);
        await Page.GotoAsync("/login");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var violations = await CspViolations.ReadFrom(Page);
        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    [Fact]
    public async Task Interactive_circuit_connects_and_responds_under_the_policy()
    {
        await CspViolations.CollectOn(Page);
        await LoginAsync();

        await Page.GotoAsync("/bokningar/ny");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The claim is "the circuit is live", so it has to be proven by something only a live circuit
        // does: the meal-plan select is server-bound, and the day-guests field is shown or hidden by
        // the server re-rendering in response to it. A page that merely loaded cannot do this.
        await Page.FillAsync("#mb-name", "CSP-testgrupp");
        await Page.SelectOptionAsync("#mb-mealplan", "SelfCateringAll");
        await Assertions.Expect(Page.Locator("#mb-dayguests")).ToBeHiddenAsync();
        await Page.SelectOptionAsync("#mb-mealplan", "FullBoardAll");
        await Assertions.Expect(Page.Locator("#mb-dayguests")).ToBeVisibleAsync();

        var violations = await CspViolations.ReadFrom(Page);
        Assert.True(violations.Count == 0,
            $"the admin circuit raised CSP violations:\n  {string.Join("\n  ", violations)}");
    }

    [Theory]
    [InlineData("/bokningar")]
    [InlineData("/kalender")]
    [InlineData("/konton")]
    [InlineData("/byggnader")]
    public async Task Admin_page_renders_with_no_csp_violation(string path)
    {
        await CspViolations.CollectOn(Page);
        await LoginAsync();

        await Page.GotoAsync(path);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var violations = await CspViolations.ReadFrom(Page);
        Assert.True(violations.Count == 0,
            $"{path} raised CSP violations:\n  {string.Join("\n  ", violations)}");
    }
}

/// <summary>
/// Collects CSP violations from the browser itself. <c>securitypolicyviolation</c> fires on the
/// document for every blocked resource, inline script or inline style, and carries the directive
/// that did the blocking — far more precise than matching console text, and it survives the page
/// navigations these tests perform because the script is re-installed on every document.
/// </summary>
internal static class CspViolations
{
    public static Task CollectOn(IPage page) => page.AddInitScriptAsync(@"
        window.__cspViolations = [];
        document.addEventListener('securitypolicyviolation', e => {
            window.__cspViolations.push(
                e.violatedDirective + ' blocked ' + (e.blockedURI || '(inline)') +
                ' on ' + e.documentURI);
        });");

    public static async Task<IReadOnlyList<string>> ReadFrom(IPage page) =>
        await page.EvaluateAsync<string[]>("window.__cspViolations || []");
}
