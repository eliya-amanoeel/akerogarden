# Åkerögården — Post-Launch Architecture & Security Review (Phase 11)

**Date:** 2026-09-08
**Reviewed against:** production as it was actually running, `main` at `aaf0604` (PR !65)
**Scope:** the whole running system after go-live — the two hosts' composition roots, the security-header middleware, authentication/authorisation, the completion-token and scheduled-job engines, email dispatch, secrets at rest, the Bicep templates, and the live Azure resource configuration in `akerogarden-prod`.

**Method:** two passes.
1. **Static review** of `src/AkeroGarden.Web.Public/Program.cs`, `src/AkeroGarden.Web.Admin/Program.cs`, `AkeroGarden.Web.Core/Security`, the service layer, and `infra/azure/*.bicep`.
2. **Live verification** against production — read-only `az` against every resource in `akerogarden-prod`, `curl` against both public hostnames, and KQL against the Log Analytics workspace (`jmk-ag-prod-logs`).

Every finding below states whether it is **live-verified** or **source-only**. Where the two disagreed, the running system won.

> **Why this exists and what it is not.** This is the *second* security pass. `security-review.md` (Phase 7, 2026-07-08) was written before production existed and before any real guest PII flowed through the system; it was a static review that explicitly parked three items for live verification and recorded a verdict of "no vulnerabilities found" against the state of the code at that time. **It is not superseded and should not be edited.** This document does what that one could not: it examines the system as deployed and serving real guests. Two of its three deferrals closed during the review itself; **the third — DataProtection key-ring durability — closed on 2026-09-09**, so all three are now settled.
>
> **Point-in-time record, with a tracked status column.** The *findings* are as of 2026-09-08 and are **not** retro-edited — if a finding was wrong, it is corrected in the register above rather than quietly rewritten. What does get updated is the **verdict column of the triage table**, plus the unresolved section, so the document doubles as the tracker for its own remediation. A finding's text and its status are deliberately different things. Blunt by design, per the phase-completion gate in `.claude/CLAUDE.md`.

---

## Verdict

**The application layer is in good shape. The risk has moved into the infrastructure and the operational seams.**

The areas most commonly got wrong in a system like this are right here: the authorisation model has no holes, there is no raw SQL anywhere, the completion-token policy is cryptographically sound and carefully reasoned, the CSP work is genuine enforcement rather than convention, and the nightly job engine demonstrably runs. Phase 7's verdict holds for the code it reviewed.

What Phase 7 structurally could not see is where the findings now are: **secrets at rest in App Service configuration, an over-privileged database identity, an unused internet-reachable deployment path, and a credential-bearing URL landing in a log store.**

**24 standing findings — 6 high, 9 medium, 9 low — plus 3 unresolved unknowns.** (A 25th was found on 2026-09-09 while verifying the remediation; see #25.) Two are live defects producing errors in production today. None constitutes an active compromise; none was known to have been exploited.

---

## Corrections register — what the second pass changed

The first pass produced 25 findings. Re-verification changed six of them. Recorded here because the *pattern* matters more than the individual errors: **every incorrect finding came from reasoning out of source code to a conclusion about production without running the query that would settle it** — in one case, a query that had already been run.

| Item | First pass said | Second pass established |
|---|---|---|
| Nightly jobs | "GDPR retention could stop in complete silence — no evidence a run happened" | **Wrong.** All three jobs fire daily, 3/3, for at least 10 consecutive days, logged explicitly in `AppTraces` with 30-day retention. Downgraded to "no *alert* if they stop" (#18) |
| `AdminSeeder` logs admin emails | Reported as a live PII exposure in telemetry | **Retracted.** The regex sweep across `AppTraces`/`AppExceptions` returned **zero** email addresses. The logging paths are one-time and have already run |
| Redis HA drift | "Confirmed real, not noise — resolves one of the three DO-NOT-APPLY unknowns" | **Over-claimed.** The property is live and undeclared, but whether a re-apply removes it depends on the API default for `Balanced_B0`, which was never determined. **Still unresolved; leave the banner as it is** |
| Login enumeration | "An attacker can enumerate accounts" | **Narrowed.** Only distinguishes an account that exists *and is deactivated*. Non-existent and wrong-password both return the same generic error |
| `PK_AuditLogs` crash | Root cause stated as circuit-scoped `DbContext` | **Reframed.** Defect is real; the cause was a hypothesis presented as a conclusion. See #2 |
| 404 router exception | Root cause hypothesised as a stale deployed build | **Reframed.** `@page "/not-found"` has been present since `b0eb057` (Phase 10b). The deployed build has the attribute. **Unexplained** — see #16 |
| DB durability (#15) | Cited geo-redundant backup and HA as open options | **Scope corrected during triage.** Geo-redundancy is unsupported on Burstable, and `infra/azure/README.md` already records HA, read replicas and geo-redundancy as accepted on 2026-07-20. `prod.parameters.json` pointed at that section and the pointer was not followed. Finding narrowed to autogrow, a storage alert, and backup retention |

One finding moved the other way: the ARRAffinity cookies (#10) were filed as low/informational in the first pass and upgraded to medium in the second, once `docs/legal/Integritetspolicy.md` §7 was actually read.

---

## ❌ Live defects — producing errors in production now

### 1. Completion magic-link tokens are written verbatim to Application Insights — HIGH

**Live-verified.** `AppRequests.Url` records the full request URL, so every `/slutfor/{token}` hit stores the token. The workspace holds **3 distinct tokens across 6 requests, each 43 characters** — exactly `Convert.ToBase64String(32 bytes)` minus padding, confirming these are real 256-bit `CompletionTokenPolicy` tokens and not incidental strings. One originates from the end-to-end magic-link verification performed at the 2026-09-07 cutover.

The token is a bearer credential. Per `CompletionAccessPolicy`, holding it grants read access to the booking and the right to submit the completion form — guest names and allergy data. It is retained 30 days in Log Analytics, readable by both subscription Owners and by the deploy service principal (Contributor at RG scope), and it is not redacted anywhere.

NFR-05 is satisfied in the letter — no personal data appears in the URL. But the credential *to* the personal data now sits in a log store, which is the same class of problem the NFR exists to prevent. `CompletionTokenPolicy` itself is not at fault; the telemetry configuration is.

### 2. `PK_AuditLogs` duplicate-key violation tears down admin circuits — HIGH

**Live-verified.** On 2026-09-08 at 16:00:03, 16:00:04 and 16:00:05, `jmk-ag-prod-admin` logged `Npgsql.PostgresException 23505: duplicate key value violates unique constraint "PK_AuditLogs"`. Telemetry properties show it surfacing as `ExceptionRenderingComponent` and then `CircuitUnhandledException` — an admin lost their session to an unhandled error.

**What is established:** there is exactly **one** place an `AuditLog` is constructed (`AuditWriter.RecordAsync`), its `Id` is always `Guid.NewGuid()`, and **no component injects `ApplicationDbContext` directly** (zero occurrences; no `OwningComponentBase`, no `IDbContextFactory` anywhere in the solution). A GUID collision is therefore not the explanation. The only remaining mechanism is a **re-insert of an entity EF still held in `Added` state**, which requires either two overlapping `SaveChangesAsync` calls on one context, or a save that committed server-side while throwing client-side.

**What is not established:** the root cause. `ApplicationDbContext` is registered scoped (`AkeroServiceCollectionExtensions.cs:38`) and the admin host runs Interactive Server, where a DI scope lives as long as the circuit — that is what makes an overlapping or repeated save on one context *possible*, and three occurrences inside two seconds is consistent with a rapid repeat or double-click on a single admin action. **This is a hypothesis, not a conclusion.** Reproduce before changing anything.


**✅ RESOLVED 2026-09-09 — the hypothesis was right, and the trigger was findable.**

Telemetry named the page: `/konton`, two `BeginInvokeDotNetFromJS` events three seconds apart, then the crash and a torn-down circuit. `AdminInviteService.InviteAsync` and `ResendInviteAsync` both ran in this order:

```
await SendInviteEmailAsync(user);   // SMTP — slow, and had NO timeout before #12
await RecordAccountAuditAsync(...); // adds an AuditLog to the context, unsaved
await db.SaveChangesAsync();
```

The slowest call in the service sat inside the window where an audit row was pending. The admin host shares **one `DbContext` for the whole Blazor circuit**, and the accounts page had no double-submit guard, so a second action during that email added its own audit row to the same change tracker. Both operations then called `SaveChangesAsync` over a tracker holding **both** rows, both tried to insert both, and the loser got the duplicate-key violation. That accounts for every observed detail: the page, the three-second gap, why it was the audit table specifically, and why it is rare — it needs the overlap.

**Fixed by removing the window and the overlap.** All three invite paths now commit *before* sending, which also fixes an ordering bug in its own right: the invite email used to go out before the commit, so a failed save left a real invite sent with no record of it. The accounts page now serialises actions through a single in-flight guard and disables its controls while one is running — protection it never had.

Guarded by an integration test that asserts the *ordering*, not the behaviour: the email service checks, from inside the send, that the audit row is already readable on a separate connection. Verified by mutation — reinstating the old order makes it fail.

> **⚠️ DEFERRED — the structural fix, for whoever needs it later.**
>
> The correct fix is `AddDbContextFactory` with a context created per operation, which is Microsoft's documented guidance for Blazor Server precisely because a DI scope there lives as long as the *circuit*, not the operation. It makes this class of collision **impossible** rather than merely unlikely, and it would also close the pre-existing `ObjectDisposedException`s ("Cannot access a disposed context instance") that appear a few times a week and share the same cause.
>
> It was skipped deliberately: every service takes `ApplicationDbContext` by constructor injection, so the change touches the whole service layer and every test that composes one — a large, risky refactor for a panel one person uses. What shipped removes the trigger and the window; it does not remove the possibility. If the admin panel ever gains concurrent users, background work writing through the same context, or a second instance, **do this properly first** rather than adding more guards.

### 3. The published privacy policy's cookie table is incomplete — MEDIUM

**Live-verified against the document.** `docs/legal/Integritetspolicy.md` §7 states the site uses **"endast strikt nödvändiga kakor"** and then gives an exhaustive two-row table: the antiforgery cookie and the admin auth cookie. Production sets **`ARRAffinity` and `ARRAffinitySameSite` on every response of both hosts**, including the public site. Neither appears in the table; one carries `SameSite=None`.

Three consequences. The table is factually incomplete on a live legal document. The no-cookie-banner argument in §7 rests on that table being complete. And on the **public** host, affinity serves no purpose — Static SSR, stateless, single instance — so it is not defensible as "strictly necessary" there in the way it is for the admin host's Blazor circuits.

Note how this was missed: line 3 of the file records the content as *"verifierat mot koden … kakor"*. Verified against the code — and ARRAffinity is set by the App Service platform, not by the code. The verification was real but scoped too narrowly.

`clientAffinityEnabled` is also not declared in `main.bicep`, so the live value is undeclared state.

---

### 25. The CSP blocks every facility image — MEDIUM, found 2026-09-09

**Found after the review, by testing something else.** Verifying the managed-identity switchover (#9) meant uploading a facility image on staging. It uploaded fine — and did not display.

**Two live requirements contradict each other.** FR-81 has the public site render facility images from their anonymous Blob URLs. `img-src 'self' data:`, shipped for NFR-39 on 2026-09-08, does not list the storage account, so the browser blocks every one of them. The blob itself is healthy: it returns 200 and the container was publicly readable. Nothing is wrong with storage; the browser simply refuses to fetch it.

**Production was not visibly affected, which is why nobody noticed.** It has never had a facility image uploaded, so there was nothing to block. The defect would have surfaced the first time the client added a photo — silently, with no error anywhere, on the page that sells the venue.

**This is a gap in the review itself, not only in the code.** The audit verified the CSP was present, correct, identical across both hosts, and that it did not break the admin circuit. It never asked whether the policy broke a *feature*. A policy can be perfectly formed and still forbid something the system is required to do, and no amount of header inspection reveals that — only exercising the feature does. The two requirements shipped a day apart and neither review caught the collision.

**Fixed by serving images from the app** (`/bilder/{name}`) rather than widening `img-src`. Widening was the one-line option and was rejected: routing through the app keeps the policy tight, and the visitor's browser never contacts the storage domain at all, which is what NFR-35 is actually about. It follows the precedent already set by `/kontakt`'s map, which is drawn from OpenStreetMap geometry rather than embedded for the same reason.

Consequences worth knowing: the database now stores a blob **name** rather than a URL (both shapes are still accepted, so no data migration was needed); the `facility-images` container is **private**, since nothing fetches it anonymously any more; and the **public** host now needs blob read access it never had before, because it serves the bytes.

---

## ⚠️ Gaps and concerns

### High

**4. Three production secrets sit as plaintext App Service settings while others correctly use Key Vault.** *Live-verified.* `ConnectionStrings__Postgres` (both sites), `DataProtection__KeysBlobConnectionString` (both sites), `Blob__ConnectionString` (admin) and `Seed__AdminPassword` (admin) are literal values. `Smtp__Username`, `Smtp__Password` and `Redis__ConnectionString` are `@Microsoft.KeyVault(...)` references. The pattern is half-applied. Anyone holding Contributor — which includes the deploy service principal — reads them with one CLI call. Phase 7's "no secrets in source (verified)" was and remains true; it simply never asked where secrets live at rest in configuration.

**5. The application connects to PostgreSQL as the server administrator.** *Live-verified, definitive.* Both sites' live connection strings use `Username=akerogarden_admin`; the server's `administratorLogin` is `akerogarden_admin`. The application runtime therefore holds rights to drop the database and manage roles. This compounds #4: the credential exposed in plaintext is the most privileged one available.

> **✅ RESOLVED 2026-09-09 — for the public host, deliberately not for admin.**
>
> A least-privilege role `akerogarden_public` now serves the internet-facing site: `CONNECT`, `USAGE` on the schema, and `SELECT/INSERT/UPDATE/DELETE` plus sequence access on the data. Verified on both environments that it **cannot** create tables, create roles, or act as superuser. `ALTER DEFAULT PRIVILEGES FOR ROLE akerogarden_admin` covers tables future migrations create — without that the role works until the next migration and then fails on a page nobody tested.
>
> **The admin host still connects as the owner, and that is the honest limit of this fix.** It runs `MigrateAsync` at startup, which needs DDL. Moving migrations into the deploy pipeline was considered and is **not available**: PostgreSQL is VNet-only and the hosted build agent cannot reach it, so migrations must run from inside the VNet — which means from the app container. The alternative, a second owner-credential used only for the migration call, was judged not worth the extra moving parts on a live database for a panel one person uses.
>
> What this buys: the process handling anonymous internet traffic can no longer alter or destroy the schema. What it does not buy: the admin process still can. If migrations ever move out of app startup, finish the job.
>
> Staging first, then production, each verified by the same three privilege queries before the credential was switched — and staging additionally by a real booking submission, which is the only check that exercises `INSERT` and the identity sequence together.

**6. SCM and FTP basic publishing credentials are enabled and internet-reachable on both production sites.** *Live-verified.* `basicPublishingCredentialsPolicies/ftp` and `/scm` both return `allow: true`, and both `jmk-ag-prod-{public,admin}.scm.azurewebsites.net` answer **401** (reachable, auth required) rather than 403. Deploys authenticate via the Azure DevOps service principal, so this is a credential-based path into the container that nothing uses. Kudu grants file-system and console access to a container whose environment holds the database administrator password. Neither policy is declared in Bicep.

**7. Key Vault is the weakest link in an otherwise careful secrets design.** *Live-verified.* `enablePurgeProtection` is null (off), `softDeleteRetentionInDays` is 7 (the minimum), `publicNetworkAccess` is Enabled with `networkAcls: null`, RBAC is disabled in favour of vault-wide access policies, and there are no diagnostic settings. The vault holds the RSA key wrapping **both** hosts' DataProtection master keys: without purge protection that key can be deleted and purged outright, permanently invalidating every auth cookie and antiforgery token. With no diagnostics there is no record of who read which secret, ever.

### Medium

**8. No diagnostic settings on any resource in the production resource group.** *Live-verified* — all 23 checked; Key Vault, PostgreSQL, both App Services and the storage account all return `[]`. There is no infrastructure-layer audit trail for a system holding guest PII.

**9. The admin login form leaks deactivated-account existence, and neither login nor password reset is rate-limited.** *Source-verified.* `Login.razor` returns "Det här kontot är inaktiverat." before `PasswordSignInAsync`, so that one state is observable without a password and without tripping lockout. The contrast is instructive: `ForgotPassword.razor` deliberately does the opposite, always reporting success, with a comment saying so. Separately, `AddAkeroInquiryRateLimiting` is public-host-only. Identity lockout is per-account, so an attacker can also lock out a named admin at will, and the password-reset form is an unauthenticated email trigger sharing the One.com SMTP account with guest booking mail — abusing it degrades the booking funnel.

**10. One storage account key spans both DataProtection containers.** *Source-verified against live config.* Both hosts receive the same `blobConnectionString`, built from `storageAccount.listKeys()`. The public site can therefore read the admin site's `dataprotection-keys-admin` container. The isolation is real at the DataProtection *application-name* layer but not at the credential layer — which is not what the "shared nothing" comments in either `Program.cs` imply. `allowBlobPublicAccess` is true account-wide (required for facility images) and `networkRuleSet.defaultAction` is `Allow`.

**11. A failed email still marks itself as sent.** *Source-verified.* `SendPreDeletionWarningAsync` → `SendToAdminAsync` → `SmtpEmailService.SendAsync`, which swallows every exception by design (NFR-13). `RetentionJob` then sets `booking.DeletionWarningEmailSentAt = now` unconditionally. A silent SMTP failure therefore records the FR-66 pre-deletion warning as delivered, and the record is deleted 30 days later with nobody having been warned. The marker records an attempt, not a delivery.

**12. SMTP is awaited inline in the public booking POST, with no client timeout.** *Source-verified.* `CreateStayRequestAsync` awaits two sends sequentially, each up to `MaxSendAttempts = 3`. `TrySendAsync` sets no `client.Timeout`, so MailKit's 120-second default applies per operation. A hung One.com connection can hold a guest's booking submission open for minutes. NFR-13 guarantees mail failure never *fails* the booking — not that it never *blocks* it.

**13. Telemetry runs at 55–60 % of its daily cap at near-zero traffic.** *Live-verified.* Ingestion is 0.054–0.059 GB/day against `dailyQuotaGb: 0.1`. It has never tripped. `AppMetrics` (2.16 GB cumulative) and `AppPerformanceCounters` dominate; traces and exceptions are 0.0007 GB each. An incident will hit the cap and stop ingestion precisely when the logs are needed.

**14. No `EnableRetryOnFailure` against a Burstable PostgreSQL.** *Source-verified.* `AddAkeroDatabase` calls plain `UseNpgsql`. Flexible Server performs maintenance failovers; every transient blip currently becomes a user-facing 500.

**15. PostgreSQL storage has no autogrow and no alert; backup retention is at the 7-day minimum.** *Live-verified.* `storage.autoGrow: Disabled` at 32 GB with no storage alert, and `backupRetentionDays: 7`.

**Scope corrected after first drafting.** This finding originally also cited `geoRedundantBackup: Disabled` and HA disabled. Both are **withdrawn**: geo-redundant backup is not available on the Burstable tier at all, and `infra/azure/README.md` §"SKU limitations" already records the absence of HA, read replicas and geo-redundancy as **deliberately accepted on 2026-07-20**, with the cost reasoning stated. Not open questions, and not reopened here.

What remains is genuinely open, and the first half is endorsed by that same README, which says storage autogrow *"can be enabled after creation, so this is a monitoring concern rather than a provisioning decision"* — the mitigation is named there and was never implemented. There is a `pg-cpu-credits-remaining` alert but nothing watching storage; if the database fills 32 GB, PostgreSQL goes read-only and the booking system stops accepting writes, with no warning. Backup retention is adjustable to 35 days on Burstable and is not discussed anywhere in the repo.

### Low

**16. Every 404 throws a router exception — unexplained.** *Live-verified*, 15 on 2026-09-08 and 25 on 2026-09-07, public host only: `The type AkeroGarden.Web.Public.Components.Pages.Public.NotFound does not have a Microsoft.AspNetCore.Components.RouteAttribute applied to it`. The source **does** carry `@page "/not-found"` and has since `b0eb057` (Phase 10b), so the deployed build has the attribute. Both `Router.NotFoundPage` and `UseStatusCodePagesWithReExecute("/not-found")` are wired. No candidate cause remains; needs a repro.

**17. `/Error` returned 500 repeatedly at 15:58 on 2026-09-08** — entirely within the cold-start window after a container restart, consistent with the documented deploy-window behaviour. Not treated as a new defect.

**18. No alert if the nightly jobs stop.** The jobs demonstrably run (see the corrections register), and `alwaysOn` is true with `numberOfWorkers: 1`, so the design holds. Nothing notifies anyone if the daily count drops to zero. Related latent risk: nothing enforces single-instance, so a future scale-out would silently double-send every reminder.

**19. `Server: Kestrel` disclosed on every response.** *Live-verified.*

**20. No `healthCheckPath`; HTTP/2 disabled; `HEAD` returns 405 on all pages.** *Live-verified.* App Service cannot detect an unhealthy instance, and some uptime monitors use HEAD.

**21. `ReturnUrl` is captured but never honoured.** *Source-verified.* The auth cookie appends it; `Login.razor` always routes to the role-aware landing path. No open redirect — that is the safe failure — but it partly defeats the deliberate `SameSite=Lax` cookie decision, whose stated purpose was letting an admin click through from a notification email.

**22. Two subscription Owners; one service principal holds Contributor on prod *and* staging.** *Live-verified.* Relevant to handover: a compromise of the deploy service connection reaches production configuration, and therefore the secrets in #4.

**23. No smart-detection alert rule exists.** *Live-verified* — zero `smartDetectorAlertRules` in the RG. The `Failure-Anomalies-Alert-Rule-Deployment` of 2026-07-20 failed and was never noticed. Explicit 5xx alerts cover the same ground.

**24. `clientAffinityEnabled` is not declared in Bicep.** Folded into #3.

**Context, not a finding:** the public site receives sustained hostile scanning — 902 requests to `xmlrpc.php` plus `/blog` probes, all 404. No WAF is in place (B1, no Front Door), and none is proposed at this scale. It does reinforce finishing the WordPress decommission.

---

## ❔ Unresolved

**~~DataProtection key-ring durability~~ — ✅ CLOSED 2026-09-09.** The last open Phase 7 deferral, raised 2026-07-08 and unverifiable from source. Closed behaviourally on production: an authenticated admin session survived `az webapp restart -g akerogarden-prod -n jmk-ag-prod-admin` with no re-login.

That single test settles three things at once. The key ring **is durable across a restart** — the original question. It **survived the switch to managed identity unchanged**, which was the real risk in #9: the app is reading the same ring the connection string wrote, not a newly created one. And it explains the two `AntiforgeryValidationException`s logged at 08:49 that day — they arrived within seconds of a restart while a browser held a page rendered before it, and were stale submissions rather than a key-ring regression. A recreated ring would have invalidated the auth cookie too, and did not.

**All three Phase 7 deferrals are now closed** — NFR-25 and NFR-01 during this review, DataProtection durability here.

**Redis high-availability drift** — `highAvailability: Enabled` is live on the cluster and absent from `main.bicep`. Whether a re-apply would remove it depends on the API default for `Balanced_B0`, which was not determined. **Exactly as unresolved as before this review began** — the DO-NOT-APPLY banner in `main.bicep` should not be amended on the strength of this document.

> **RESOLVED 2026-09-09 by applying the template, which is the only thing that could resolve it.** It was noise. The resource declares no `properties` block at all, so `kind`, `redundancyMode` and `minimumTlsVersion` came back as removals beside `highAvailability` and those are plainly read-only. After the staging and production applies, both clusters still report `highAvailability: Enabled`, `redundancyMode: ZR`, TLS 1.2. The caution above was right on its own terms: this document could not settle it, and reading did not — running what-if and then verifying the applied result did.

**~~`SslMode=Require`~~ — ✅ CLOSED 2026-09-10.** Both live connection strings use it, and the behaviour under Npgsql 10.0.3 is now confirmed from the version's own shipped XML documentation rather than inferred: `Require` is *"Fail the connection if the server doesn't support SSL"* — **encryption only, with no certificate verification**. `VerifyCA` adds certificate validation and `VerifyFull` adds hostname validation; those are the validating modes, and `Require` is not one of them. The wording is identical in 9.0.3 and 10.0.3, so the "semantics vary across major versions" worry does not apply to this pair.

The original risk assessment stands unchanged: **low impact inside a delegated VNet** — `publicNetworkAccess` is `Disabled`, the server resolves through a private DNS zone, and an attacker positioned to present a forged certificate is already inside the VNet. It is now a known property rather than an open question. If it is ever tightened, `SslMode=VerifyFull` is the target; Azure PostgreSQL presents a publicly-rooted certificate, so no CA bundle needs shipping with the app.

---

## ✅ Confirmed sound

**Two Phase 7 deferrals close here with live evidence:**

- **NFR-25 — PostgreSQL VNet isolation.** `publicNetworkAccess: Disabled`, delegated `db-subnet`, private DNS zone, `require_secure_transport on`, `ssl_min_protocol_version TLSv1.2`, `password_encryption scram-sha-256`.
- **NFR-01 — HTTPS and HSTS at the edge.** `http://` → 301, `www` → 308 → apex, `Strict-Transport-Security: max-age=31536000` on both hosts.

**Also verified:**

- **CSP and all four companion headers are correct on both hosts**, including on 404 responses, with the per-host runtime-computed import-map hash working as designed and `X-Robots-Tag: noindex, nofollow` on admin.
- **Authorisation has no holes.** Every routable admin page carries an explicit `[Authorize]`; the five without (`Login`, `Error`, `NotFound`, `ForgotPassword`, `ResetPassword`) are correctly anonymous. The list-download minimal APIs are role-gated to `AdminRoles.All`. Unauthenticated probes of `/`, `/bokningar` and a list-download URL all 302 to `/login`.
- **No raw SQL anywhere** — no `FromSqlRaw`, `ExecuteSqlRaw`, or interpolated variants in the solution.
- **No open redirect.**
- **Image upload is properly validated server-side** — content-type allowlist, 5 MB cap, random blob names never derived from user input.
- **No emails, names or connection strings anywhere in telemetry** — verified by regex sweep across `AppTraces`, `AppExceptions` and `AppRequests`; zero hits.
- **Forgot-password is enumeration-safe**, and email logging consistently masks recipient addresses to the domain.
- **The inquiry rate limiter's fail-open is complete** — handled at the call site in `BookInquiry.razor`, covering a runtime Redis outage and not only startup misconfiguration.
- **The nightly job engine runs** — all three jobs, 3/3, every day for at least 10 consecutive days at 03:00 Stockholm.
- **Alerting and cost control exist** — four metric alerts (public availability, both hosts' 5xx, Postgres CPU credits), an action group to `info@akerogarden.se`, and a monthly budget.

---

## 📌 Open observation — logging pipeline faults on the public host (noted 2026-09-09)

**Not a finding from the review, and not user-impacting.** Recorded here because this document is where production oddities are tracked, and because it is growing.

`System.BadImageFormatException` — *"An error occurred while writing to logger(s). (Format of the executable (.exe) or library (.dll) is invalid.)"*, and *"Could not load file or assembly '&lt;Unknown&gt;'"* at `RuntimeHelpers.CompileMethod`. The JIT failing to compile a method in the **logging path**, so some log writes fail.

| Day | Public restarts | Occurrences |
|---|---|---|
| 2026-07-21 → 2026-08-31 | 13 | **none** |
| 2026-09-07 (go-live) | 3 | 2 |
| 2026-09-08 | 4 | 20 |
| 2026-09-09 | 8 | 60 |

**It began on go-live day**, never having occurred across thirteen restarts in the preceding six weeks, and it is growing faster than restart count — roughly 0.7, then 5, then 7.5 per restart. Confined to the **public** host; the admin host has never logged one.

**Ruled out:** App Service auto-instrumentation conflicting with the OpenTelemetry SDK — the classic cause of `CompileMethod` + `BadImageFormatException`. None of `ApplicationInsightsAgent_EXTENSION_VERSION`, `APPINSIGHTS_INSTRUMENTATIONKEY` or the `XDT_*` settings is present on either site.

**Why it is only an observation.** Request serving is unaffected: zero non-cold-start 5xx, both sites healthy. What it costs is *telemetry completeness* — occasionally a log write is lost — and telemetry is what every other conclusion in this document rests on. That makes it worth knowing about and not worth chasing yet.

**Revisit if:** the rate keeps climbing independently of deploy frequency, it appears on the admin host, or a real incident turns out to have gaps in its logs. The first of those is the signal that matters.

---

## Triage

Four questions per finding: is it a live defect or hardening; who owns it; does it need a decision before any code; what does it cost. Verdicts are recommendations pending sign-off.

| # | Finding | Defect / Hardening | Owner | Decision first? | Cost | Verdict |
|---|---|---|---|---|---|---|
| 1 | Completion tokens in App Insights | Defect (live exposure) | Dev | No | ~1h | **✅ DONE, VERIFIED IN PRODUCTION 2026-09-09** — a probe request to `/slutfor/<token>` was recorded as `…/slutfor/redacted`. Historic rows: purge still `pending` (below) |
| 2 | `PK_AuditLogs` crash | Defect (live) | Dev | No | ~2h | **✅ ROOT CAUSE FOUND & FIXED (code) 2026-09-09** — slow SMTP inside the window where an audit row was pending, on a circuit-shared `DbContext`. Invite paths now commit before sending; the accounts page serialises actions. Structural fix logged below as deferred |
| 3 | Plaintext secrets in app settings | Hardening | Dev + owner | ✅ Decided | ~2h | **✅ DONE 2026-09-09** — Postgres → Key Vault ref; blob key removed via #9; `Seed__AdminPassword` deleted. Verified: **no plaintext secret in any of the four apps** |
| 4 | App runs as Postgres server admin | Hardening | Owner | ✅ Decided | ½ day + window | **✅ DONE 2026-09-09, both environments** — the PUBLIC host now connects as `akerogarden_public`: DML only, verified `can_create_tables = f`, `can_make_roles = f`, `is_superuser = f`. Admin keeps the owner credential because migrations must run from inside the VNet. Scope was a deliberate choice, see below |
| 5 | SCM/FTP basic auth enabled | Hardening | Owner (`az`) | No | 5 min | **✅ DONE, VERIFIED LIVE 2026-09-10** — staging 2026-09-08, production since. All four production policies (`ftp`/`scm` × both sites) return `allow: false`. Note the check: a `curl` to `*.scm.azurewebsites.net` still answers **401**, because the endpoint also challenges for AAD — the 401/403 heuristic used when this finding was raised does **not** distinguish the two states, and the ARM policy is the authoritative signal |
| 6 | Key Vault purge protection / soft-delete / network | Hardening | Owner (`az`) | ✅ Decided | 15 min | **✅ DONE 2026-09-08** — purge protection on, audit logging on. **90-day retention REFUSED by Azure** — `softDeleteRetentionInDays` is immutable once set, so 7 days is permanent for this vault |
| 7 | No diagnostic settings anywhere | Hardening | Dev (Bicep) | ✅ Decided | ~1h | **✅ DONE, VERIFIED LIVE 2026-09-10** — Key Vault `to-logs`/`AuditEvent` (2026-09-08) and PostgreSQL `to-logs`/`PostgreSQLLogs` both present. That is the whole of this item's decided scope; the two web apps and the storage account still carry none, which was never in scope here and is not a residue of this finding |
| 8 | Login enumeration + no rate limit on login/reset | Defect (minor) | Dev | No | ~2h | **✅ deployed 2026-09-09** — not externally verified; exercising it means hammering a live auth form. Covered by unit + integration tests |
| 9 | One storage key spans both DP containers | Hardening | Dev + owner | ✅ Decided | ½ day | **✅ DONE 2026-09-09** — managed identity for **both** key rings and image upload. No storage account key exists in any app or in `main.bicep` |
| 10 | Privacy policy §7 omits ARRAffinity (+ #24 Bicep) | Defect (document wrong) | Dev + owner | Minor | ~30 min | **✅ DONE, both halves verified live** — public sites set zero cookies (2026-09-08); published §7 now carries the `Serverkoppling` row (2026-09-09) |
| 11 | Failed email still sets dedupe marker | Defect (correctness) | Dev | No | ~1h | **✅ deployed 2026-09-09** — verified by integration test, not externally observable |
| 12 | SMTP inline, no client timeout | Resilience | Dev | No | ~30 min | **✅ deployed 2026-09-09** — not externally observable |
| 13 | Telemetry at 55–60 % of daily cap | Hardening | Owner | ✅ Decided | 15 min | **◑ PARKED 2026-09-09 — good enough, not worth more cycles.** `AppPerformanceCounters` is off and verified at zero rows. The OTel-instrument view is restored after being removed on a bad measurement (see below) and ships with the next deploy, no cycle of its own. Nothing is broken and the cap has never been hit; close it opportunistically |
| 14 | No `EnableRetryOnFailure` | Resilience | Dev | No | 15 min | **✅ merged PR !66 and OBSERVED WORKING** — retried a real transient Postgres timeout on `/boka`, 2026-09-09 08:47 |
| 15 | autoGrow off, no storage alert, 7-day backups | Risk position | Owner | ✅ Decided | ~30 min | **✅ DONE, all three verified live 2026-09-09** — `autoGrow: Enabled` and `backupRetentionDays: 35` re-verified after the `main` re-apply; `jmk-ag-prod-pg-storage-percent` (80 %, avg over 15 min) created by the `alerts.bicep` apply that followed. Briefly logged as fully done while the alert was only written — the gap was that `alerts.bicep` is a separate deployment |
| 16 | Every 404 throws router exception | Defect | Dev | No | Unknown | **◑ PARKED 2026-09-09 — log noise, no user impact.** 404s return 404 correctly. Not 1:1 with 404s (8 exceptions against 6 x 404 in the same hour), declining 25→15→8 over three days, and **none since the 10:15 deploy** across subsequent 404s and a deliberate probe. Cause never found; both `NotFoundPage` and `UseStatusCodePagesWithReExecute` are wired, which is redundant and the likeliest conflict. Re-open if it rises or reaches a user |
| 17 | `/Error` 500s on cold start | Not a defect | Dev | ✅ Decided | ~1h | **✅ CLOSED 2026-09-09 — investigated, not a defect.** Every `/Error` 500 in 30-day retention falls inside a restart window. The single outlier (2026-09-09 08:45, 183 s out) is the transient Postgres timeout EF retried — a database fault surfacing through the error page, not the error page failing |
| 18 | No alert if nightly jobs stop | Hardening | Dev (Bicep) | No | ~30 min | **✅ DONE, live 2026-09-09** — `jmk-ag-prod-nightly-jobs-not-running`, severity 1, fewer than 3 `Kör schemalagt jobb` traces in 24 h. **The rule as written would have fired most nights and was fixed before applying:** the jobs run at 01:00:00.1–01:00:01.5 (measured over 7 days of production traces), so shortly after 01:00 yesterday's traces have aged out of the rolling window while today's are not yet queryable — Log Analytics ingestion lags by minutes — and `minFailingPeriodsToAlert: 1` read that as a missed run. Now requires two consecutive failing evaluations. A nightly false sev-1 gets muted, and this rule guards statutory GDPR erasure |
| 19 | `Server: Kestrel` disclosed | Hardening | Dev | No | 5 min | **✅ DONE, verified live 2026-09-09** — no `Server` header on either host |
| 20 | No `healthCheckPath`, HTTP/2 off, HEAD→405 | Hardening | Dev + owner | No | ~30 min | **✅ DONE, verified live 2026-09-09.** `/health` returns `Healthy` on both hosts and is absent from sitemap.xml; the Bicep half landed with the re-apply, so `healthCheckPath=/health` and `http20Enabled=true` are now live on both sites and App Service probes. Both endpoints answer anonymously — an authenticated `/health` would have had App Service judge the admin instance unhealthy and cycle it. HEAD→405 deliberately not pursued |
| 21 | `ReturnUrl` never honoured | UX defect | Dev | No | ~30 min | **✅ deployed 2026-09-09** — needs an authenticated session to observe; covered by `LocalUrl` unit tests |
| 22 | Two Owners, SP Contributor on both envs | Risk position | Owner | ✅ Decided | 15 min | **✅ DONE 2026-09-08** — SP now `Website Contributor` on both RGs; Owner roles unchanged |
| 23 | No smart-detection rule | Hardening | Dev | ✅ Decided | ~1h | **✅ CLOSED 2026-09-09 — investigated, ACCEPTED.** 5xx runs 12–25/day, essentially all clustered in cold starts, against 1,700–3,800 requests. Anomaly detection over that would mostly relearn "deploys cause 5xx". The existing threshold alerts (10 × Http5xx / 5 min, per site) already cover a real spike. First accepted item in this review |
| 24 | `clientAffinityEnabled` undeclared | Hardening | — | — | — | Folded into #10 |
| 25 | CSP blocks every facility image | Defect (latent in prod) | Dev | ✅ Decided | ~3h | **✅ DONE, verified end to end 2026-09-09** — app-hosted `/bilder/{name}` endpoint; both containers now `publicAccess: None` (staging and production, set by the Bicep re-apply). Proven on staging by uploading an image in admin and viewing it on the public site; production holds no facility images yet, so anonymous access was closed there before the client's first upload |

**Unknowns:** DataProtection durability → one answer closes it · Redis HA drift → **INVESTIGATE**, ~10 min · `SslMode=Require` → **INVESTIGATE**, ~5 min *(status now: **all three closed.** DataProtection 2026-09-09 by the restart test; Redis HA 2026-09-09 by the Bicep apply — it was noise; `SslMode` 2026-09-10, from the Npgsql 10.0.3 documentation — `Require` encrypts without verifying the certificate, see above)*

**Tally:** 19 to fix · 4 to investigate · **1 accepted (#23)** · #24 folded into #10. **Every row is ratified; nothing is left undecided and nothing is being left open.**

**Progress at 2026-09-09, after both deployments.** *(⚠️ Read with the 2026-09-10 correction below: this paragraph's "half done: #5 and #7 on staging only" is **wrong** — both were complete. The rest of it holds. Left standing rather than rewritten, because this is a point-in-time record.)* **Verified against the running system:** #1 (a probe request came back as `/slutfor/redacted`), #10 (zero cookies on the public sites, `Serverkoppling` row published), #19 (no `Server` header), #20's `/health` endpoint, plus #3, #6, #9, #15, #22 on the Azure side. **Deployed but not externally observable** — covered by tests rather than by a live probe: #8, #11, #12, #21. **Half done:** #5 and #7 on staging only. **Updated 2026-09-09 after the Bicep re-apply:** #20 is now fully done — `healthCheckPath` and HTTP/2 are live and App Service probes both hosts. #25's container half is live in both environments. `alerts.bicep` was then applied too (16:2x, Succeeded), closing #18 and the last third of #15. **Everything in this review that was ever "declared but not applied" is now applied.** That apply also corrected two defects in `alerts.bicep` that predated the review: the availability test was still pinging the `azurewebsites.net` hostname rather than `akerogarden.se`, so a DNS or certificate failure on the real domain would have left the alert green, and the budget's start date moved forward on every apply.

**#13 is parked, and the way it got there is the lesson.** The original view-based fix *worked* — with it live, `AppMetrics` contained only the internal `_APPRESOURCEPREVIEW_` row, against 35,594 rows for a single http metric the day before. It was nevertheless declared a failure and deleted, because the measurement used to judge it was taken from a window that was almost entirely **before the fix was deployed**. A replacement was written and shipped, and only then did the row-level data show what had actually happened.

Both mechanisms are needed and they cover different sources: the view stops the OpenTelemetry instruments, `EnablePerformanceCounters` stops `AppPerformanceCounters`, and neither reaches the other's. Both are now in place.

Two things worth carrying forward. The measurement error is the same one the corrections register already records — a conclusion drawn from data that could not support it — but this time it destroyed working code rather than just producing a wrong claim. And the item never justified the attention: #13 is spare headroom on a cap that has never been hit, and it consumed three deploy cycles. Severity should govern how much effort a finding gets, and here it did not. Of the three unknowns, **DataProtection durability is closed**; Redis HA drift and `SslMode=Require` remain open exactly as recorded.

> **⚠️ This paragraph was wrong, and so was the one above it. Status re-established against production on 2026-09-10.**
>
> The text that stood here said #20's Bicep apply and #18's alert rule were still unapplied, and that Redis HA drift remained open. The paragraph beginning *"Progress at 2026-09-09"* said the opposite on those three and, in its own first half, called #5 and #7 *"half done, staging only"* — which the triage rows still echoed. **Neither summary was wholly right**, and for two days this document simultaneously asserted and denied the same five facts. Every one has now been checked live:
>
> | Claim | Verified 2026-09-10 |
> |---|---|
> | **#5** SCM/FTP basic auth | **Done.** All four production policies `allow: false` |
> | **#7** diagnostic settings | **Done.** Key Vault `AuditEvent` + PostgreSQL `PostgreSQLLogs` |
> | **#18** nightly-jobs alert | **Applied.** Rule present, enabled, severity 1 |
> | **#20** health check / HTTP/2 | **Applied.** `/health` and `http20Enabled: true` on both sites |
> | **Redis HA drift** | **Resolved.** `highAvailability: Enabled` live; `main.bicep`'s banner already records it as noise |
>
> **Also closed:** the historic-token purge. One `/slutfor` row exists across the workspace's full 52 days of data (back to 2026-07-21) and it is redacted — **zero unredacted tokens remain**. And `SslMode` is answered above.
>
> **The lesson is the one this document already taught itself.** The corrections register records that every wrong finding in the first pass came from *reasoning to a conclusion about production without running the query that would settle it*. This time the same error was made against the **document**: two stale statements outvoted one accurate one, and the tally was treated as evidence. A count of assertions is not evidence. **Re-verify, don't reconcile.**

**Current position:** every one of the 26 findings is fixed, closed, or consciously parked. The only items not in a terminal *fixed* state are **#13** and **#16**, both parked by decision with explicit re-open triggers, and both recorded as such above. **#4 is complete**; #2, #17 and #23 are investigated and closed.

**A 26th item was raised by the maintainer on 2026-09-09, not by this review — ✅ FIXED the same day.** The admin UI had no way to *remove* a facility image, only replace one, and no code path deleted a blob at all, so every replacement orphaned the previous file permanently. FR-27 specifies uploading and never mentions removal, so this was an unspecified gap rather than a violated requirement.

A "Ta bort bild" control now clears the image, and `FacilityService` deletes the superseded blob on replace, on clear, and on hard-delete of the facility. Cleanup runs **after** the commit and never throws: the database is the record of what a facility has, and a storage hiccup must not discard a completed edit — an orphan is a tidiness problem, a failed save is a real one. Four integration tests cover replace, clear, an unrelated edit that must delete nothing, and a storage failure that must not fail the edit; verified by mutation.

**Also fixed, after the maintainer rejected leaving it:** uploading happened the moment a file was chosen, so picking one and then closing the panel orphaned a blob created by an action that had been explicitly cancelled. It was left out of the first pass as "rarer than the replace path", which missed the stronger objection — removal already waits for *Spara*, so the same gesture had two different contracts depending on direction. Selection now holds the file in memory and only `Save` uploads; closing the panel discards it. Guarded by a source scan asserting the upload call lives in `Save` and appears exactly once, following the existing `NoInlineStyleAttributeTests` pattern, because the rule lives in component flow that neither the unit nor the out-of-process E2E tier can reach behaviourally.

*(Superseded — the purge was later confirmed complete, zero unredacted tokens; see "Also closed" above. Kept as written.)* **The historic-token purge has not completed.** The seven purge operations issued on 2026-09-08 all still report `pending` ~20 hours later, and six unredacted `/slutfor` rows remain in `AppRequests`. New requests are redacted, so the exposure is not growing; the backlog is simply still there. Purge is asynchronous and Azure gives no completion estimate, so this needs re-checking rather than assuming, and the rows age out of the 30-day retention regardless.

> ⚠️ **#5 carries a caveat.** Disabling SCM basic auth can break `AzureWebApp@1` depending on how the task authenticates. Apply to staging and confirm a deploy still succeeds before touching production.

### Decisions taken — 2026-09-08

All eight previously-undecided items were resolved in one pass. Recorded here with the reasoning, because the *rejected* options matter as much as the chosen ones.

**#3 + #4 + #9 — full secrets and least-privilege cleanup.** All four plaintext values move behind Key Vault references; a least-privilege application role replaces `akerogarden_admin` in both connection strings; each host gets its own storage credential (or DataProtection moves to managed identity). `main.bicep` is updated to match, but the changes are applied with **targeted `az` commands** — the full template re-apply stays deferred, so this closes the exposure without inheriting the DO-NOT-APPLY risk. The DB role swap needs a maintenance window; the rest does not.

**#6 — Key Vault: purge protection, 90-day soft delete, diagnostics.** Network access deliberately stays public (AAD-gated). Restricting it to the VNet was rejected: the operator's own CLI already hits `Forbidden` on `secrets list`, and locking the network as well would make break-glass access materially harder for a solo maintainer. Purge protection is irreversible and was enabled knowingly.

**#7 + #13 — trim metrics, add Key Vault and Postgres diagnostics, keep the 0.1 GB/day cap.** `AppMetrics` and `AppPerformanceCounters` are ~99 % of current ingestion and near-worthless for this workload; cutting them funds the diagnostics that matter at no extra cost and restores headroom to survive an incident. Raising the cap was rejected as removing a cost ceiling that was set deliberately.

**#15 — autogrow, a storage alert, and 35-day backup retention.** Geo-redundancy and HA were withdrawn from the finding entirely, not merely declined — see the corrections register.

**#22 — tighten the deploy service principal only.** The client's Owner role on their own subscription is **left unchanged**: that is a conversation to have with them, not a unilateral change, and is recorded in the handover material instead.

> **✅ Implemented 2026-09-08.** `akerogarden-devops-deploy` (object id `31698afd-c2c7-4fae-b3fa-b163856782ef`) moved from **Contributor** to **Website Contributor** on both `akerogarden-staging` and `akerogarden-prod`. Zero Contributor assignments remain. Applied to staging first and validated by a successful staging deploy before production was touched; both production sites verified healthy afterwards (200 / 302). No production deploy was run to test it — staging exercised the identical `AzureWebApp@1` task, and the next real deploy will confirm it.
>
> **Correction to the original rationale.** This finding was first written up as shrinking the blast radius behind #3 ("that principal can read every plaintext secret"). **That is wrong.** `Website Contributor` grants `Microsoft.Web/sites/*`, which includes `Microsoft.Web/sites/config/list/action` — a downgraded principal can still read every app-setting *value*. The app-settings exposure is closed by **#3** (Key Vault references), not by this change: once the values are references, the principal reads only the reference string and holds no vault access policy.
>
> What the downgrade actually removes is still material: as RG Contributor the principal could call `listKeys()` on the storage account — obtaining the DataProtection key rings for **both** hosts — and could modify or delete PostgreSQL, Redis, Key Vault's management plane and the VNet. None of that is reachable now.
>
> **Operational consequence:** the deploy principal can no longer touch any non-web resource, so no part of the remaining infra work (#3, #6, #7, #9, #13, #15) can be automated through the pipeline. That is consistent with Bicep never having been in CI/CD; those changes are run interactively by an Owner.

### Ratification of the remaining rows — 2026-09-08

The eight items above were the ones flagged as needing a decision. The other fifteen rows carried a *recommendation* and no sign-off, which is not the same as decided. They were ratified in a second pass:

- **Nothing is accepted.** Both proposed ACCEPT rows were rejected as accepts. **#17** (`/Error` 500s) becomes an investigation with a specific question: the 500s observed on 2026-09-08 were all inside a restart window, so establish whether `/Error` ever fails *outside* cold start — if it does, the styled error page is broken and that is a real defect rather than known deploy-window behaviour. **#23** becomes an investigation into whether anomaly detection earns its place alongside the existing threshold alerts.
- **#2 and #16 are investigated without a timebox** — root cause before any change, explicitly chosen over a defensive fix that would suppress the symptom while leaving the mechanism unknown.
- **All three unknowns are to be resolved**, including the DataProtection key-ring durability check that closes the last outstanding Phase 7 deferral.
- **All eleven remaining fix items are in scope** as a single batch (#1, #5, #8, #10, #11, #12, #14, #18, #19, #20, #21).

The net position is that this review leaves **no accepted risk at all** — unusual, and worth stating plainly, because it means the exit criterion cannot close until all 26 work items reach a terminal state. Recorded as an explicit decision, not an oversight.

---

## Phase 11 exit criterion

> *"the post-launch architecture/security review is complete with findings resolved or explicitly logged"*

The review is **complete** and every finding has a **ratified verdict** — 19 to fix, 4 to investigate, 0 accepted, plus 3 unknowns to resolve. No fixes were applied and no commits made as part of the review itself.

The criterion closes when each of the 26 work items reaches a terminal state. Because nothing was accepted, every one of them must be either fixed or investigated to a conclusion. If an investigation ends without a tractable cause, moving that item to *accepted* is still legitimate — but it becomes a new decision to take deliberately at that point, and the reasoning must be written down here. It is not a silent fallback.

**A merged PR is not a terminal state.** Nine items were merged and awaiting deployment when this was written; until a deploy runs they change nothing in production. #1 was the one to watch — completion tokens continue reaching Log Analytics until the redaction processor is actually running. Mark an item done against the running system, not against `main`, which is the same rule the review itself was built on.

> **✅ CRITERION MET — 2026-09-10.** All 26 work items are in a terminal state, verified against production rather than against `main`. Twenty-four are fixed or investigated to a conclusion; **#13** and **#16** are parked by deliberate decision with explicit re-open triggers, which the criterion's own wording permits as *"explicitly logged"*. The three unknowns are all closed, and #1's backlog is gone — zero unredacted completion tokens remain in the workspace.
>
> Two of those closures took a second look: #5 and #7 had been recorded as half-done and were in fact complete, which was found only by querying Azure rather than re-reading this file. The status block further up carries that correction and the reason it matters.
