# Åkerögården Booking System — Claude Instructions

## Project overview

A full-stack booking and admin system for Åkerögården, a rural retreat in Norrtälje, Sweden. Replaces a decommissioned Payload CMS. The old production database was dropped during decommission — all data must be rebuilt from scratch.

**Single source of truth:** `docs/product/requirements.md` — an unversioned living document stating current truth. If anything here conflicts with the SRS, the SRS wins — raise it, don't silently diverge.

## Tech stack (locked — do not change without explicit discussion)

- **.NET 10 LTS Blazor** — Static SSR for all public pages, Interactive Server for the admin panel
- **EF Core + PostgreSQL** on Azure Flexible Server (VNet-only, no public internet access)
- **ASP.NET Core Identity** for admin authentication (email + password)
- **Azure App Service** Basic B1 (Linux)
- **Azure Managed Redis** — distributed rate limiting (successor to the retired Azure Cache for Redis)
- **Azure Blob Storage** — images and list downloads
- **MailKit** over One.com SMTP
- **Azure DevOps Pipelines** CI/CD (migrated from GitHub Actions in Phase 10a — repo lives in Azure Repos, `dev.azure.com/akerogarden/akerogarden`; deploys authenticate via a service-principal-backed service connection, not a publish-profile secret)
- **Bicep** — Azure infrastructure-as-code. Both production and staging are provisioned from the same `.bicep` templates with different parameter files. Never click-through portal provisioning — if it's not in code, it doesn't exist.

## Architecture

- Public site: `akerogarden.se` — Static SSR Blazor pages
- Admin panel: `admin.akerogarden.se` — Interactive Server Blazor, login-gated
- Staging: `staging.akerogarden.se` — separate Azure resource group (NOT a deployment slot; B1 doesn't support slots)
- Deploy flow: dev → CI → staging → production

## Key documents

| File | Purpose |
|---|---|
| `docs/product/requirements.md` | Main SRS — single source of truth; unversioned living document, edited in place |
| `docs/product/requirements-changelog.md` | Frozen archive of SRS history up to the retirement of document versioning. Not maintained — read-only reference |
| `docs/product/erd.md` | Mermaid entity-relationship diagram (15 app entities) |
| `docs/product/architecture.md` | Mermaid app-level architecture diagram — public/admin surfaces, hosting, external services, tech stack |
| `docs/product/booking-lifecycle-timeline.md` | Mermaid lifecycle diagrams — status transitions and their emails, what the nightly run decides, and five dated approval scenarios showing every completion-link issuance outcome and expiry. Draws the rules in §2.5/2.6/2.7/§9; the SRS remains authoritative |
| `docs/product/implementation-plan.md` | 12-phase dependency-ordered build plan (0–9 done; 10a staging re-provisioning into the client's Azure tenant [done]; 10b public/admin host separation — 3-project split + two App Services on one B1 plan [FR-70, done, staging cut over 2026-07-17]; **10c production pre-stage (DNS-independent) — ✅ done 2026-07-21: prod provisioned/deployed/verified in the client tenant, restore drill + email deliverability (SPF/DKIM/DMARC) passed, prod left running**; **10d cutover & launch — the only phase left before launch, gated on the jurist review of the legal docs**; 11 post-production hardening & handover — partially built, paused). **The 2026-08 demo-feedback work is deliberately not a phase** — see the row below |
| `docs/product/demo-feedback-2026-08-05.md` | Decision record for the client demo held 2026-08-05: every change request, the decision taken on each *and its reasoning*, two design corrections of our own (§7), and a conflict audit against the implementation (§8). Not a phase — it runs inside the window where 10d is blocked on the jurist, but lands **before** the cutover. Authoritative on this scope; the SRS carries the resulting requirements |
| `docs/ux/design.md` | Visual design tokens, CSS classes, logo usage |
| `infra/ops/dns-cutover-runbook.md` | DNS + one.com cutover steps for both Phase 10a (staging, client tenant) and Phase 10d (production, WordPress retirement) |

## Data model (EF Core entities)

Defined in `docs/product/requirements.md §8` and visualised in `docs/product/erd.md`.

Entities: `Booking`, `PhysicalBuilding`, `Building`, `Room`, `Participant`, `BookingMealSelection`, `BookingBuildingAssignment`, `MealOption`, `Facility`, `PriceRule`, `BlockedPeriod`, `CleaningZone`, `BookingCleaningSelection`, `BookingFacilitySelection`, `AuditLog`. (`PhysicalBuilding` = the physical building a lodging `Building` sits in; the level cleaning is priced at and a whole building is blocked at — see §8.14. `AuditLog` = append-only who-did-what record, Phase 11; stores no guest PII — see §8.15, §6.9. `BookingMealSelection` = the group's meal choice per sitting, re-parented from `Participant` in the 2026-08 demo-feedback work; `BookingBuildingAssignment` = which lodging buildings a group sleeps in, replacing per-participant room assignment — `Room` is now capacity data only, never an assignment target.)

ASP.NET Core Identity tables (`AspNetUsers`, etc.) are infrastructure — excluded from the application data model.

**Add EF Core migrations incrementally** — introduce each entity in the phase that first needs it (see implementation plan).

> **Pre-go-live migration reset — ✅ DONE, and now FINAL (2026-08-10).** Because the system had not gone live and there was no production data to preserve, the migration history was **wiped and regenerated as a single consolidated baseline** — twice. First on 2026-07-13 (`20260713155158_InitialSchema`, commit `1ddce7d`), then a second and last time on 2026-08-10 after the demo-feedback work reshaped the schema: `20260810191257_InitialSchema` is now the only migration in the repo. New entities added in those windows (e.g. `AuditLog`, §8.15; `BookingBuildingAssignment`, §8.16) landed **in the baseline**, not as incremental migrations on top of it — the "incrementally / per phase" rule above describes how the schema was *designed*, not a requirement to keep the pre-launch migration files.
>
> **The exception has now been spent — do not regenerate the baseline again.** It ran to the Phase 10d cutover (amended 2026-08-06; it previously expired at 10c) because the gate that matters is *real guest data*, not *production exists* — 10c provisioned and migrated production, but production has never gone live. That is no longer true after launch. **From the 10d cutover onward the rule is absolute:** every schema change is an incremental migration and history is never rewritten again.
>
> A regenerated baseline **cannot** be applied over an existing schema — `MigrateAsync` tries to `CREATE TABLE` over the old tables and the container dies on the 230s startup timeout (staging, 2026-07-14). Drop and recreate per `infra/ops/db-access-runbook.md`; never migrate in place.

## Critical rules

- **Swedish only** — all public and admin UI text is Swedish from day one. No English strings, no i18n framework (NFR-32).
- **No monetary totals stored, and no payment state at all** — `AmountExVat` on `CleaningZone` and `PriceRule` is reference data only. No computed totals anywhere (C8). The deposit/final-payment tracking booleans were removed from `Booking` in 2026-08 (FR-42 retired): invoicing is manual and entirely outside the system, so flags here tracked nothing the system could act on. The deposit *policy* still governs the approval and cancellation emails.
- **Snapshot pattern** — `BookingMealSelection`, `BookingCleaningSelection`, `BookingFacilitySelection`, and `BookingBuildingAssignment` each keep a live FK **and** a snapshot of the agreed value taken at the time of agreement. The live FK powers queries; the snapshot preserves what was agreed if reference data is later edited (NFR-15/16/33/34).
- **No hard-deletes of referenced reference data** — deactivate (soft) instead; block hard-delete when a record is referenced by a booking (FR-16/20/26/30/88).
- **Booking state machine is one-way** — only the transitions defined in §2.5 are valid. Invalid transitions must be blocked in code, not just hidden in the UI.
- **No PII in URLs** — no guest names, emails, or IDs in query strings or route segments (NFR-05).
- **No personnummer** — never collect or store Swedish national ID numbers (NFR-23).
- **No personal data outside EU regions** — all Azure resources in North Europe or Sweden Central (NFR-06/24).
- **Self-hosted fonts only** — no Google Fonts CDN or any external font requests. Loading Google Fonts sends user IPs to Google without a legal basis (Austrian DPA ruling, Jan 2022; enforced in Germany too). Font files go in `wwwroot/fonts/` with `@font-face` declarations in `site.css` (NFR-35).
- **Code style is enforced** — `.editorconfig` + `Directory.Build.props` (`EnforceCodeStyleInBuild`, `IDE0055` = error). Match existing conventions; formatting violations fail the build. Generated EF migrations are excluded.

## Environments (§11.1)

| Environment | Where | Notes |
|---|---|---|
| Local dev | Mac + Docker | PostgreSQL, Redis, Azurite (Blob emulator), Mailpit (SMTP catcher); `dotnet user-secrets` for config |
| Automated test | Ephemeral Azure Pipelines containers | Ephemeral DBs; CI fails on any test failure |
| Staging | Separate Azure resource group | `staging.akerogarden.se`; synthetic data only; test SMTP inbox; power down when idle |
| Production | Azure (North Europe or Sweden Central) | See requirements.md §11 |

## Testing

Write tests **with** each feature, not after (NFR-26–28). Three tiers:
- **Unit tests:** business logic (pricing tiers, retention dates, token expiry, meal/participant validation)
- **Integration tests:** every API endpoint, against real ephemeral containers in CI (no mocking the database)
- **End-to-end / UI tests:** Playwright for .NET (`Microsoft.Playwright`), introduced in Phase 6 (`tests/AkeroGarden.E2E`). Cover key browser journeys (esp. the booking funnel), accessibility (axe-core), and responsive reachability at 360/768/1280. **No pixel/visual-regression screenshot diffing** — flaky across macOS↔CI-Linux font rendering; assert behaviour and structure instead.

### Test integrity — non-negotiable

A test must honestly verify the real behaviour it claims to. Never shape a test just to make it go green. Concretely:

- **Never route a test around a known bug.** Do not fill/avoid a field or step so the suite passes despite a defect. Either **fix the bug**, or write a test that **asserts the current (wrong) behaviour** so it is captured and visible — never hidden.
- **Assert the whole claim, not a weaker proxy.** If a flow is described as "submits", the test drives it to success (e.g. reaches the acknowledgement) — not merely "renders". If a feature "cascades" / "toggles" / "filters", a test exercises that behaviour, not just the presence of the control.
- **Don't call something "verified" that no test exercises.** If you only eyeballed a screenshot, say that — don't report it as tested.
- **If honest verification is hard, spend the time** to make it testable (seed the right data, expose a hook) or fix the underlying issue. Going back, rethinking, and fixing is always preferred over a test that misrepresents coverage. Report coverage gaps plainly instead of papering over them.
- **The clock is an input, never an ambient fact.** Anything that *compares* against "now" takes its now from outside itself — a `today` parameter (`ReminderJob`, `CompletionLinkIssuanceJob`, `EvaluateCompletionAccessAsync`) or an injected `TimeProvider` (`RetentionJob`, `BookingService`). Stamping `CreatedAt`/`UpdatedAt` is not a comparison and is not the concern.
- **Seed test dates relative to today, never as a literal**, and **pin the clock when the test's subject is a *distance*** (the 40-day issue window, the 14-day expiry) via `DbFixture.PinClock` — at a boundary the test and the service each read the clock, and across midnight they disagree by the one day that flips the result. If you pin, take *every* date in that test from the pinned clock (`DbFixture.TodayIn`); a pinned service with a wall-clock seed has two disagreeing "now"s and is worse than not pinning. Slice 7 broke three files at once this way — not three bad tests, one wall clock underneath all of them.

## Implementation phases

Follow `docs/product/implementation-plan.md` in order. Do not start a phase until the prior phase's exit criteria are met.

0. Foundation — scaffold, infra, auth, design system, CI/CD, local dev
1. Reference data + admin CRUD — Building, Room, MealOption, Facility, PriceRule, CleaningZone
2. Public site — Static SSR pages, availability calendar
3. Inquiry & booking lifecycle — state machine, emails, rate limiting, blocks
4. Completion flow — magic-link guest form, snapshots, room assignment
5. Notifications, lists & retention — reminders, PDF/Excel downloads, GDPR auto-deletion
6. UI/UX — visual + experiential design pass (from scratch, both surfaces), Figma-first then Blazor (scope TBD)
7. Hardening & pre-demo readiness — security pass, NFR verification on staging, parked functional polish, operational/content blockers (privacy text, images, VAT)
8. Client feedback — demo the app to the client on staging (real UAT), then implement the agreed issues/changes/improvements
9. Pre-go-live — small agreed pre-launch changes surfaced on staging after Phase 8 sign-off (e.g. admin invite-lifecycle resend/remove), done before the cutover
10. Transition & go-live — 10a staging re-provisioning (client tenant); 10b public/admin host separation; **10c production pre-stage** (provisioning, config reconciliation, production-SMTP email E2E, restore drill — all DNS-independent, verified on `*.azurewebsites.net`); **10d cutover & launch** (DNS/SSL binding, one.com/WordPress cutover, final deploy, **jurist-reviewed legal text** — moved to 10d 2026-07-20, run just before launch as the long pole). The 10c/10d seam is deliberate: only certificate issuance and the WordPress retirement actually require the DNS switch — do not recombine them. The legal text is DNS-independent but was placed in 10d by client choice, and provisioning is decoupled from it so 10c need not wait
11. Post-production hardening & handover — cost/health alerts, logging/diagnostics, admin delete-confirmation dialogs, test-suite honesty audit, audit trail (who-did-what — §6.9, backend now / admin view deferred), post-launch architecture/security review, technical SEO/social, client handover documentation

## Working style

- Discuss approach before implementing. Wait for explicit confirmation before making changes.
- If you spot a conflict between this file and the SRS, raise it — don't silently pick one.
- Tombstoned requirements (FR-12, FR-41, FR-43, FR-46) need no work — they exist only to keep numbering stable.
- **Challenge the user when their instinct is wrong or under-thought.** Don't be agreeable. If a decision has a real downside, say so directly and explain why before offering alternatives.
- Be extremely careful when updating any document under `docs/product/`, all these are strongly integrated into eachother, when updating any of these files, all other files should be checked to be kept in sync with the change.
- **Requirement changes are edited in place — there is no SRS changelog.** The SRS states current truth and carries no version number. When a requirement changes: (1) edit the body text directly; (2) put the rationale in the **commit body**, not just the subject — `git log --follow docs/product/requirements.md` is now the change history, so a lazy commit message loses it permanently; (3) if the change alters a **live constraint** (something a future reader could reasonably try to undo), fold a one-sentence *why* into the section that governs it, so the reasoning survives without needing the git log at all. `requirements-changelog.md` is frozen history — never append to it.
- Before reading `project_akerogarden_history.md` (the archived-phase-history memory file), tell me first — say what you're about to look up and why, don't fetch it silently.

## Phase completion gate

**Before merging any phase branch to main**, run a structured evaluation pass against all deliverables for that phase. The pass must check:

1. **Phase exit criteria** (from `docs/product/implementation-plan.md`) — list each criterion as pass / partial / fail with evidence.
2. **Critical rules** in this file — Swedish-only UI, no PII in URLs, no monetary totals stored, self-hosted fonts, no hard-deletes of referenced data, booking state machine transitions, no personnummer, EU regions only.
3. **NFR compliance** — specifically the NFRs listed in the phase's "Satisfies" row.
4. **FR compliance** — every FR listed in the phase's "Satisfies" row implemented and correctly.
5. **Security** — auth, cookie settings, antiforgery, no secrets in source, no sensitive data in URLs or logs.
6. **Tests** — unit and integration tests written with the feature (not after); CI passes.
7. **Infra** — Bicep covers every resource introduced in the phase; nothing provisioned by clicking.
8. **Design system** — Swedish text, correct CSS tokens, self-hosted fonts, no external requests.

Produce a structured report with three sections: ✅ CONFIRMED, ⚠️ GAPS / CONCERNS, ❌ VIOLATIONS. Be blunt — do not soften findings. Fix every finding before merging; do not carry gaps forward into the next phase.
