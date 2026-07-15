# Åkerögården — Security Review (Phase 7)

**Date:** 2026-07-08
**Scope:** Phase 7 hardening security pass over the security NFRs — **NFR-01–07, NFR-25, NFR-30** — plus authentication/session hardening, antiforgery, rate limiting, secrets management, and the parked hardening items (ForwardedHeaders real-client-IP, DataProtection key durability).
**Method:** static review of `src/AkeroGarden.Web/Program.cs`, the Identity/cookie/antiforgery config, the public/admin routing, the Bicep infrastructure (`infra/azure/main.bicep` + parameter files), and the test suite. Items whose behaviour is only observable in a real Azure environment (HTTPS/HSTS at the edge, VNet isolation, DataProtection keys surviving a platform recycle) are **code/config-verified here and flagged for live verification in Phase 10** when production/staging Azure is exercised.

> This is the Phase 7 gate deliverable required by `.claude/CLAUDE.md` → "Phase completion gate" §5 (Security). Blunt by design.

---

## Verdict

**No vulnerabilities found.** Every security NFR is satisfied in code/config with evidence below. Three items are **deferred to live Azure verification** (Phase 10) because they cannot be honestly asserted from source alone. One **minor documentation inaccuracy** (a mislabelled code comment) and two **accepted design decisions** are recorded.

---

## Security NFR findings

| NFR | Requirement (SRS §12) | Status | Evidence |
|---|---|---|---|
| **NFR-01** | All traffic over HTTPS; SSL managed by App Service | ✅ code-config, ⏳ live-verify P9 | `app.UseHttpsRedirection()` + `app.UseHsts()` (non-dev) in `Program.cs`. Managed cert via `ssl.bicep`. The redirect/HSTS response is only observable behind the App Service TLS edge → confirm live in Phase 10. |
| **NFR-02** | Admin routes redirect unauthenticated requests to login | ✅ | `ConfigureApplicationCookie` sets `LoginPath`/`AccessDeniedPath = /login`; admin pages are `[Authorize]`-gated and the `/admin/bokningar/{id}/…` list endpoints use `.RequireAuthorization()`. Integration-tested (auth-redirect tests). |
| **NFR-03** | Completion tokens cryptographically random (≥128-bit), single-use, time-limited | ✅ | `CompletionTokenPolicy` (unit-tested); token generated on approval, invalidated on submit (FR-62), expiry per §2.6 with 48h floor. |
| **NFR-04** | Inquiry rate limit 5/IP/10 min, distributed store | ✅ | `RedisInquiryRateLimiter` backed by Azure Managed Redis; verified live on staging (5 ok, 6th blocked). Real client IP now correct behind the load balancer via `ForwardedHeaders` (below). |
| **NFR-05** | No personal data in URLs | ✅ | Admin detail routes key on `{Id:guid}`, not names/emails. Completion page keys on an opaque token, not guest identity. Password reset uses a DataProtection token, not PII. Calendar exposes only aggregate state. |
| **NFR-06** | Personal data only in EU (North Europe / Sweden Central) | ✅ code-config | Staging `location: swedencentral`; prod param `location: northeurope` — both EU. All personal-data resources (PostgreSQL, Blob) inherit `location`. |
| **NFR-07** | Public calendar exposes only availability status | ✅ | Calendar renders Free/Partial/Full from the aggregate `AvailabilityCalculator`; no guest/group/booking detail. `IsPubliclyVisible` was removed (architect review I9) so every booking/block counts — no hidden-occupancy leak either. |
| **NFR-25** | PostgreSQL public network access disabled; VNet-only | ✅ code-config, ⏳ live-verify P9 | `main.bicep` Postgres Flexible Server: `network.delegatedSubnetResourceId` + `privateDnsZoneArmResourceId`, `publicNetworkAccess` path Disabled. No firewall rules opening 5432. Live VNet-isolation check is a Phase 10 exit criterion. |
| **NFR-30** | SMTP credentials stored as encrypted secrets, never in source | ✅ | `smtpUsername`/`smtpPassword` are Bicep secure params → Key Vault secrets (`smtp-username`, `smtp-password`); app reads via KV reference. Repo scan of `appsettings*.json` found **no** passwords/secrets/connection strings committed. |

---

## Authentication & session hardening

- **Password policy:** `RequiredLength = 12`, `RequireNonAlphanumeric = true`.
- **Lockout:** 5 failed attempts → 5-minute lockout (`MaxFailedAccessAttempts = 5`, `DefaultLockoutTimeSpan = 5 min`); `lockoutOnFailure: true` on sign-in.
- **Auth cookie:** `HttpOnly = true`; `SecurePolicy = Always` (prod) / `SameAsRequest` (dev); `SameSite = Strict`; 30-day sliding expiry ("remember me").
- **Antiforgery:** cookie `SecurePolicy = Always` (prod). Blazor SSR injects the token into every `EditForm method="post"` automatically (no explicit `<AntiforgeryToken/>` — the Phase-0 duplicate-token 400 bug). Middleware wired via `app.UseAntiforgery()`; a custom handler catches `AntiforgeryValidationException`.
- **Password reset:** 1-hour token lifespan (`DataProtectionTokenProviderOptions.TokenLifespan`), FR-73.

## Rate limiting & real client IP (ForwardedHeaders)

- `UseForwardedHeaders()` is registered **first** in the pipeline, rewriting `RemoteIpAddress`/scheme from `X-Forwarded-For`/`-Proto`.
- **Not spoofable:** `ForwardLimit = 1` with `KnownIPNetworks.Clear()` + `KnownProxies.Clear()` — the documented App Service pattern. The platform front end appends the real client IP as the rightmost XFF entry; a client-set header cannot inject a fake trusted proxy.
- **Fail-open by design:** if the Redis connection string is unparseable at startup, the app degrades to `NoOpInquiryRateLimiter` (logged) rather than 500-ing the public `/boka` page. See *Accepted decisions*.

## DataProtection key durability

- `AddAkeroDataProtection` is config-gated: local dev keeps the filesystem key ring; staging/prod persist the key ring to **Blob** and wrap the master key with **Key Vault** (Bicep provisions the private `dataprotection-keys` container + RSA wrap key + `get/wrapKey/unwrapKey` policy).
- **Why it matters:** without durable, shared keys an App Service recycle/redeploy/scale-out regenerates the ring and invalidates every antiforgery token + auth cookie (a guest mid-`/boka` then hits "A valid antiforgery token was not provided"). Fixed here; the *keys-survive-restart* behaviour is only observable in Azure → **live-verify in Phase 10**.

## Secrets management

- No secrets in source (verified). SMTP credentials, DB and Redis connection strings resolve from Key Vault references / App Service settings / `dotnet user-secrets` (local). Seed admin password is a Bicep secure param, not committed.

---

## Accepted decisions (recorded, not defects)

1. **Password reset accepts the same password.** Re-using the current password on reset is permitted. This is **NIST SP 800-63B-aligned** (no forced rotation / no "must differ" rule) and not a vulnerability — recorded so it is not re-raised as a finding.
2. **Rate limiter fails open.** A bad/unresolved Redis connection string disables rate limiting (logged) rather than breaking the inquiry funnel. Deliberate availability-over-strictness trade-off for a low-abuse public form; the distributed store requirement (NFR-04) is met when Redis is healthy, which is the normal state and is monitored.

## Minor finding (non-security, documentation)

- **`Program.cs` DataProtection comment mislabels the NFR.** The block is commented `NFR-30 — keys survive restarts`, but **SRS NFR-30 is SMTP-credential secrecy**. DataProtection key durability is an *unnumbered Phase-7 hardening item* (found during Phase 6 manual testing), not NFR-30. Cosmetic — the code is correct; only the comment's NFR reference is wrong. *(Recommend: change the comment to reference the Phase-7 hardening item instead of NFR-30.)*

## Deferred to Phase 10 (live Azure verification — cannot be honestly asserted from source)

- **NFR-01 / HSTS:** confirm HTTPS redirect + HSTS header at the App Service edge.
- **NFR-25:** confirm PostgreSQL is unreachable from the public internet (VNet isolation drill).
- **DataProtection durability:** confirm antiforgery tokens + auth cookies survive an App Service restart/redeploy (shared Blob key ring).

*These are already Phase 10 exit criteria; listed here so the security posture is not reported as "fully verified" when three items are only code-verified.*
