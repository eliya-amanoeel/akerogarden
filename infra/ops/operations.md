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

## A corrected Key Vault secret doesn't reach the app on a restart

The SMTP credentials (`Smtp__Username`, `Smtp__Password`), the Redis connection string, and the
Data-Protection key are all delivered to the apps as `@Microsoft.KeyVault(...)` **references** in
app settings (see `infra/azure/main.bicep`). App Service resolves each reference **once**, then
**caches** the resolved value. The cache refreshes only when:

- **an app setting changes** (any edit re-resolves *all* of that site's references), or
- the periodic **~24h** auto-refresh fires.

**A plain `az webapp restart` does NOT refresh Key Vault references** — it restarts the worker but
keeps the cached value. So if you fix a secret *value* in the vault, restarting the app will *not*
pick it up; the app keeps authenticating with the old value until you touch its config.

Learned the hard way (staging, 2026-07): a `smtp-username` secret corrupted with Unicode smart
quotes (`'staging@akerogarden.se'`, bytes `e2 80 98 … e2 80 99`) was corrected in the vault, and
the app was restarted several times — yet it kept failing SMTP AUTH with `535 5.7.8 [A3]`, because
every restart re-served the cached corrupted value. The vault held the clean value the whole time;
the app never re-read it.

### Force a refresh after fixing a secret value

Re-apply the reference (or any app setting) to trigger re-resolution — do this on **every** app that
carries the reference (both `jmk-ag-<env>-public` and `jmk-ag-<env>-admin` carry the SMTP one):

```bash
for APP in jmk-ag-<env>-public jmk-ag-<env>-admin; do
  az webapp config appsettings set -g <rg> -n $APP \
    --settings "Smtp__Username=@Microsoft.KeyVault(VaultName=jmk-ag-<env>-kv;SecretName=smtp-username)"
done
```

### Verify what the app actually resolved (not what the vault holds)

The vault value and the app's *cached* value can differ — always check the app side.

- Reference resolution status (should say `Resolved`):
  ```bash
  az rest --method get --uri "https://management.azure.com<siteResourceId>/config/configreferences/appsettings?api-version=2022-03-01"
  ```
  Note: `Resolved` only means a value was fetched at some point — it can still be a **stale** one.
- The value the app is really using (definitive), from inside the container via the Kudu command
  API (`https://<app>.scm.azurewebsites.net/api/command`) — App Service exposes the resolved
  secret to the app process as the env var, so dump its bytes:
  ```bash
  python3 -c "import os,sys; v=os.environ.get('Smtp__Username',''); print(repr(v), v.encode().hex(' '))"
  ```
  Clean = `staging@akerogarden.se`; corrupted smart-quote = leading `e2 80 98` / trailing `e2 80 99`.

> **Production risk (Phase 10c):** the same gotcha applies in prod. When correcting any Key Vault
> secret in production, bump an app setting on **both** apps (or wait 24h) — never trust a restart —
> and confirm with the in-container check above before declaring it fixed.

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
