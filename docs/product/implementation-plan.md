# Åkerögården — Implementation Plan

**Based on:** requirements.md, erd.md
**Audience:** the developer building v1.
**Purpose:** a dependency-ordered, phased path from empty repo to production launch. Each phase lists its **goal**, **scope & deliverables**, the **requirements it satisfies**, and **exit criteria** (what "done" means before moving on).

---

## How to use this plan

> ⚠️ **This document is nearly spent, and that is deliberate.** Everything up to and including
> Phase 10c is finished, and on 2026-08-15 those sections were collapsed to a summary table —
> the narrative had started to *contradict* the system it described, which makes it worse than
> useless. What is left live is **Phase 10d** (the cutover) and four open **Phase 11** items.
>
> **For current truth always read `requirements.md`.** This file answers only "what order was
> it built in, and what is left". When 10d closes and the Phase 11 items are done or moved to
> `requirements.md` §10, nothing live remains here and the file should be **deleted**, not
> frozen — `git log --follow` is the archive, and `requirements-changelog.md` already exists for
> anything that genuinely needs a frozen home.

- Phases are **dependency-ordered** — each builds on the one before. Don't start a phase until the prior phase's exit criteria are met.
- Every requirement ID (FR-xx / NFR-xx) traces back to requirements.md. If a phase seems to conflict with the SRS, the **SRS wins** — raise it, don't silently diverge.
- The data model is fixed in requirements.md §8 and visualised in erd.md. **Add EF Core migrations incrementally**, introducing each entity in the phase that first needs it (noted per phase). **Pre-go-live exception — ✅ DONE, and now SPENT (2026-08-10).** With no production data to preserve, the migration history was wiped and regenerated as a single consolidated baseline twice: first on 2026-07-13, collapsing the 14-migration per-phase history into `20260713155158_InitialSchema` (commit `1ddce7d`); then a second and final time on 2026-08-10, collapsing that baseline plus the four demo-feedback migrations into **`20260810191257_InitialSchema`**, the only migration now in the repo. Entities added in those windows — `AuditLog` (§8.15), `BookingBuildingAssignment` (§8.16) — landed **in** the baseline rather than as incremental migrations on top of it. **The exception ran to the Phase 10d cutover** (amended 2026-08-06, was "when Phase 10c migrates the production database"): 10c provisioned and migrated production, but production has never gone live and held **no real data** — the gate that actually matters is *real guest data*, not *production exists*. The 2026-08 demo-feedback work reshaped the schema substantially (`Participant` loses four fields, `MealPreference` is re-parented and renamed, a building-assignment entity is added, the payment flags are dropped), which is why the second regeneration was taken and both the staging and production databases were dropped and recreated. **From the 10d cutover onward the rule is absolute:** production holds real data, every schema change is an incremental migration, and history is never rewritten again. See `docs/review/demo-feedback-2026-08-05.md`.
- **Tombstoned requirements need no work:** FR-12, FR-41, FR-43 (merged/removed), FR-46 (dropped), and — from the 2026-08 demo feedback — FR-42 (payment tracking withdrawn) and FR-56 (per-participant room assignment withdrawn). They're listed in the SRS only to keep numbering stable.

### Cross-cutting concerns (apply in every phase, not a separate phase)

- **Testing (NFR-26–28):** three tiers — **unit** tests for business logic (pricing tiers, retention/deletion dates, token expiry, meal/participant validation); **integration** tests for every API endpoint against ephemeral containers; and **end-to-end / UI** tests (Playwright for .NET — `Microsoft.Playwright`, introduced in Phase 6) covering key browser journeys, accessibility (axe-core), and responsive reachability at 360/768/1280. **No pixel/visual-regression diffing** — it is flaky across OS/font rendering (local macOS ↔ CI Linux) and high-maintenance; assert behaviour and structure instead. CI fails the build on any test failure. Write tests *with* each feature, not after.
- **Swedish-only UI (NFR-32):** all public and admin text is Swedish from day one. No English strings, no i18n framework.
- **GDPR:** no personnummer (NFR-23); personal data only in EU regions (NFR-06/24); no personal data in URLs (NFR-05).
- **Data integrity:** editing/deactivating reference data must never alter saved bookings (NFR-14); snapshots back this (NFR-15/16/33/34).
- **UI approach — functional first, polish last:** In Phases 1–5, build screens that are functional and readable, not polished. No animations, no pixel-perfect spacing, no mobile optimisation — just enough UI to drive and test the feature. Exception: the Phase 0 design system (tokens, base layout, logo component) is foundational and must be done properly — skipping it means inline styles everywhere and a worse retrofit later. Polish, responsiveness, and visual refinement happen in Phase 6 (UI/UX).

---

## What shipped, phases 0 – 10c  ✅ all complete and merged

Collapsed on 2026-08-15. These phases were narrated at ~117 KB while they were being built; that
narrative is now **history, not truth**, and several parts of it had gone stale (the 2026-08
demo-feedback work removed `MealPreference`, `IsFullBoard`, `AttendsBreakfast`,
`GuestChoosesRooms`, per-participant room assignment and the payment flags that those sections
still described). Rather than maintain a record that contradicts the system, the detail is left
where it is durable: **`git log --follow docs/product/implementation-plan.md`** holds every word of
it, `requirements.md` states current truth, and the reasoning for each change is in its commit body.

Before collapsing, every completed section was scanned for *live constraints* — rules a future
reader could reasonably try to undo. All but one already had a home. The exception was the
"why single-app host gating is a dead end" caveat (`AmbiguousMatchException` on two
`MapRazorComponents` roots), which moved to **`architecture.md`**, beside the two-app topology it
explains.

| Phase | Merged | What it delivered | Satisfies |
|---|---|---|---|
| **0** Foundation | 2026-06-29 | Deployable skeleton an admin can log into: Blazor Static SSR (public) + Interactive Server (admin), EF Core + Postgres, ASP.NET Identity, design system, Docker local dev, Bicep IaC, CI/CD | FR-35, FR-71–74 · NFR-01/02/06/11/12/24/25/28/30/32 |
| **1** Reference data + admin CRUD | 2026-07-01 | Six entities as vertical slices — Building, Room, MealOption, Facility, PriceRule, CleaningZone | FR-13–20, FR-22–34, FR-85–88 · NFR-09/14 |
| **2** Public site | 2026-07-01 | Static SSR pages, data-driven Faciliteter / Meny / Priser, pure `AvailabilityCalculator` | FR-01/07/81–83 · NFR-08/17/20–23 |
| **3** Inquiry & booking lifecycle | 2026-07-02 | The booking engine: state machine, emails, rate limiting, blocks, public calendar. Redis migrated to Azure Managed Redis mid-phase when the classic tier was retired | FR-02–11, FR-21, FR-36–42, FR-47–53, FR-75–84 · NFR-03–07/09/13 |
| **4** Completion flow | 2026-07-04 | Magic-link guest form with no account; snapshots. Render mode reversed Interactive → Static SSR + progressive JS | FR-48/49, FR-54–62 · NFR-15/16/18/33/34 |
| **5** Notifications, lists & retention | 2026-07-05 | `DailyJobRunner` in-process `BackgroundService` (B1 *does* support Always On), GDPR retention delete + 30-day warning, 14-day reminder, PDF/Excel lists | FR-44/45, FR-63–69, FR-89–91 · NFR-10 |
| **6** UI/UX | 2026-07-07 | Direction A "Editorial lugn" across both surfaces, 7 slices; introduced the Playwright + axe E2E tier | NFR-17–23, NFR-32/35 |
| **7** Hardening & pre-demo | 2026-07-08 | `ForwardedHeaders` real client IP, DataProtection key durability (Blob + KV), stricter validation, non-blocking approval guard. Three real staging bugs found and fixed. `security-review.md` written | NFR-08/09/10 · residual NFR-01–07/25/30 · NFR-13/32 |
| **8** Client feedback (demo + UAT) | 2026-07-11 | Client signed off on staging. Booking reference `AG-YYYY-NNNN` (FR-92), cleaning rework + new `PhysicalBuilding` entity, admin mobile usability, Matlista redesigned as a kitchen prep summary | NFR-17–19 + whatever the feedback touched |
| **9** Pre-go-live | 2026-07-11 | Admin invite lifecycle — `AdminInviteService`, pending-invite UI | FR-74 |
| **10a** Staging re-provisioning | 2026-07-16 | Staging rebuilt inside the **client's** tenant, `jmk-ag-staging-*` naming; CI/CD migrated GitHub Actions → Azure DevOps | — |
| **10b** Public/admin separation | 2026-07-17 | FR-70 made structurally true: three projects, **two App Service sites on one B1 plan**. Single-app host gating was attempted first and reversed — see `architecture.md` | **FR-70** · reinforces NFR-01/02 |
| **10c** Production pre-stage | 2026-07-21 | Production provisioned, deployed, verified on `*.azurewebsites.net` — restore drill and SPF/DKIM/DMARC deliverability both passed. Left running. **Never gone live** | NFR-31 · NFR-12/29 |
| **Demo feedback** *(not a phase)* | 2026-08-10 | Group-level meals, building-instead-of-room assignment, `Samordnare` role, guest-stated check-in/out times, two turnover days, payment tracking removed, deferred link issuance at 40 days. Authority: `docs/review/demo-feedback-2026-08-05.md` | see that document |

---
## Phase 10d — Cutover & launch

**Goal:** flip the domain and go live. Deliberately short and scripted — every item below is fast and rehearsed, because everything slow was completed in 10c.

**Depends on:** Phase 10c complete. In particular, go-live must not be scheduled until the legal text is jurist-signed — that review is the long pole and it gates this phase, not 10c's engineering work.

**Scope & deliverables:**

- **Privacy-policy + booking-terms legal text jurist-reviewed and live (moved from 10c, decision 2026-07-20).** **Drafts exist** at `docs/legal/Integritetspolicy.md` and `docs/legal/Boknings-och-ordningsvillkor.md` (started 2026-07-11 — see `[[project-legal-documents]]`), grounded in the real controller identity (Kaldeiska katolska Mar Narsay Missionen i Södertälje, org.nr 815601-0129, trading as Åkerögården) and the actual retention logic (§2.7/FR-67–91). Both still carry `[ATT BEKRÄFTA]` gaps (no-show/remaining-balance terms, damage liability, force majeure, cookie section, governing law) and **must be reviewed by a Swedish jurist before publish** — a first draft from an AI assistant is not a substitute for legal sign-off, given GDPR/IMY exposure and Swedish consumer-contract law on the cancellation terms. The privacy policy's **cookie ("Kakor") section** absorbs the cookie-consent assessment: no consent banner is required (only strictly-necessary auth/antiforgery cookies are set today, no analytics/tracking), so this is a documentation task, not a gate implementation. **This is the phase's long pole** — deliberately done just before go-live rather than early, and it gates the launch (NFR-20–24). The legal text itself has no DNS dependency; it sits in 10d only because the client chose to run the jurist review immediately before launch.
- **DNS + SSL:** lower DNS TTLs a day ahead, then configure DNS for `akerogarden.se` (apex — `A`; an apex cannot use `CNAME`) and `admin.akerogarden.se` (`CNAME`). The `asuid` `TXT` verification records are already in place from 10c. Bind the custom hostnames and run `ssl.bicep` for the managed certificates. **Expect the ARM multi-hostname `for`-loop Conflict gotcha** that Phase 10a hit — budget for binding one hostname and one SSL binding manually via `az webapp config hostname add` / `az webapp config ssl bind`, and reconcile the end state back to the Bicep declaration (see `infra/azure/README.md` gotchas). *(Staging's own subdomain binding was handled in Phase 10a.)*
- **Base-URL config flip + final production deploy.** Switch `publicBaseUrl` / `adminBaseUrl` to the real domains and redeploy. **Re-verify one magic-link email end-to-end afterwards** — this is the one 10c verification that does not survive the flip (see the 10c SMTP note).
- **Re-apply Bicep and redeploy to close any drift.** Production may have sat provisioned for weeks while the jurist review ran, with Phase 11 work continuing on `main` in the meantime. Cheap to do, and it guarantees the launched build is current.
- **one.com / WordPress cutover:** one.com is the **registrar + DNS host + email provider**, so the cutover is DNS-record surgery inside one.com. **Retire only the WordPress web hosting** — never touch the DNS zone, the domain registration, or the email records (`MX`, `SPF`, `DKIM`, `DMARC`) and mailboxes, because the app sends guest/admin mail through One.com SMTP; breaking those silently kills every email. Make the **apex `akerogarden.se` canonical and 301-redirect `www`→apex**. Point the apex + `admin` records at Azure; keep WordPress powered but unlinked through a short soak; then decommission the WP hosting. The current live site is trivial (a simple info page — a few images + phone/email), so there is no content-parity work and near-zero rollback risk.
- **Go-live checklist** and post-launch smoke test on the real domains.

**Satisfies:** NFR-20–24 (privacy policy — moved from 10c) · the residual go-live NFRs — production reachable, correctly certificated, and serving the real domains.

**Exit criteria:** the privacy policy and booking terms jurist-reviewed, gap-free and live; go-live checklist complete; the WordPress site decommissioned with DNS + email preserved; production reachable over valid managed SSL on `akerogarden.se` and `admin.akerogarden.se`, with `www` redirecting to the apex; a magic-link email verified to carry real-domain links.

> **Migration discipline resumes here.** The pre-go-live exception described at the top of this plan expires at **this** phase's cutover (amended 2026-08-06 — it previously expired at 10c). Production was provisioned and migrated in 10c but has never held real data, and the demo-feedback work reshapes the schema, so one further baseline regeneration is taken before launch. From the cutover onward production holds real data: every subsequent schema change is an incremental migration and the history is never rewritten again.
>
> **Cutover step, do not skip:** a regenerated baseline **cannot** be applied to a database that still holds the old tables — `MigrateAsync` tries to `CREATE TABLE` over them and the container dies on the 230-second startup timeout (this happened on staging 2026-07-14). Both databases must be **dropped and recreated** per `infra/ops/db-access-runbook.md`, never migrated in place.

---

## Phase 11 — Post-production hardening & handover

**Goal:** the operational maturity and admin-experience items that don't block go-live but shouldn't be left indefinitely once real guests and real data are flowing — plus formally handing the finished system to the client. *(Open-ended, like Phases 8–9: contents are whatever surfaces as real usage begins.)*

**Scope & deliverables:**


### Closed in Phase 11 ✅

Collapsed 2026-08-15 for the same reason as the phases above — the detail is in git and in the
code it describes. Checked for orphaned constraints first; none found.

| Item | Outcome |
|---|---|
| Azure cost + health alerts | ✅ 2026-07-22, `infra/azure/alerts.bicep`, production only. Deliberately minimal after a first over-engineered draft was cut back |
| Logging & diagnostics | ✅ 2026-07-12. App Insights + OpenTelemetry; workflow in `infra/ops/operations.md` |
| Confirmation dialogs on destructive admin actions | ✅ 2026-07-12. Shared `ConfirmDialog`; the modal pattern is in `design.md` |
| Test-suite honesty audit | ✅ 2026-07-12. The rule it produced is in CLAUDE.md |
| Wall-clock dependency in `BookingService` | ✅ 2026-08-10. `TimeProvider` injected; the rule is in CLAUDE.md. **The remaining sweep is still open — see below** |
| Design-system audit (`site.css`) | ✅ 2026-08-11 (PR #46). Found an invisible alert glyph, dead `site.js`, 11 orphaned rules, a doubled `:root`. Rules recorded in `design.md` |
| Admin-notification Reply-To bug | ✅ 2026-07-12, `f05f339` |
| Byggnader tree rework | ✅ 2026-07-14. `.bldg-tree` + shared slide-in panel, both in `design.md` |
| Audit trail — who did what (FR-95–98, §6.9) | ✅ backend built + no-PII inspection passed 2026-07-22. **Admin view deferred** → `requirements.md` §10 |
| "Händelser" — state at admin handover | **Deferred** by decision → `requirements.md` §10 |
| Whole-physical-building blocking | **Removed from scope** 2026-07-14 → `requirements.md` §10 |
| Staging deploy boot failure | ✅ Resolved 2026-07-13 |

### Still open
- **Pinned-clock sweep over the remaining integration test files.** Carried forward from PR #42 (2026-08-10), which made `BookingService` take `TimeProvider` and gave six boundary tests `DbFixture.PinClock`/`TodayIn`. The other integration files were left on the wall clock. **Not a defect today** — their assertions are not about *distances* (the 40-day issue window, the 14-day expiry), which is the only thing that breaks at a midnight boundary. It becomes one the moment any of them starts asserting a distance. The rule itself is in CLAUDE.md ("the clock is an input, never an ambient fact"); this is the outstanding application of it. **If you pin, every date in that test must come from the pinned clock** — a pinned service with a wall-clock seed holds two disagreeing "now"s and is worse than not pinning, which is how slice 7 broke three files at once.
- **Architecture and security review.** A second, post-launch-oriented pass distinct from Phase 7's pre-launch `security-review.md` (which was written before production existed and before any real guest PII flowed through the system). Re-examine the architecture now that it's running for real: production VNet isolation as actually configured (not just declared in Bicep), Key Vault/managed-identity wiring, DataProtection key durability under real restart/scale events, rate-limiter behaviour under real traffic, and whether any Phase 0–10 "accepted decision" (e.g. fail-open rate limiting, NIST-aligned password-reset-same-password) still holds given real usage patterns.
- **Technical SEO & social sharing.** `sitemap.xml`, `robots.txt`, per-page meta descriptions, Open Graph/Twitter Card tags so a shared `akerogarden.se` link renders a proper preview (title/description/image), and Google Search Console domain verification. Explicitly **no analytics** — the privacy policy's "no consent banner needed" position (Phase 10c) stays true; adding analytics would reopen that question and is out of scope here unless separately decided. *(The cookie assessment lives with the privacy policy, a Phase 10d deliverable.)*
- **Client delivery & handover documentation (Swedish).** A short, plain-language admin manual covering the day-to-day admin workflows (approve/deny a booking, resend/remove an invite, generate lists, read the dashboard) — not a developer doc. Paired with an **email-accounts inventory**: every mailbox/account involved in the system's operation (`info@akerogarden.se` guest-facing address, the SMTP sending account and its credentials location, the admin notification recipient(s), the retired staging-only `temp@amaeli.se` test mailbox, and the deferred `dataskydd@akerogarden.se` GDPR contact if it's ever stood up) so the client knows what exists, where credentials live, and what each address is for.

**Satisfies:** operational maturity beyond the go-live NFR set — plus the new audit-trail obligations **FR-95, FR-96, FR-98 and NFR-36/37** (FR-97, the admin view, is deferred). Otherwise no new FR/NFR obligations, except the confirm-dialog item which reinforces the existing hard-delete guard rules (FR-16/20/26/30/88), and the Byggnader tree rework which extends admin-mobile usability (NFR-17) beyond its current documented floor for one specific page.

**Exit criteria:** cost/health alerts live on production (deliberately production-only for this pass — staging is powered down when idle and lower-stakes; a staging alerts pass can follow separately if wanted); a documented logging/diagnostics story exists; every admin hard-delete path requires confirmation; the test-suite honesty audit is complete with findings resolved or explicitly logged; the post-launch architecture/security review is complete with findings resolved or explicitly logged; sitemap/robots/OG tags/Search Console are live; the Byggnader page is rebuilt as a unified Physical Building → Building → Room tree with the approved mobile layout (✅ done, pending the completion gate before merge to main); the audit-trail backend (FR-95/96/98, NFR-36/37 — slices A–C) is built, tested, and verified to hold no guest PII (✅ the no-PII inspection ran 2026-07-22 and passed; FR-97 admin view explicitly deferred); the client has the admin manual and the email-accounts inventory in hand.

---


## Phase → primary requirements map (quick reference)

| Phase | Functional | Non-functional |
| --- | --- | --- |
| 0 Foundation | FR-35, 71–74 | NFR-01,02,06,11,12,24,25,28,30,32 |
| 1 Reference data | FR-13–34, 85–88 | NFR-09,14 |
| 2 Public site | FR-01,07,81–83 | NFR-08,17,20–23 |
| 3 Inquiry & lifecycle | FR-02–06,08–11,10a,21,36–42,47,50,51–53,75–80,84 | NFR-03,04,05,07,09,13 |
| 4 Completion | FR-48,49,54–62 | NFR-15,16,18,33,34 |
| 5 Notifications/lists/retention | FR-44,45,63–69,89–91 | NFR-10 |
| 6 UI/UX | — | NFR-17,18,19,20–23,32,35 (supports 08) |
| 7 Hardening & pre-demo readiness | — | NFR-08,09,10 + residual security (01–07,25,30),13,32 |
| 8 Client feedback | FR-92,93,94 + feedback-driven | NFR-17 (admin mobile),18,19 + feedback-driven |
| 9 Pre-go-live | FR-74 (admin invite lifecycle) | feedback-driven pre-launch polish |
| 10a/10b Staging re-provisioning & host separation | FR-70 (host separation — 10b) | — |
| 10c Production pre-stage | — | NFR-12,29,31 |
| *(Demo feedback 2026-08-05 — not a phase)* | FR-100–108; retires FR-42, FR-56, FR-59, FR-99; amends FR-03,10,16,20,21,38,51,52,53,55,57,58,63,64,75,93,95 | NFR-15,16 (snapshots re-pointed) |
| 10d Cutover & launch | — | NFR-20–24 (privacy policy) · residual go-live (reachability, managed SSL) |
| 11 Post-production hardening & handover | FR-95,96,98 (audit trail; FR-97 UI deferred) | operational maturity (alerts, logging, security review), NFR-36,37 (audit trail), FR-16/20/26/30/88 (delete-confirm UX) |

*Testing (NFR-26–28) and Swedish UI (NFR-32) are cross-cutting and apply to every phase.*

---

*Derived from requirements.md and erd.md — Åkerögården Booking System.*
