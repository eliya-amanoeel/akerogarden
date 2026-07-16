# Operations — diagnostics & logging

How to find out *why* something failed in staging or production, without SSH-ing into the
App Service container. Written for whoever operates this system next (Phase 11).

## Where logs go

Staging and production export structured logs, traces, and dependency calls (Postgres, SMTP,
Redis, Blob) to an **Application Insights** resource, via the OpenTelemetry .NET SDK
(`Azure.Monitor.OpenTelemetry.AspNetCore`), wired in `Program.cs`. It only activates when
`APPLICATIONINSIGHTS_CONNECTION_STRING` is set — Bicep sets it in staging/prod; local dev has
no connection string, so nothing is exported there (console logging only, same as before).

Resource names (from `infra/azure/main.bicep`, output `appInsightsName`):
- Production: `jmk-ag-prod-appinsights`
- Staging: `jmk-ag-staging-appinsights`

Backed by a Log Analytics workspace (`jmk-ag-<env>-logs`) with:
- **Retention: 30 days.** Older data is gone — don't expect to investigate anything older.
- **Daily ingestion cap: 100 MB/day**, to bound cost. If telemetry unexpectedly stops
  appearing mid-day, check the workspace's `workspaceCapping` state in the Portal before
  assuming the app has gone silent — the cap may have been hit.

Wiring OpenTelemetry into `ILogger` exports everything at `Information` level or above, not
just the lines this project added deliberately — verified on staging: EF Core's own
`Executed DbCommand ...` trace (category `Microsoft.EntityFrameworkCore.Database.Command`)
was showing up for every database round-trip, adding real volume against the daily cap for
no diagnostic value beyond what a `Warning`-level EF Core log already gives you (actual query
problems still surface). `appsettings.json` caps that category at `Warning`; local dev keeps
it at `Information` in `appsettings.Development.json` since nothing is exported there anyway.

## Finding a specific failure

Every request gets an **operation ID** automatically (ASP.NET Core OpenTelemetry
instrumentation) that ties together the request, its logs, and any dependency calls it made.

1. **Azure Portal → the App Insights resource → Failures.** Shows failed requests/exceptions
   grouped by type, over time. Click one to see its operation ID and the full call chain.
2. **Transaction search** (same resource, left nav) — search by time range, or by an
   operation ID if you already have one (e.g. from an admin's bug report timestamp).
3. **Logs (KQL)** — for anything the above two don't cover. Two starting queries:

   All traces for one request, once you have its operation ID from step 1 or 2:
   ```kql
   union traces, exceptions, dependencies
   | where operation_Id == "<paste-here>"
   | order by timestamp asc
   ```

   Everything logged about a specific booking (bookings are logged by GUID, never by guest
   name/email — see below), if a guest or admin reports a problem with a known booking:
   ```kql
   traces
   | where message has "<booking-guid>"
   | order by timestamp desc
   ```

## What gets logged (and what doesn't)

- **Booking lifecycle** (`BookingService`): creation, approve/deny/close/cancel, mark
  arrived/completed, completion-link regeneration, and guest completion-form submission —
  each logs the booking's GUID and the transition, never the guest's name or email.
- **Rate limiter** (`RedisInquiryRateLimiter`): a warning when the inquiry rate limit is hit,
  with the request count only — not the client IP, which is personal data under GDPR.
- **Email delivery** (`SmtpEmailService`): retry/failure warnings log the recipient's domain
  only (`***@example.com`), not the full address.
- Nothing logs guest names, phone numbers, or full email addresses — consistent with NFR-05
  (no PII in URLs) extended to telemetry. If you add new logging, keep to entity IDs, not
  personal data, and check `Microsoft.AspNetCore` request logs (URLs) don't leak IDs beyond
  what NFR-05 already permits (booking IDs are GUIDs, not names).

## If Application Insights itself is unreachable

Fall back to the App Service's built-in log stream, same as before this existed:

```
az webapp log tail --name akerogarden-<env>-app --resource-group <rg>
```

This shows stdout/stderr directly (including the one pre-DI-container bootstrap message —
the Redis fail-open warning in `Program.cs` — which happens before the logging pipeline, and
before OpenTelemetry, are wired up, and so is deliberately still `Console.Error.WriteLine`,
not `ILogger`).

## .NET runtime targeting (why the app boots even when the platform patch moves)

The app is published **framework-dependent** onto App Service's `DOTNETCORE|10.0` platform, so
the platform — not our build — owns the runtime and its security patches. That's the right trade
for an app that goes quiet between releases: it keeps getting runtime CVE fixes without a redeploy.

Two guards keep a build from ever targeting a runtime the platform doesn't have (which would fail
to start, since .NET rolls a runtime *forward* but never backward):

- **`global.json`** (repo root) pins the SDK feature band (`10.0.100`, `rollForward: latestPatch`)
  so CI builds are reproducible and can't silently jump a minor/major band. The workflows
  (`ci.yml`, `deploy-staging.yml`, `deploy-prod.yml`) select the SDK from it via
  `global-json-file: global.json` — this is the single source of the build SDK version.
- **`<TargetLatestRuntimePatch>false</TargetLatestRuntimePatch>`** in `AkeroGarden.Web.csproj`
  makes the published app request the **baseline** 10.0 patch and roll *forward* to whatever
  the platform has installed, instead of baking in the build SDK's newest patch.

Net effect: no re-pinning is needed when Azure bumps the platform patch. To move to a new .NET
**minor/major**, bump `TargetFramework` and `global.json` together, and update `linuxFxVersion`
in `infra/azure/main.bicep` in the same change.
