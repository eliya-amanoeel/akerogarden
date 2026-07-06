using Microsoft.Playwright;

namespace AkeroGarden.E2E.Infrastructure;

/// <summary>xUnit collection so the app + browser boot once for every E2E test.</summary>
[CollectionDefinition(Name)]
public sealed class E2ECollection : ICollectionFixture<E2EFixture>
{
    public const string Name = "e2e";
}

/// <summary>
/// Base for E2E tests: a fresh, isolated browser context + page per test, pointed at the
/// running app via <see cref="IBrowserContext"/> BaseURL (so tests navigate with relative paths).
/// </summary>
[Collection(E2ECollection.Name)]
public abstract class E2ETestBase(E2EFixture fixture) : IAsyncLifetime
{
    protected E2EFixture Fixture { get; } = fixture;
    protected IBrowserContext Context { get; private set; } = null!;
    protected IPage Page { get; private set; } = null!;

    // Common breakpoints (px): mobile, tablet, desktop.
    protected static readonly ViewportSize Mobile = new() { Width = 360, Height = 780 };
    protected static readonly ViewportSize Tablet = new() { Width = 768, Height = 1024 };
    protected static readonly ViewportSize Desktop = new() { Width = 1280, Height = 900 };

    public async Task InitializeAsync()
    {
        Context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Fixture.BaseUrl,
            ViewportSize = Desktop,
        });
        Page = await Context.NewPageAsync();
    }

    public async Task DisposeAsync() => await Context.DisposeAsync();
}
