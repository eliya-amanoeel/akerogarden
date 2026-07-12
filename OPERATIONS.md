# Operations — diagnostics & logging

How to find out *why* something failed in staging or production, without SSH-ing into the
App Service container. Written for whoever operates this system next (Phase 11).

## Where logs go

Staging and production export structured logs, traces, and dependency calls (Postgres, SMTP,
Redis, Blob) to an **Application Insights** resource, via the OpenTelemetry .NET SDK
(`Azure.Monitor.OpenTelemetry.AspNetCore`), wired in `Program.cs`. It only activates when
`APPLICATIONINSIGHTS_CONNECTION_STRING` is set — Bicep sets it in staging/prod; local dev has
no connection string, so nothing is exported there (console logging only, same as before).

Resource names (from `infra/main.bicep`, output `appInsightsName`):
- Production: `akerogarden-prod-appinsights`
- Staging: `akerogarden-staging-appinsights`

Backed by a Log Analytics workspace (`akerogarden-<env>-logs`) with:
- **Retention: 30 days.** Older data is gone — don't expect to investigate anything older.
- **Daily ingestion cap: 100 MB/day**, to bound cost. If telemetry unexpectedly stops
  appearing mid-day, check the workspace's `workspaceCapping` state in the Portal before
  assuming the app has gone silent — the cap may have been hit.

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
