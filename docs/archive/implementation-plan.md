# Åkerögården — Implementation Plan

**Based on:** requirements.md, erd.md
**Audience:** the developer building v1.
**Purpose:** a dependency-ordered, phased path from empty repo to production launch. Each phase lists its **goal**, **scope & deliverables**, the **requirements it satisfies**, and **exit criteria** (what "done" means before moving on).

---

## How to use this plan

> ✅ **Every phase is closed. This document is now a historical record and holds nothing live.**
> Phases 0–11 each ran as a narrated section and each was collapsed to a **summary table row** as it
> closed — because in every case the narrative eventually began to *contradict* the system it
> described, which makes it worse than useless. Phase 11 closed 2026-09-09 and got the same
> treatment.
>
> **For current truth always read `requirements.md`.** This file answers one question only: *what
> was built, in what order, satisfying which requirements.* It is not a source of truth about how
> anything behaves today, and nothing here should be relied on over the SRS.
>
> **It was going to be deleted, and that decision was reversed on 2026-09-09.** The earlier note said
> that once nothing live remained the file should be DELETED rather than frozen, on the grounds that
> `git log --follow` is the archive. Collapsing Phase 11 the same way as every other phase turned out
> to be the better end: the staleness that motivated deletion came from *live* content contradicting
> the system, and a table of closed phases has nothing left to contradict. What deletion would
> actually have cost is the one thing here with no other home — the phase-to-requirement map at the
> bottom, which is provenance the SRS deliberately does not carry.
>
> **So it stays, and it stays closed.** Do not reopen a phase section to record new work: this file
> takes a new row only if a new phase is ever opened. Anything else belongs in the SRS, in the
> document that owns the subject, or in a commit body.

- Phases are **dependency-ordered** — each builds on the one before. Don't start a phase until the prior phase's exit criteria are met.
- Every requirement ID (FR-xx / NFR-xx) traces back to requirements.md. If a phase seems to conflict with the SRS, the **SRS wins** — raise it, don't silently diverge.
- The data model is fixed in requirements.md §8 and visualised in erd.md. **Add EF Core migrations incrementally**, introducing each entity in the phase that first needs it (noted per phase). **Pre-go-live exception — ✅ DONE, and now SPENT (2026-08-10).** With no production data to preserve, the migration history was wiped and regenerated as a single consolidated baseline twice: first on 2026-07-13, collapsing the 14-migration per-phase history into `20260713155158_InitialSchema` (commit `1ddce7d`); then a second and final time on 2026-08-10, collapsing that baseline plus the four demo-feedback migrations into **`20260810191257_InitialSchema`**, the only migration now in the repo. Entities added in those windows — `AuditLog` (§8.15), `BookingBuildingAssignment` (§8.16) — landed **in** the baseline rather than as incremental migrations on top of it. **The exception ran to the Phase 10d cutover** (amended 2026-08-06, was "when Phase 10c migrates the production database"): 10c provisioned and migrated production, but production has never gone live and held **no real data** — the gate that actually matters is *real guest data*, not *production exists*. The 2026-08 demo-feedback work reshaped the schema substantially (`Participant` loses four fields, `MealPreference` is re-parented and renamed, a building-assignment entity is added, the payment flags are dropped), which is why the second regeneration was taken and both the staging and production databases were dropped and recreated. **From the 10d cutover onward the rule is absolute:** production holds real data, every schema change is an incremental migration, and history is never rewritten again. See `docs/archive/demo-feedback-2026-08-05.md`.
- **Tombstoned requirements need no work:** FR-12, FR-41, FR-43 (merged/removed), FR-46 (dropped), and — from the 2026-08 demo feedback — FR-42 (payment tracking withdrawn) and FR-56 (per-participant room assignment withdrawn). They're listed in the SRS only to keep numbering stable.

### Cross-cutting concerns (apply in every phase, not a separate phase)

- **Testing (NFR-26–28):** three tiers — **unit** tests for business logic (pricing tiers, retention/deletion dates, token expiry, meal/participant validation); **integration** tests for every API endpoint against ephemeral containers; and **end-to-end / UI** tests (Playwright for .NET — `Microsoft.Playwright`, introduced in Phase 6) covering key browser journeys, accessibility (axe-core), and responsive reachability at 360/768/1280. **No pixel/visual-regression diffing** — it is flaky across OS/font rendering (local macOS ↔ CI Linux) and high-maintenance; assert behaviour and structure instead. CI fails the build on any test failure. Write tests *with* each feature, not after.
- **Swedish-only UI (NFR-32):** all public and admin text is Swedish from day one. No English strings, no i18n framework.
- **GDPR:** no personnummer (NFR-23); personal data only in EU regions (NFR-06/24); no personal data in URLs (NFR-05).
- **Data integrity:** editing/deactivating reference data must never alter saved bookings (NFR-14); snapshots back this (NFR-15/16/33/34).
- **UI approach — functional first, polish last:** In Phases 1–5, build screens that are functional and readable, not polished. No animations, no pixel-perfect spacing, no mobile optimisation — just enough UI to drive and test the feature. Exception: the Phase 0 design system (tokens, base layout, logo component) is foundational and must be done properly — skipping it means inline styles everywhere and a worse retrofit later. Polish, responsiveness, and visual refinement happen in Phase 6 (UI/UX).

---

## What shipped, phases 0 – 11  ✅ all complete and merged

Collapsed on 2026-08-15. These phases were narrated at ~117 KB while they were being built; that
narrative is now **history, not truth**, and several parts of it had gone stale (the 2026-08
demo-feedback work removed `MealPreference`, `IsFullBoard`, `AttendsBreakfast`,
`GuestChoosesRooms`, per-participant room assignment and the payment flags that those sections
still described). Rather than maintain a record that contradicts the system, the detail is left
where it is durable: **`git log --follow docs/archive/implementation-plan.md`** holds every word of
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
| **10d** Cutover & launch | 2026-09-07 | **`akerogarden.se` went live on Azure.** Root `A` + `admin` CNAME at one.com, hostnames bound, managed SSL on both hosts, `www` 308→apex, base-URL flip, magic link re-verified end to end, test bookings cleared. Legal text published **without** a jurist review — that gate was cancelled, not met; see below and `docs/legal/known-conflicts-at-launch.md`. Outstanding work moved to Phase 11 | NFR-20–24 · residual go-live (reachability, managed SSL) |
| **11** Post-production hardening & handover | 2026-09-09 | **Everything the running system needed after go-live.** Audit-trail backend (FR-95/96/98) with a no-PII inspection; cost/health alerts and a portal dashboard; a Content-Security-Policy on both hosts (NFR-39); a post-launch architecture & security review — 26 findings, all resolved, closed or parked, closing all three of Phase 7's deferrals; the Bicep re-apply that ended eight weeks of production drift, plus `alerts.bicep`; FR-112 on-behalf-of bookings for guests with no email; the fluid-typography `px` audit; and the first automated guard for Swedish-only UI. **Handover documents written, not sent.** | FR-95,96,98, FR-112 · NFR-36,37,38,39 · operational maturity |
| **Demo feedback** *(not a phase)* | 2026-08-10 | Group-level meals, building-instead-of-room assignment, `Samordnare` role, guest-stated check-in/out times, two turnover days, payment tracking removed, deferred link issuance at 40 days. Authority: `docs/archive/demo-feedback-2026-08-05.md` | see that document |

---
## Phase 10d — Cutover & launch  ✅ closed 2026-09-07

**`akerogarden.se` is live on Azure.** Collapsed the day it closed, for the same reason as the
phases above: the section had grown to ~50 lines describing a pre-launch state — draft banners,
`[ATT BEKRÄFTA]` placeholders, a pending questionnaire, a jurist review — none of which exists any
more. Keeping it would have left the plan contradicting the running system, which is the failure
mode this document has already had once.

**What shipped** is the table row above. **The cutover record** — every DNS value, the certificate
state, and four corrections to the runbook that the live cutover disproved — is in
`infra/ops/dns-cutover-runbook.md` under "Production cutover — 2026-09-07". Read that before any
future DNS work.

> ### ⚠️ One exit criterion was not met, and could not be
>
> The phase required *"the privacy policy and booking terms **jurist-reviewed**, gap-free and
> live"*. They are gap-free and live. **They were never jurist-reviewed, and will not be** — the
> client did not commission the review, and on 2026-09-07 the decision was taken to launch without
> it, with the consequences stated and accepted.
>
> This is recorded as **unmet**, not quietly ticked. The evidence, the eight client questions still
> unanswered, and the divergence between the published terms (§7, a percentage-of-value cancellation
> scale) and the emails (still the SRS's 20% / two-month deposit rule) are all in
> **`docs/legal/known-conflicts-at-launch.md`**, which is the authority on what was knowingly
> carried across the line. Anything that reads as "the legal text is signed off" is wrong.

> **Migration discipline is now IN FORCE.** The pre-go-live exception described at the top of this
> file expired at this cutover. Production holds real guest data from 2026-09-07: every schema
> change is an incremental migration and the history is never rewritten again. The
> drop-and-recreate warning that used to sit here applied to a *regenerated baseline* and is spent
> — an incremental migration applies cleanly in place, which is the whole point of freezing the
> baseline.

---

## Phase 11 — Post-production hardening & handover  ✅ closed 2026-09-09

**What shipped** is the table row above. Collapsed the day it closed, for the same reason as every
phase before it: the section had grown past 100 lines and had already been caught asserting three
things its own neighbouring rows contradicted — that Search Console was unverified, that FR-97 was a
Phase 11 criterion the SRS already owned, and that `alerts.bicep` was unapplied three paragraphs
below the entry recording its application. A narrative accumulates claims faster than anyone
re-reads them; a table row cannot.

**Where the reasoning went**, since none of it is repeated here:

| What | Read instead |
|---|---|
| The Bicep re-apply, and what a future apply must not repeat | the banner at the top of `infra/azure/main.bicep`, then `infra/azure/README.md` |
| The security review — 26 findings, and its own remediation tracker | `docs/review/post-launch-security-review.md` |
| On-behalf-of bookings for guests with no email (FR-112) | `requirements.md` FR-112, `erd.md`, `booking-lifecycle-timeline.md` |
| The `px` audit, and the two values deliberately left in `px` | `design.md` under "Text scaling" |
| Swedish-only, now guarded | `.claude/CLAUDE.md`, critical rules |

> ### Two things were closed by decision rather than by code — both deliberately
>
> **FR-111's residue.** An admin-created booking for a guest who *does* have an email gets no
> acknowledgement at creation, so the terms reach them with the approval mail rather than before it.
> This is prevented by the admin's first question — does the guest have an email they read, could
> they open a confirmation and fill in a form — which now appears **on `/bokningar/ny` itself**, not
> only in the handover manual, because a control nobody is looking at is not a control. A process
> control is weaker than a system one: if manual bookings for email-capable guests become routine,
> revisit it.
>
> **WebKit at large text is unverifiable with this harness.** Playwright exposes no way to set its
> default font size and the CDP call Chromium uses does not exist there, so 200% would need a real
> Safari driven outside Playwright. Accepted, not outstanding — the engine table in `design.md` says
> which engine covers what.

> **What remains is not engineering, and is deliberately not a phase criterion.** Sending the
> handover, decommissioning the WordPress hosting (**the web hosting only** — the DNS zone, the
> domain registration and every mail record stay, or every email the app sends dies silently), and
> the legal follow-up, which is blocked on the client and cannot gate a phase. The published terms
> §7 still contradicts the deposit rule the approval and cancellation emails state; that divergence
> is **SRS-vs-contract, not code-vs-SRS**, and `docs/legal/known-conflicts-at-launch.md` is the
> authority on it.


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
