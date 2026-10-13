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
| `docs/archive/requirements-changelog.md` | Frozen archive of SRS history up to the retirement of document versioning. Not maintained — read-only reference |
| `docs/product/erd.md` | Mermaid entity-relationship diagram (16 app entities) |
| `docs/product/architecture.md` | Mermaid app-level architecture diagram — public/admin surfaces, hosting, external services, tech stack |
| `docs/product/booking-lifecycle-timeline.md` | Mermaid lifecycle diagrams — status transitions and their emails, what the nightly run decides, and five dated approval scenarios showing every completion-link issuance outcome and expiry. Draws the rules in §2.5/2.6/2.7/§9; the SRS remains authoritative |
| `docs/archive/implementation-plan.md` | **Closed history — nothing live.** Phases 0–11 are all done (Phase 11 closed 2026-09-09) and each was collapsed to a summary table row as it closed, because in every case the narrative began to contradict the system it described. Answers one question: *what was built, in what order, satisfying which requirements* — including the phase-to-requirement map at the bottom, the one piece of provenance the SRS deliberately does not carry. **Never a source of truth about current behaviour, and not to be reopened**: it takes a new row only if a new phase is opened. It was going to be deleted; that was reversed 2026-09-09 and the reasoning is in its header |
| `docs/archive/demo-feedback-2026-08-05.md` | Decision record for the client demo held 2026-08-05: every change request, the decision taken on each *and its reasoning*, two design corrections of our own (§7), and a conflict audit against the implementation (§8). Not a phase — it ran inside the window when 10d was still believed to be blocked on a jurist review, and landed before the 2026-09-07 cutover. **Archived 2026-10-11** — the record of *why* on this scope; the SRS carries the resulting requirements and is the authority on behaviour |
| `docs/archive/deposit-flow-2026-10-07.md` | Decision record for the deposit-before-confirmation requirement (2026-10-07): the design, rejected alternatives, decisions D1–D21, the client's answers of 2026-10-08, and everything the change touches, plus D22–D27 (the estimated price). **Archived 2026-10-11 — nothing open.** The record of why — the behaviour itself is in the SRS (FR-115/116) |
| `docs/review/post-launch-security-review.md` | The 2026-09-08/09 security & architecture review — report and tracker of its own remediation; two findings parked with re-open triggers. **The one document left in `docs/review/`**, and it stays there while anything in it carries a re-open trigger |
| `docs/archive/known-conflicts-at-launch.md` | **Archived 2026-10-09 — every item resolved.** The record of what was knowingly carried across the launch line in the legal texts and how each was settled. Current legal truth is the published texts' sources (`docs/legal/Bokningsvillkor.md`, `docs/legal/Integritetspolicy.md`) and the SRS |
| `docs/ux/design.md` | Visual design tokens, CSS classes, logo usage |
| `infra/ops/dns-cutover-runbook.md` | DNS + one.com cutover steps for both Phase 10a (staging, client tenant) and Phase 10d (production, WordPress retirement) |

## Data model (EF Core entities)

Defined in `docs/product/requirements.md §8` and visualised in `docs/product/erd.md`.

Entities: `Booking`, `PhysicalBuilding`, `Building`, `Room`, `Participant`, `BookingMealSelection`, `BookingBuildingAssignment`, `MealOption`, `Facility`, `PriceRule`, `BlockedPeriod`, `CleaningZone`, `BookingCleaningSelection`, `BookingFacilitySelection`, `AuditLog`, `JobRun`. (`PhysicalBuilding` = the physical building a lodging `Building` sits in; the level cleaning is priced at and a whole building is blocked at — see §8.14. `AuditLog` = append-only who-did-what record, Phase 11; stores no guest PII — see §8.15, §6.9. `JobRun` = one nightly job run and its outcome; what it did is the `AuditLog` entries tagged with it — see §8.17, FR-114. `BookingMealSelection` = the group's meal choice per sitting, re-parented from `Participant` in the 2026-08 demo-feedback work; `BookingBuildingAssignment` = which lodging buildings a group sleeps in, replacing per-participant room assignment — `Room` is now capacity data only, never an assignment target.)

ASP.NET Core Identity tables (`AspNetUsers`, etc.) are infrastructure — excluded from the application data model.

**Every schema change is now an incremental migration.** `20260810191257_InitialSchema` is the only migration in the repo and it is the last baseline that will ever be regenerated — see the block below for why the exception is spent. (The old "introduce each entity in the phase that first needs it" rule described how the schema was *designed* during the build; the build is over.)

> **Pre-go-live migration reset — ✅ DONE, and now FINAL (2026-08-10).** Because the system had not gone live and there was no production data to preserve, the migration history was **wiped and regenerated as a single consolidated baseline** — twice. First on 2026-07-13 (`20260713155158_InitialSchema`, commit `1ddce7d`), then a second and last time on 2026-08-10 after the demo-feedback work reshaped the schema: `20260810191257_InitialSchema` is now the only migration in the repo. New entities added in those windows (e.g. `AuditLog`, §8.15; `BookingBuildingAssignment`, §8.16) landed **in the baseline**, not as incremental migrations on top of it — the "incrementally / per phase" rule above describes how the schema was *designed*, not a requirement to keep the pre-launch migration files.
>
> **The exception has now been spent — do not regenerate the baseline again.** It ran to the Phase 10d cutover (amended 2026-08-06; it previously expired at 10c) because the gate that matters is *real guest data*, not *production exists* — 10c provisioned and migrated production, but production has never gone live. That is no longer true after launch. **From the 10d cutover onward the rule is absolute:** every schema change is an incremental migration and history is never rewritten again.
>
> A regenerated baseline **cannot** be applied over an existing schema — `MigrateAsync` tries to `CREATE TABLE` over the old tables and the container dies on the 230s startup timeout (staging, 2026-07-14). Drop and recreate per `infra/ops/db-access-runbook.md`; never migrate in place.

## Critical rules

- **Swedish only** — all public and admin UI text is Swedish from day one. No English strings, no i18n framework (NFR-32). **Guarded since 2026-09-09**, after the Phase 11 gate found this was the one critical rule with no test: `SwedishOnlyRenderedTextTests` (E2E) scans the rendered text of 12 public routes (the 9 pages plus the 3 thank-you variants, NFR-41) and 15 admin routes, and `SwedishOnlyTextTests` (unit) covers validation messages and display names, which live in C# attributes and only render after a failed submit. Note the split — a source scan of `.razor` was tried first and flagged 129 false positives, all leakage from Razor control flow like `@foreach (var booking in Bookings)`; markup is checked as *rendered text* for that reason. Words spelled the same in both languages ("information", "total", "lunch") are deliberately outside the deny-list.
- **One monetary amount per booking — the admin-entered estimated price — and payment state only where the system acts on it** — `AmountExVat` on `CleaningZone` and `PriceRule` is reference data only. The system computes no price from the price lists (C8). **Amended 2026-10:** `Booking.EstimatedPriceInclVat` is the one stored amount — the admin enters it when saying yes, the guest's emails state it (the client's terms §2 say the confirmation states the price, and paying the deposit binds the booking), it is never changed once sent, and the deposit is computed from it (20 %), never stored beside it. The deposit/final-payment tracking booleans were removed from `Booking` in 2026-08 (FR-42 retired) because they tracked nothing the system could act on. **Amended 2026-10:** the `AwaitingDeposit` status with its due date and reminder (FR-115/116) *is* payment state, deliberately — it holds the dates, withholds the completion link and gates confirmation. Never add a further amount, invoice number or "paid" flag beside it: that is the island FR-42 removed (SRS §5.6). The emails never restate the cancellation scale; they point to the booking terms (FR-79).
- **Snapshot pattern** — `BookingMealSelection`, `BookingCleaningSelection`, `BookingFacilitySelection`, and `BookingBuildingAssignment` each keep a live FK **and** a snapshot of the agreed value taken at the time of agreement. The live FK powers queries; the snapshot preserves what was agreed if reference data is later edited (NFR-15/16/33/34).
- **No hard-deletes of referenced reference data** — deactivate (soft) instead; block hard-delete when a record is referenced by a booking (FR-16/20/26/30/88).
- **Booking state machine is one-way** — only the transitions defined in §2.5 are valid. Invalid transitions must be blocked in code, not just hidden in the UI.
- **No PII in URLs** — no guest names, emails, or IDs in query strings or route segments (NFR-05).
- **No personnummer** — never collect or store Swedish national ID numbers (NFR-23).
- **No personal data outside EU regions** — all Azure resources in North Europe or Sweden Central (NFR-06/24). This is the *system's* data: the client forwards `info@akerogarden.se` (also the admin-notification address) to Gmail, which the privacy policy §4/§5 discloses — never reword the policy back to "never outside the EU".
- **Nothing loads from a third-party host** — no font CDN, analytics, tag manager, embedded map, video or social widget; no third-party iframe, script, stylesheet or image. A request like that sends the visitor's IP, and usually cookies, before consent and without a legal basis (Austrian DPA ruling on Google Fonts, Jan 2022; enforced in Germany too). Fonts go in `wwwroot/fonts/` with `@font-face` in `site.css`. **Linking out is fine** — the rule is about what loads unbidden. Where an embed is the obvious solution, build the self-hosted equivalent instead (`/kontakt`'s map is drawn from OpenStreetMap geometry for exactly this reason). This is also what makes §7 of the privacy policy true and keeps the site free of a cookie banner, so breaking it is never a purely visual decision (NFR-35, widened from fonts-only on 2026-08-18). **Since 2026-09-08 a `default-src 'self'` Content-Security-Policy enforces it on both hosts (NFR-39)** — so this is no longer a convention you could breach by accident: the browser blocks it. That policy also forbids inline styling, so **`style="…"` attributes in `.razor` markup are dropped by the browser** — use a modifier class beside the component in `site.css` (see `design.md`); a unit test scans for the attribute.
- **Code style is enforced** — `.editorconfig` + `Directory.Build.props` (`EnforceCodeStyleInBuild`, `IDE0055` = error). Match existing conventions; formatting violations fail the build. Generated EF migrations are excluded.

## Environments (§11.1)

| Environment | Where | Notes |
|---|---|---|
| Local dev | Mac + Docker | PostgreSQL, Redis, Azurite (Blob emulator), Mailpit (SMTP catcher); `dotnet user-secrets` for config |
| Automated test | Ephemeral Azure Pipelines containers | Ephemeral DBs; CI fails on any test failure |
| Staging | Separate Azure resource group | `staging.akerogarden.se`; synthetic data only; test SMTP inbox; power down when idle; **locked to everyone by default** — open it per visit with a temporary IP rule (`infra/ops/staging-access-runbook.md`) |
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

**🚀 THE SITE IS LIVE.** `akerogarden.se` cut over to Azure on **2026-09-07**. **Every phase is now closed** — Phase 11 (post-production hardening & handover) closed 2026-09-09 and passed its completion gate.

**There is no open phase, and no new phase is planned.** The handover was sent 2026-09-14 and the WordPress hosting decommissioned 2026-09-09. **The legal follow-up is complete (2026-10-09):** the client answered every open question, every conflict carried across the launch line is resolved, and the booking terms and privacy policy match the system. Treat further work as ordinary changes against the SRS.

> **The jurist review is not happening and never will be.** Any statement anywhere that 10d is "gated on the jurist review of the Swedish legal text" is **dead** — that was true for weeks and stopped being true on 2026-09-07. The client replaced the terms document instead of answering our questions, no review was commissioned, and the decision to launch without one was taken with the consequences stated and accepted. **`docs/archive/known-conflicts-at-launch.md` is the record** of what was knowingly carried across the line and how each item was settled — every item is now resolved, which is why it is archived. For current legal truth read the published texts' sources (`docs/legal/Bokningsvillkor.md`, `docs/legal/Integritetspolicy.md`) and the SRS; do not re-raise the accepted risks.

What each phase delivered is in `docs/archive/implementation-plan.md`, as one row each. Don't restate the phase list here — it duplicated the plan, and two of its entries had gone stale (Phase 4 still claimed room assignment after FR-56 was withdrawn; Phase 6 still said "Figma-first", when HTML-mockup-first was what actually happened).

## Working style

- Discuss approach before implementing. Wait for explicit confirmation before making changes.
- If you spot a conflict between this file and the SRS, raise it — don't silently pick one.
- **Tombstoned requirements need no work** — they exist only to keep numbering stable: **FR-12, FR-41, FR-42, FR-43, FR-46, FR-56, FR-59, FR-99**. The last four were retired in 2026-08: FR-42 payment tracking, FR-56 per-participant room assignment, FR-59 the "Standard för hela gruppen" cascade (obsolete once meal choice became group-level under FR-105), FR-99 the link-expiry warning email (superseded by FR-108 before it was built). *This list was stale for a week and listed only four — if you are about to implement an FR, confirm against the SRS row rather than trusting a summary anywhere, including this one.*
- **Challenge the user when their instinct is wrong or under-thought.** Don't be agreeable. If a decision has a real downside, say so directly and explain why before offering alternatives.
- Be extremely careful when updating any document under `docs/product/`, all these are strongly integrated into eachother, when updating any of these files, all other files should be checked to be kept in sync with the change.
- **Requirement changes are edited in place — there is no SRS changelog.** The SRS states current truth and carries no version number. When a requirement changes: (1) edit the body text directly; (2) put the rationale in the **commit body**, not just the subject — `git log --follow docs/product/requirements.md` is now the change history, so a lazy commit message loses it permanently; (3) if the change alters a **live constraint** (something a future reader could reasonably try to undo), fold a one-sentence *why* into the section that governs it, so the reasoning survives without needing the git log at all. `requirements-changelog.md` is frozen history — never append to it.
- Before reading `project_akerogarden_history.md` (the archived-phase-history memory file), tell me first — say what you're about to look up and why, don't fetch it silently.
- **GitHub is a public mirror of `main` — catch it up at the start of every session.** `origin` is Azure Repos (PRs, CI/CD); the `github` remote is a public copy at `github.com/eliya-amanoeel/akerogarden`, published 2026-10-12 with the client's consent. The code is MIT (`LICENSE`); the client's content — name, logo, photos, legal texts, public-page copy — is excluded and all rights reserved (README → License), so never describe the whole repo as MIT. Nothing syncs it automatically — PRs auto-complete after a session has ended, so there is no moment to push from. At the start of any session that touches the repo, `git fetch origin github`; if `github/main` is behind `origin/main`, run `git push github main`. That one push is standing permission; anything else still needs asking. Push `main` only — never a feature branch, a tag, or anything not yet merged on Azure. **The repo is public:** no secrets, guest data or client-confidential material in files *or commit messages*.

## Phase completion gate

**Before merging any branch large enough to warrant it**, run a structured evaluation pass. This began as a phase gate; **with every phase now closed it applies to any substantial branch** — a new feature, a schema change, anything touching auth, secrets, infrastructure or the booking state machine. It is *not* required for ordinary fix/chore branches. **Production now serves real guests**, so a finding that would once have been "fix before launch" is now a live defect — weigh them accordingly.

The pass must check:

1. **What the change set out to do** — list each intended outcome as pass / partial / fail with evidence. (This read "phase exit criteria, from `implementation-plan.md`" until 2026-09-09; there are no open phases and no live exit criteria any more.)
2. **Critical rules** in this file — Swedish-only UI, no PII in URLs, no amount stored but the estimated price, self-hosted fonts, no hard-deletes of referenced data, booking state machine transitions, no personnummer, EU regions only.
3. **NFR compliance** — specifically the NFRs the change touches.
4. **FR compliance** — every FR the change claims to implement, verified against its SRS row rather than a summary.
5. **Security** — auth, cookie settings, antiforgery, no secrets in source, no sensitive data in URLs or logs.
6. **Tests** — unit and integration tests written with the feature (not after); CI passes.
7. **Infra** — Bicep covers every resource introduced in the phase; nothing provisioned by clicking.
8. **Design system** — Swedish text, correct CSS tokens, self-hosted fonts, no external requests.

Produce a structured report with three sections: ✅ CONFIRMED, ⚠️ GAPS / CONCERNS, ❌ VIOLATIONS. Be blunt — do not soften findings. Fix every finding before merging; do not carry gaps forward into the next phase.
