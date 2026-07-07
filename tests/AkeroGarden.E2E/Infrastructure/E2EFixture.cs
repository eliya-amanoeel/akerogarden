using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using AkeroGarden.Web.Data;
using AkeroGarden.Web.Data.Entities;
using AkeroGarden.Web.Models;
using Microsoft.EntityFrameworkCore;
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
        await SeedReferenceDataAsync();

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

    /// <summary>Known, unexpired completion tokens seeded for the /slutfor E2E tests. The submit
    /// test uses its own booking because submitting consumes it (Approved → DetailsComplete),
    /// which would otherwise break the read-only tests sharing a token.</summary>
    public const string CompletionToken = "e2e-completion-token-abc123";
    public const string CompletionSubmitToken = "e2e-completion-submit-token-xyz789";

    /// <summary>Credentials for the admin user AdminSeeder creates on boot (Seed:AdminEmail/Password
    /// passed via env below). The admin E2E tests log in with these before hitting /admin.</summary>
    public const string AdminEmail = "e2e-admin@akerogarden.test";
    public const string AdminPassword = "E2e-Admin-Pass1!";

    // The app boots with an empty DB (only an admin user is seeded). Seed the minimum the public
    // journeys need: bedroom capacity (else every calendar day reads fully booked), a couple of
    // meal options, and one Approved booking with a valid completion token for /slutfor.
    private async Task SeedReferenceDataAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_db.GetConnectionString())
            .Options;

        await using var ctx = new ApplicationDbContext(options);
        if (await ctx.Buildings.AnyAsync()) return;

        var building = new Building { Id = Guid.NewGuid(), Name = "Huvudbyggnaden", IsActive = true };
        building.Rooms.Add(new Room { Id = Guid.NewGuid(), BuildingId = building.Id, Name = "Sovsal", BedCount = 40, IsActive = true });
        ctx.Buildings.Add(building);

        // Two lunch options (a real choice → per-day selects + a group-default lunch select) and a
        // single dinner option (no choice → rendered as fixed text + a hidden field). Lets the E2E
        // exercise both the group-default cascade and the single-option path honestly.
        ctx.MealOptions.AddRange(
            new MealOption { Id = Guid.NewGuid(), Name = "Rotfruktssoppa", MealType = MealType.Lunch, IsActive = true },
            new MealOption { Id = Guid.NewGuid(), Name = "Ugnsbakad lax", MealType = MealType.Lunch, IsActive = true },
            new MealOption { Id = Guid.NewGuid(), Name = "Kycklinggryta", MealType = MealType.Dinner, IsActive = true });

        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        Booking CompletionBooking(string token) => new()
        {
            Id = Guid.NewGuid(),
            BookingType = BookingType.StayRequest,
            Status = BookingStatus.Approved,
            ContactName = "Anna Andersson",
            ContactEmail = "anna@example.se",
            CheckIn = checkIn,
            CheckOut = checkIn.AddDays(2),
            OvernightGuestCount = 2,
            MealPlan = MealPlan.FullBoardAll,
            CompletionToken = token,
            CompletionTokenExpiry = DateTime.UtcNow.AddDays(14),
            GuestChoosesRooms = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            DeleteAfter = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1),
        };
        ctx.Bookings.Add(CompletionBooking(CompletionToken));
        ctx.Bookings.Add(CompletionBooking(CompletionSubmitToken));

        // One Pending stay request so the admin dashboard's "Väntar på svar" queue and the nav
        // count badge have something to render (the completion bookings above are Approved).
        ctx.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingType = BookingType.StayRequest,
            Status = BookingStatus.Pending,
            ContactName = "Familjen Pendlar",
            ContactEmail = "pendlar@example.se",
            CheckIn = checkIn.AddDays(5),
            CheckOut = checkIn.AddDays(7),
            OvernightGuestCount = 12,
            MealPlan = MealPlan.FullBoardAll,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            DeleteAfter = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1),
        });

        await ctx.SaveChangesAsync();
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

        // Seed the first admin so the admin E2E tests can log in (AdminSeeder is a no-op without these).
        psi.Environment["Seed__AdminEmail"] = AdminEmail;
        psi.Environment["Seed__AdminPassword"] = AdminPassword;

        // No SMTP server in E2E. SmtpEmailService already swallows send failures (NFR-13), so a
        // submit still succeeds — but make it fail fast (1 attempt, no retry delay) and give it a
        // valid From address so the MimeMessage builds. Keeps the funnel test quick.
        psi.Environment["Smtp__Host"] = "127.0.0.1";
        psi.Environment["Smtp__Port"] = "587";
        psi.Environment["Smtp__FromAddress"] = "noreply@akerogarden.test";
        psi.Environment["Smtp__FromName"] = "Åkerögården";
        psi.Environment["Smtp__MaxSendAttempts"] = "1";
        psi.Environment["Smtp__RetryDelayMs"] = "0";

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
