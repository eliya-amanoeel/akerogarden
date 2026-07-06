using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Microsoft.Playwright;
using Testcontainers.PostgreSql;

namespace AkeroGarden.E2E.Infrastructure;

/// <summary>
/// Boots the real application for end-to-end tests:
///   1. starts an ephemeral PostgreSQL container (as the integration suite does),
///   2. launches the Web app out-of-process on a real Kestrel port (so a browser can reach it,
///      and so static assets/CSS resolve against the project content root — the in-memory
///      TestServer used by the integration suite can't be navigated by Playwright),
///   3. installs + launches a headless Chromium.
/// Shared across the whole E2E collection so the app boots once.
/// </summary>
public sealed class E2EFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    private readonly StringBuilder _appLog = new();
    private readonly object _logLock = new();
    private Process? _app;

    public string BaseUrl { get; private set; } = "";
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _db.StartAsync();

        BaseUrl = $"http://127.0.0.1:{GetFreePort()}";
        StartApp(BaseUrl, _db.GetConnectionString());
        await WaitUntilReadyAsync(BaseUrl);

        // Ensure the Chromium build is present (no-op if CI already installed it with OS deps).
        var exit = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exit != 0)
            throw new InvalidOperationException($"Playwright browser install failed (exit {exit}).");

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        Playwright?.Dispose();

        if (_app is { HasExited: false })
        {
            try { _app.Kill(entireProcessTree: true); } catch { /* already gone */ }
            _app.Dispose();
        }

        await _db.DisposeAsync();
    }

    private void StartApp(string baseUrl, string postgresConnectionString)
    {
        var webProject = LocateWebProject();
        var configuration = Environment.GetEnvironmentVariable("E2E_WEB_CONFIG") ?? "Debug";

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(webProject);
        psi.ArgumentList.Add("--configuration");
        psi.ArgumentList.Add(configuration);
        psi.ArgumentList.Add("--no-launch-profile");

        // Development => plain-HTTP cookies + real exception surfaces; only an HTTP URL is bound,
        // so UseHttpsRedirection finds no HTTPS port and serves HTTP through (no redirect loop).
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        psi.Environment["DOTNET_ENVIRONMENT"] = "Development";
        psi.Environment["ASPNETCORE_URLS"] = baseUrl;
        psi.Environment["ConnectionStrings__Postgres"] = postgresConnectionString;

        _app = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _app.OutputDataReceived += (_, e) => Capture(e.Data);
        _app.ErrorDataReceived += (_, e) => Capture(e.Data);
        _app.Start();
        _app.BeginOutputReadLine();
        _app.BeginErrorReadLine();
    }

    private void Capture(string? line)
    {
        if (line is null) return;
        lock (_logLock) _appLog.AppendLine(line);
    }

    private string RecentLog()
    {
        lock (_logLock)
        {
            var lines = _appLog.ToString().Split('\n');
            return string.Join('\n', lines[^Math.Min(40, lines.Length)..]);
        }
    }

    private async Task WaitUntilReadyAsync(string baseUrl)
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            if (_app is { HasExited: true })
                throw new InvalidOperationException($"Web app exited during startup.\n--- app log ---\n{RecentLog()}");

            try
            {
                // Any HTTP response means Kestrel is listening (migrate + seed have completed).
                using var resp = await http.GetAsync(baseUrl + "/");
                return;
            }
            catch
            {
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException($"Web app did not become ready within 120 s.\n--- app log ---\n{RecentLog()}");
    }

    private static string LocateWebProject()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AkeroGarden.slnx")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException("Could not locate the repository root (AkeroGarden.slnx).");

        var csproj = Path.Combine(dir.FullName, "src", "AkeroGarden.Web", "AkeroGarden.Web.csproj");
        if (!File.Exists(csproj))
            throw new InvalidOperationException($"Web project not found at {csproj}.");

        return csproj;
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
