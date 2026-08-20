using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using AkeroGarden.Web.Core.Data;
using AkeroGarden.Web.Core.Data.Entities;
using AkeroGarden.Web.Core.Models;
using AkeroGarden.Web.Core.Services;
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
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:16")
        .Build();

    private readonly StringBuilder _appLog = new();
    private readonly object _logLock = new();
    private Process? _adminApp;
    private Process? _publicApp;

    /// <summary>Public host (Static SSR) base URL — the guest-facing surface (home, /boka, /slutfor).</summary>
    public string PublicBaseUrl { get; private set; } = "";

    /// <summary>Admin host (Interactive Server + Identity) base URL — /login and the flattened admin
    /// routes (dashboard at "/", /bokningar, /konton, …).</summary>
    public string AdminBaseUrl { get; private set; } = "";

    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;

    /// <summary>A DbContext bound to the shared test database — for tests that need to seed/clean up
    /// data directly (setup/teardown) rather than through the UI.</summary>
    public ApplicationDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_db.GetConnectionString()).Options);

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        var postgres = _db.GetConnectionString();

        // Two-app topology (Phase 10b): the admin host owns migration + admin seeding and MUST boot
        // first — it creates the schema the public host reads (the public host never migrates). Start
        // admin, wait until it has migrated/seeded and is listening, then start public against the
        // now-ready schema. Both share the one Postgres container.
        AdminBaseUrl = $"http://127.0.0.1:{GetFreePort()}";
        _adminApp = StartApp("AkeroGarden.Web.Admin", AdminBaseUrl, postgres, admin: true);
        await WaitUntilReadyAsync(_adminApp, AdminBaseUrl, "admin");

        PublicBaseUrl = $"http://127.0.0.1:{GetFreePort()}";
        _publicApp = StartApp("AkeroGarden.Web.Public", PublicBaseUrl, postgres, admin: false);
        await WaitUntilReadyAsync(_publicApp, PublicBaseUrl, "public");

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

        KillApp(_publicApp);
        KillApp(_adminApp);

        await _db.DisposeAsync();
    }

    private static void KillApp(Process? app)
    {
        if (app is { HasExited: false })
        {
            try { app.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }
        app?.Dispose();
    }

    /// <summary>Known, unexpired completion tokens seeded for the /slutfor E2E tests. The submit
    /// test uses its own booking because submitting consumes it (Approved → DetailsComplete),
    /// which would otherwise break the read-only tests sharing a token.</summary>
    public const string CompletionToken = "e2e-completion-token-abc123";
    public const string CompletionSubmitToken = "e2e-completion-submit-token-xyz789";

    /// <summary>A SelfCateringAll booking — read-only, for the group-default meal-gating regression
    /// test (Phase 8 bug: the per-day pickers rendered regardless of "Helpension för alla").</summary>
    public const string SelfCateringCompletionToken = "e2e-completion-selfcatering-token-jkl012";

    /// <summary>A booking with the Konferensrum facility selected at inquiry — read-only, for the
    /// "conference toggle only shows when applicable" test (Phase 8 cleaning rework, §5.5).</summary>
    public const string ConferenceCompletionToken = "e2e-completion-conference-token-def456";

    /// <summary>Same as <see cref="ConferenceCompletionToken"/> but single-use, for the cleaning-
    /// submission-persists-to-DB test.</summary>
    public const string ConferenceCompletionSubmitToken = "e2e-completion-conference-submit-token-ghi789";

    /// <summary>Credentials for the admin user AdminSeeder creates on boot (Seed:AdminEmail/Password
    /// passed via env below). The admin E2E tests log in with these before hitting the dashboard.</summary>
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

        // A lodging Building requires a PhysicalBuilding (§8.2, FR-13) — seed one and link it.
        var sovPb = new PhysicalBuilding { Id = Guid.NewGuid(), Name = "Sovbyggnad", IsActive = true };
        ctx.PhysicalBuildings.Add(sovPb);
        // IsAccessible on purpose: it is what renders the `.lodging-item__tag` pill, whose label
        // "Tillgänglighetsanpassad" is a 23-character compound with no break opportunity — the
        // widest single token on the public site, and one that overflowed a 360px screen at a
        // raised text size. Without this flag the layout guards never see it.
        var building = new Building { Id = Guid.NewGuid(), Name = "Huvudbyggnaden", PhysicalBuildingId = sovPb.Id, IsAccessible = true, IsActive = true };
        building.Rooms.Add(new Room { Id = Guid.NewGuid(), BuildingId = building.Id, Name = "Sovsal", BedCount = 40, IsActive = true });
        ctx.Buildings.Add(building);

        // Two lunch options (a real choice, so the "samma rätt alla dagar" shortcut has something to
        // copy) and a single dinner option. Every sitting renders the same way now — one dropdown for
        // the whole group (FR-105) — so a one-option meal is simply a dropdown with one entry.
        ctx.MealOptions.AddRange(
            new MealOption { Id = Guid.NewGuid(), Name = "Rotfruktssoppa", MealType = MealType.Lunch, IsActive = true },
            new MealOption { Id = Guid.NewGuid(), Name = "Ugnsbakad lax", MealType = MealType.Lunch, IsActive = true },
            new MealOption { Id = Guid.NewGuid(), Name = "Kycklinggryta", MealType = MealType.Dinner, IsActive = true });

        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        // BookingReference (FR-92) carries a unique index — these 3 rows insert in one batch, so
        // each needs a distinct placeholder up front (unlike BookingService's two-phase real-format
        // write, which isn't needed here since no test asserts the AG-YYYY-NNNN form).
        Booking CompletionBooking(string token) => new()
        {
            Id = Guid.NewGuid(),
            BookingReference = $"AG-E2E-{Guid.NewGuid():N}"[..20],
            BookingType = BookingType.StayRequest,
            Status = BookingStatus.Approved,
            ContactName = "Anna Andersson",
            ContactEmail = "anna@example.se",
            CheckIn = checkIn,
            CheckOut = checkIn.AddDays(2),
            OvernightGuestCount = 2,
            MealPlan = MealPlan.FullBoardAll,
            // The standard-schedule edge-day meals a real inquiry is created with (§4.1): arrival
            // dinner, departure breakfast. A 2-night stay therefore takes 3 sittings.
            ArrivalDinner = true,
            DepartureBreakfast = true,
            CheckInTime = MealOccasionResolver.StandardCheckInTime,
            CheckOutTime = MealOccasionResolver.StandardCheckOutTime,
            CompletionToken = token,
            CompletionTokenExpiry = DateTime.UtcNow.AddDays(14),
            // A seeded link is a link that was sent (FR-108), dated outside the nudge grace (FR-63).
            // The live expiry above is what keeps the issuance job from replacing the very tokens
            // these tests navigate to.
            CompletionLinkSentAt = DateTime.UtcNow.AddDays(-5),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            DeleteAfter = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1),
        };
        // Every Approved booking carries a building assignment (§8.16), matching what a real
        // approval requires (FR-103/D1) and giving the completion form's read-only "Ert boende"
        // panel (D4) something to render.
        BookingBuildingAssignment Assignment() => new()
        {
            Id = Guid.NewGuid(),
            BuildingId = building.Id,
            BuildingNameSnapshot = building.Name,
            PhysicalBuildingNameSnapshot = sovPb.Name,
            AssignedAt = DateTime.UtcNow,
        };

        var completionBooking = CompletionBooking(CompletionToken);
        completionBooking.BuildingAssignments.Add(Assignment());
        ctx.Bookings.Add(completionBooking);

        var submitBooking = CompletionBooking(CompletionSubmitToken);
        submitBooking.BuildingAssignments.Add(Assignment());
        ctx.Bookings.Add(submitBooking);

        var selfCateringBooking = CompletionBooking(SelfCateringCompletionToken);
        selfCateringBooking.MealPlan = MealPlan.SelfCateringAll;
        selfCateringBooking.BuildingAssignments.Add(Assignment());
        ctx.Bookings.Add(selfCateringBooking);

        // Cleaning rework (Phase 8, §5.5): a Konferensbyggnad physical building + Konferensrum
        // facility + Conference-kind cleaning zone, and two bookings that requested that facility
        // — one read-only (toggle-visibility test), one single-use (submission test).
        var huvudPb = new PhysicalBuilding { Id = Guid.NewGuid(), Name = "Huvudbyggnad", IsActive = true };
        var konferensPb = new PhysicalBuilding { Id = Guid.NewGuid(), Name = "Konferensbyggnad", IsActive = true };
        ctx.PhysicalBuildings.AddRange(huvudPb, konferensPb);

        var konferensrum = new Facility
        {
            Id = Guid.NewGuid(),
            Name = "Konferensrum",
            // Explicit: /faciliteter orders by DisplayOrder and renders the first as the lead
            // feature, which TrustPagesTests asserts by name. The facilities added below must
            // therefore sort after this one — leaving them all on the default 0 broke that test.
            DisplayOrder = 0,
            PhysicalBuildingId = konferensPb.Id,
            AmountExVat = 1200m,
            VatRate = 0.25m,
            IsActive = true,
        };
        ctx.Facilities.Add(konferensrum);

        // /faciliteter renders facilities[0] as the lead teaser and only builds `.fac-grid` when
        // there is a *rest* — so with a single facility the entire grid path never executed and
        // the layout guards were asserting against a page the real site does not show. Two more,
        // one of them deliberately carrying a long Swedish compound, so the grid renders (its
        // first card takes the `fac-card--wide` branch) and the wrapping rules are exercised.
        ctx.Facilities.AddRange(
            new Facility
            {
                Id = Guid.NewGuid(),
                Name = "Tillgänglighetsanpassad samlingslokal",
                DisplayOrder = 10,
                Description = "Samlingslokal med hörselslinga och entré utan trösklar.",
                AmountExVat = 800m,
                VatRate = 0.25m,
                IsActive = true,
            },
            new Facility
            {
                Id = Guid.NewGuid(),
                Name = "Bastu",
                DisplayOrder = 20,
                Description = "Vedeldad bastu vid vattnet.",
                AmountExVat = 400m,
                VatRate = 0.25m,
                IsActive = true,
            });

        ctx.CleaningZones.AddRange(
            new CleaningZone
            {
                Id = Guid.NewGuid(),
                Name = "Huvudbyggnad",
                Kind = CleaningZoneKind.MainBuilding,
                PhysicalBuildingId = huvudPb.Id,
                AmountExVat = 2000m,
                VatRate = 0.25m,
                IsActive = true,
            },
            new CleaningZone
            {
                Id = Guid.NewGuid(),
                Name = "Konferensbyggnad",
                Kind = CleaningZoneKind.Conference,
                PhysicalBuildingId = konferensPb.Id,
                AmountExVat = 2000m,
                VatRate = 0.25m,
                IsActive = true,
            });

        Booking ConferenceCompletionBooking(string token)
        {
            var b = CompletionBooking(token);
            b.FacilitySelections.Add(new BookingFacilitySelection
            {
                Id = Guid.NewGuid(),
                FacilityId = konferensrum.Id,
                FacilityNameSnapshot = konferensrum.Name,
                AmountExVatSnapshot = konferensrum.AmountExVat,
                VatRateSnapshot = konferensrum.VatRate,
            });
            return b;
        }
        ctx.Bookings.Add(ConferenceCompletionBooking(ConferenceCompletionToken));
        ctx.Bookings.Add(ConferenceCompletionBooking(ConferenceCompletionSubmitToken));

        // One Pending stay request so the admin dashboard's "Väntar på svar" queue and the nav
        // count badge have something to render (the completion bookings above are Approved).
        ctx.Bookings.Add(new Booking
        {
            Id = Guid.NewGuid(),
            BookingReference = $"AG-E2E-{Guid.NewGuid():N}"[..20],
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

    /// <summary>Launches one of the two host apps out-of-process on its own Kestrel port. The admin
    /// host (<paramref name="admin"/> = true) additionally migrates the schema and seeds the first
    /// admin, so it must be started and awaited before the public host.</summary>
    private Process StartApp(string projectName, string baseUrl, string postgresConnectionString, bool admin)
    {
        var webProject = LocateProject(projectName);
        var configuration = Environment.GetEnvironmentVariable("E2E_WEB_CONFIG") ?? "Debug";
        var label = admin ? "admin" : "public";

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

        if (admin)
        {
            // Admin-only concerns (Phase 10b: Identity, seeding, invite lifecycle all live on the
            // admin host — the public process has none of them).
            // Seed the first admin so the admin E2E tests can log in (AdminSeeder is a no-op without these).
            psi.Environment["Seed__AdminEmail"] = AdminEmail;
            psi.Environment["Seed__AdminPassword"] = AdminPassword;

            // Enable the Development-only /e2e/setup-link seam so the invite-activation flow (normally
            // reachable only via the emailed link) can be driven by the browser. Never set in prod.
            psi.Environment["E2ETestHooks"] = "true";

            // Force the demo seeder OFF, never merely "leave it unset". `dotnet run` loads the
            // developer's own user-secrets for this project, so a `DemoSeed:Reset=true` left behind
            // after a local reseed (infra/ops/seeding.md tells you to remove it, which is easy to
            // miss) is inherited by this host. DemoDataSeeder then wipes every application table on
            // boot and reseeds its own dataset — destroying the bookings and reference data this
            // fixture just seeded. The suite does not fail cleanly when that happens: the completion
            // tokens no longer exist, /slutfor renders "Länken är inte giltig", and a dozen tests sit
            // out their full 30 s timeout waiting for form elements on a page that will never have
            // them, which reads as "the E2E suite got slow" rather than as a broken fixture.
            // Environment variables are applied after user secrets in the default configuration
            // order, so this wins. Same class of leak as the developer-Redis note further down.
            psi.Environment["DemoSeed__Reset"] = "false";
        }

        // No SMTP server in E2E. SmtpEmailService already swallows send failures (NFR-13), so a
        // submit still succeeds — but make it fail fast (1 attempt, no retry delay) and give it a
        // valid From address so the MimeMessage builds. Keeps the funnel test quick. Both hosts send
        // email (public: guest acks; admin: notifications), so both get the config.
        psi.Environment["Smtp__Host"] = "127.0.0.1";
        psi.Environment["Smtp__Port"] = "587";
        psi.Environment["Smtp__FromAddress"] = "noreply@akerogarden.test";
        psi.Environment["Smtp__FromName"] = "Åkerögården";
        psi.Environment["Smtp__MaxSendAttempts"] = "1";
        psi.Environment["Smtp__RetryDelayMs"] = "0";

        // The inquiry rate limiter (NFR-04) counts per client in Redis — and the E2E host talks to
        // the *developer's* Redis, not a container of its own, so its counters outlive the test run.
        // Every submitting test shares one client address (127.0.0.1), so the real 5-per-10-minutes
        // limit is reached by the suite itself and, worse, carries over into the next run: the funnel
        // test would start failing simply because you ran the suite twice.
        //
        // A per-run key prefix isolates each run, and a high ceiling keeps the suite from throttling
        // itself. The limiter is still wired and exercised — the *limit* is proven against real Redis
        // in RateLimiterIntegrationTests, which is where that assertion belongs.
        psi.Environment["RateLimit__KeyPrefix"] = $"ratelimit:e2e:{Guid.NewGuid():N}:";
        psi.Environment["RateLimit__MaxRequests"] = "1000";

        var app = new Process { StartInfo = psi, EnableRaisingEvents = true };
        app.OutputDataReceived += (_, e) => Capture(label, e.Data);
        app.ErrorDataReceived += (_, e) => Capture(label, e.Data);
        app.Start();
        app.BeginOutputReadLine();
        app.BeginErrorReadLine();
        return app;
    }

    private void Capture(string label, string? line)
    {
        if (line is null) return;
        lock (_logLock) _appLog.AppendLine($"[{label}] {line}");
    }

    private string RecentLog()
    {
        lock (_logLock)
        {
            var lines = _appLog.ToString().Split('\n');
            return string.Join('\n', lines[^Math.Min(40, lines.Length)..]);
        }
    }

    private async Task WaitUntilReadyAsync(Process app, string baseUrl, string label)
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            if (app.HasExited)
                throw new InvalidOperationException($"The {label} app exited during startup.\n--- app log ---\n{RecentLog()}");

            try
            {
                // Any HTTP response means Kestrel is listening (for the admin host, migrate + seed
                // have completed by the time it binds).
                using var resp = await http.GetAsync(baseUrl + "/");
                return;
            }
            catch
            {
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException($"The {label} app did not become ready within 120 s.\n--- app log ---\n{RecentLog()}");
    }

    private static string LocateProject(string projectName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AkeroGarden.slnx")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException("Could not locate the repository root (AkeroGarden.slnx).");

        var csproj = Path.Combine(dir.FullName, "src", projectName, $"{projectName}.csproj");
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
