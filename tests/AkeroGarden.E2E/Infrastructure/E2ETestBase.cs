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
/// Phase 10b runs two host apps; a subclass supplies which one this test drives via
/// <see cref="AppBaseUrl"/> — use <see cref="PublicE2ETestBase"/> or <see cref="AdminE2ETestBase"/>
/// rather than deriving from this directly.
/// </summary>
[Collection(E2ECollection.Name)]
public abstract class E2ETestBase(E2EFixture fixture) : IAsyncLifetime
{
    protected E2EFixture Fixture { get; } = fixture;
    protected IBrowserContext Context { get; private set; } = null!;
    protected IPage Page { get; private set; } = null!;

    /// <summary>The host app this test's browser context points at (public or admin).</summary>
    protected abstract string AppBaseUrl { get; }

    // Common breakpoints (px): mobile, tablet, desktop.
    protected static readonly ViewportSize Mobile = new() { Width = 360, Height = 780 };
    protected static readonly ViewportSize Tablet = new() { Width = 768, Height = 1024 };
    protected static readonly ViewportSize Desktop = new() { Width = 1280, Height = 900 };

    public async Task InitializeAsync()
    {
        Context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = AppBaseUrl,
            ViewportSize = Desktop,
        });
        Page = await Context.NewPageAsync();
    }

    public async Task DisposeAsync() => await Context.DisposeAsync();
}

/// <summary>Base for tests that drive the public (Static SSR) host — home, /boka, /slutfor, calendar.</summary>
public abstract class PublicE2ETestBase(E2EFixture fixture) : E2ETestBase(fixture)
{
    protected override string AppBaseUrl => Fixture.PublicBaseUrl;
}

/// <summary>Base for tests that drive the admin (Interactive Server + Identity) host — /login and the
/// flattened admin routes (dashboard at "/", /bokningar, /konton, …).</summary>
public abstract class AdminE2ETestBase(E2EFixture fixture) : E2ETestBase(fixture)
{
    protected override string AppBaseUrl => Fixture.AdminBaseUrl;
}
