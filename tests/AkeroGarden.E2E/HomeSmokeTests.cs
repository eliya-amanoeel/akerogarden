using AkeroGarden.E2E.Infrastructure;

namespace AkeroGarden.E2E;

/// <summary>
/// Slice-2 plumbing proof: confirms the E2E harness can boot the real app and drive it in a
/// browser. Richer Home assertions (CTA journey, axe a11y, breakpoint reachability) are added
/// alongside the Home redesign.
/// </summary>
public class HomeSmokeTests(E2EFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task Home_page_loads_and_is_branded_Swedish()
    {
        await Page.GotoAsync("/");

        var title = await Page.TitleAsync();
        Assert.Contains("Åkerögården", title);
    }
}
