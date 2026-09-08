# Åkerögården — Implementation Plan

**Based on:** requirements.md, erd.md
**Audience:** the developer building v1.
**Purpose:** a dependency-ordered, phased path from empty repo to production launch. Each phase lists its **goal**, **scope & deliverables**, the **requirements it satisfies**, and **exit criteria** (what "done" means before moving on).

---

## How to use this plan

> ⚠️ **This document is nearly spent, and that is deliberate.** Everything up to and including
> **Phase 10d is finished** — the site went live on 2026-09-07 — and each of those sections was
> collapsed to a summary table row as it closed, because the narrative had started to *contradict*
> the system it described, which makes it worse than useless. **The only thing live here is
> Phase 11.**
>
> **For current truth always read `requirements.md`.** This file answers only "what order was
> it built in, and what is left". **When the Phase 11 items are done or moved to
> `requirements.md` §10, nothing live remains and this file should be DELETED**, not frozen —
> `git log --follow` is the archive, and `requirements-changelog.md` already exists for anything
> that genuinely needs a frozen home. That is now one phase away.
>
> **Phase 11 carries six items inherited from 10d** (listed first under "Still open"). The site is
> live, so several of them changed character on launch day rather than merely moving: the missing
> CSP now protects real visitors rather than staging, and the admin-created-booking gap now affects
> real guests. Do not treat them as leftovers.

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

## What shipped, phases 0 – 10d  ✅ all complete and merged

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
| **10d** Cutover & launch | 2026-09-07 | **`akerogarden.se` went live on Azure.** Root `A` + `admin` CNAME at one.com, hostnames bound, managed SSL on both hosts, `www` 308→apex, base-URL flip, magic link re-verified end to end, test bookings cleared. Legal text published **without** a jurist review — that gate was cancelled, not met; see below and `docs/legal/known-conflicts-at-launch.md`. Outstanding work moved to Phase 11 | NFR-20–24 · residual go-live (reachability, managed SSL) |
| **Demo feedback** *(not a phase)* | 2026-08-10 | Group-level meals, building-instead-of-room assignment, `Samordnare` role, guest-stated check-in/out times, two turnover days, payment tracking removed, deferred link issuance at 40 days. Authority: `docs/review/demo-feedback-2026-08-05.md` | see that document |

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
| `/kontakt` photo column stretching | ✅ 2026-08-19. `min-height: 100%` made the photo's height a by-product of the neighbouring column; the find-here map grew that column ~400px and the placeholder became a ~1000px empty slab, silently demanding a ~1:2.2 crop of the launch photo. Now `aspect-ratio: 4 / 5`, top-aligned. The crop is recorded in `design.md`'s imagery strategy and **approved by the client 2026-08-19** |

### Still open

> **Carried over from Phase 10d (2026-09-07).** The site went live with these outstanding. None of
> them blocked the cutover; all of them are real work that has not been done. They are listed first
> because they are the youngest and the most likely to be forgotten — the phase that owned them is
> closed.

- **🚧 BLOCKING (carried from 10d) — an admin-created booking obtains no acknowledgement of anything.** Surfaced by FR-111, which left a `null` where a value is expected and exposed a gap that has existed since FR-47 was built. A manual booking (phone or email) gets **no email at all at creation** — `CreateStayRequestAsync` skips both the guest acknowledgement and the admin notification when `createdByAdmin` is true. The guest's first contact is the **approval email**, which does link the terms (FR-109) and carries the policy in every footer. So they are not uninformed, but two things are wrong: the terms reach them **at the moment the contract forms rather than before it**, the weak form of incorporation and weakest against a consumer — and **now confirmed to matter**, since the client's own §1/§9/§31 establish that consumers do book; and GDPR art. 13 wants the information given **when the data is collected**, which for a phone booking is when the admin types it in. This was marked blocking for 10d and **shipped unresolved.**
  - **A — send the existing acknowledgement email for admin-created bookings too.** `SendInquiryAcknowledgementAsync` already exists and its footer already carries the policy; add the terms link. **Needs no client input** and changes the guest's actual position rather than only our records.
  - **B — an admin attestation checkbox** on the manual-booking form, stored with the timestamp and the attesting admin. It must be **its own field, never `TermsAcceptedAt`** — it records what an admin says they did, not what a guest did.
  - **A remains the recommendation**; they are not exclusive. B's deciding input is **question 5 of the client questionnaire** (how often guests book by phone or email), still unanswered.

- **Re-apply Bicep to close production drift (carried from 10d). ⚠️ Read this whole item before running anything.** An explicit 10d deliverable that never ran: `az deployment group list -g akerogarden-prod` shows **`main` last applied 2026-07-20** — only `ssl` ran at the cutover, so the hostname bindings and certificates created that day exist in Azure without being reconciled against the templates.
  - **Prerequisite ✅ done 2026-09-08: `prod.parameters.json` has been updated to post-cutover values.** It was still carrying the Phase 10c set — `publicHostnames`/`adminHostnames` **empty** and both base URLs pointing at `*.azurewebsites.net`. Re-applying with that file would have **silently reverted the cutover**, snapping every email link back to the Azure hostnames while leaving the bindings undeclared. The arrays now name the real domains, so `main.bicep`'s declared end state finally matches production.
  - **⚠️ Bicep is NOT in CI/CD.** `azure-pipelines-deploy-prod.yml` runs `AzureWebApp@1` twice — application code only, no infrastructure. Every Bicep apply is a manual `az deployment group create`. An earlier draft of this item said to "do it through the pipeline so the secrets come from the variable group"; **that path does not exist.**
  - **The remaining risk is the secrets, and it is a live-production risk.** Five values are passed at deploy time (`postgresAdminPassword`, `smtpUsername`, `smtpPassword`, `seedAdminPassword`, `seedAdminEmail`). Omitting or changing any of them **resets the Postgres admin password and overwrites the Key Vault SMTP secrets** — on a database now holding real guest data and a mail path guests depend on. They must be passed as **the values already in use**, read from `jmk-ag-prod-kv`, not regenerated. Reading them needs a Key Vault access-policy grant the current CLI identity does not have (`secrets list` → `Forbidden`).
  - **Worth deciding whether this is worth doing at all right now.** Production is healthy and the drift is narrow — declared-vs-actual on hostname bindings that already exist and work. The re-apply is a *verification* that the templates describe reality, and its cost is handling five live credentials against a running system. Reasonable options: do it during a quiet window with the KV grant in place; or defer it and rely on the updated parameters file being correct-by-inspection, accepting that "if it's not in code it doesn't exist" is then upheld by reading rather than by a green deployment. **Not a decision to take mid-week by reflex.**

- **Decommission the WordPress hosting (carried from 10d).** Still powered and unlinked since 2026-09-07, deliberately, for a soak. When retiring it: **the web hosting only.** The DNS zone, the domain registration and every mail record (`MX`, `SPF`, `DKIM`, `DMARC`) and mailbox stay — the app sends all guest and admin mail through One.com SMTP on this domain, and breaking those kills every email silently.

- **Test `dataskydd@akerogarden.se` end to end (carried from 10d).** The privacy policy is now **live and publicly names this address** in §1 and §6 as the route for data-protection questions and rights requests. Nobody has ever sent it a message. Send one and confirm a human is actually watching the inbox: the one-month response clock under art. 12.3 runs whether or not anyone reads it, and a missed rights request is a supervisory matter.

- **Re-invite the admin accounts (carried from 10d).** The production rebuild took every `AspNetUsers` row and **only the bootstrap admin survives.** Anyone else who needs the panel has to be invited again. Note the failure mode: a stale browser session from before the rebuild renders an empty-looking admin panel rather than an obvious auth error, so "the data is gone" is the symptom to expect and misread.

- **The legal follow-up (carried from 10d).** Eight client questions remain unanswered — the plain-language tick-box version is `docs/legal/klientfragor-2026-08-23.html` — and the published terms (§7, a percentage-of-value cancellation scale) contradict the approval and cancellation emails, which still state the SRS's 20% / two-month deposit rule (`BookingEmailTemplates.cs:287` and `:437-440`, mandated by §5.6 and FR-79). **The divergence is SRS-vs-contract, not code-vs-SRS** — the code is correct against its specification and the specification is behind the contract. Resolving it moves one rule across four places and **cannot complete until question 6 is answered**, because the client's §7 has no rule between 8 and 4 weeks. Full evidence in `docs/legal/known-conflicts-at-launch.md`.

- **Pinned-clock sweep over the remaining integration test files.** Carried forward from PR #42 (2026-08-10), which made `BookingService` take `TimeProvider` and gave six boundary tests `DbFixture.PinClock`/`TodayIn`. The other integration files were left on the wall clock. **Not a defect today** — their assertions are not about *distances* (the 40-day issue window, the 14-day expiry), which is the only thing that breaks at a midnight boundary. It becomes one the moment any of them starts asserting a distance. The rule itself is in CLAUDE.md ("the clock is an input, never an ambient fact"); this is the outstanding application of it. **If you pin, every date in that test must come from the pinned clock** — a pinned service with a wall-clock seed holds two disagreeing "now"s and is worse than not pinning, which is how slice 7 broke three files at once.
- **🚧 BLOCKING — Content-Security-Policy and the other response security headers.** The solution sends **none** today: no `Content-Security-Policy`, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` or `Permissions-Policy`. `app.UseHsts()` (production only) and antiforgery are the only header-level protections in place. This is marked blocking because **NFR-35 has no enforcement mechanism without it**: the requirement was widened on 2026-08-18 from "self-hosted fonts" to "no resource may load from a third-party host while a page renders", and right now that is upheld by convention plus a single E2E test on a single page (`Contact_page_loads_nothing_from_a_third_party`). A `default-src 'self'` makes it impossible to break by accident, on every page, instead of merely disapproved of. Found by the completion gate on the `/kontakt` map work — and missed by that gate's own first pass, which ran the grep and never reported the null result.
  - **It is not a live hole today**, which is why it did not gate the cutover: nothing third-party is currently requested, and that is asserted by test. It is defence in depth against the next edit, not a fix for a present leak. **That calculus changed on 2026-09-07** — the site now serves real guests on the real domain, so the next accidental third-party reference leaks live visitors' IPs rather than staging's. It is the oldest blocking item here and should go first.
  - **Expect the policy to be the work, not the middleware.** It has to be validated against **both** hosts: the public Static SSR app and the admin Interactive Server app, whose circuit needs `connect-src` for its WebSocket and whose reconnect UI must be checked for inline styles. Blazor's own assets are same-origin, but a policy that needs `'unsafe-inline'` for styles buys much less than it appears to — decide that consciously rather than discovering it under a broken page.
  - Once it ships, consider whether NFR-35 should name the CSP as its enforcement, so the requirement and the mechanism point at each other.

- **A print stylesheet.** The site ships none, so printing any page carries the header, footer, docked mobile CTA and the "Vill du boka?" card, and breaks pages wherever they happen to fall. Not caused by the find-here map — surfaced by its completion gate on 2026-08-19, deliberately deferred rather than fixed in that branch, since it is site-wide and unrelated. Two sizes to choose between: a minimal `@media print` block that hides the chrome, forces black-on-white and keeps the map and the definition lists off page breaks (~30 min, covers every page at once); or treating the three pages people actually print as documents — `/kontakt` for the directions, and `/integritetspolicy` and `/bokningsvillkor`, which are legal texts guests save as PDFs. Start with the minimal one; the document treatment is only worth it if someone asks for it.

- **Fluid-typography pass over `site.css`. — PARTLY DONE 2026-08-19; what is left is the systematic sweep.** The root font size, the shared chrome on both hosts, and every page-level overflow reachable at 150% text on a 360px screen were fixed in the same branch as the root-font-size change, because that change is what made them reachable: shipping it without them would have put a horizontal scrollbar on four public pages and `/konton`. Fixed there: `html { font-size: 100% }`; the header un-pinned from a fixed height; the two sticky-bar offsets split, measured and documented; `.site-footer__grid`, `.facts` and `.trust-strip__grid` given explicit track minimums; `.prose` and `.admin-topbar h1` given `overflow-wrap`; `.data-card__head` allowed to wrap; the `/boka` consent label made one flex item. Verified by measuring 81 public and 99 admin page/viewport/root combinations at 16/20/24px roots — all clean — and guarded by `TextScalingTests`. **What remains is the sweep this item was opened for:** walking *every* absolute `px` in `site.css` deliberately rather than only those that happened to break at 360px, and covering the viewport/root combinations not measured (between 360 and 768). Specifically still open, and measured rather than guessed:
  - **200% text (SC 1.4.4) is now clean and is the headline result.** The first pass verified 150% (a 24px root — as far as Chrome's own font-size control goes). Extending to 175% and 200% (28px and 32px, reachable via Firefox's text-only zoom) found the whole remaining family at once, and it had a single cause: **layout breakpoints written in `px` do not know how large the reader's text is.** Every media query that *adds a column* was moved to `em` — the header's inline-nav/hamburger switch, `.hero__grid`, `.trust-strip__grid`, `.usecards`, `.teaser`, `.steps`, `.site-footer__grid`, `.dash-grid`, `.about-grid`, `.fac-grid`, `.lodging-list`, `.menu-cols`, `.contact-grid`, `.cal-months`, `.form-row`, `.day__meals`, `.facts` — plus `overflow-wrap` on headings, `minmax(0, 1fr)` on the hero track, and `max-width: 100%` on buttons and `.segmented`. `em` in a media query tracks the browser's initial font size, so at a default root every value resolves to the pixel width it replaced and **nothing moves for a default visitor**. Final state: **540 public combinations — 9 pages × 12 widths (360–1600px) × 5 roots (100/125/150/175/200%) — with zero overflow.** A `px` pin can itself re-introduce the overflow a collapse was meant to prevent; that cost a re-measure to find, and is recorded in `design.md`.
  - **Admin was swept too:** 66 combinations at 175%/200% across 11 pages × 360/768/1280, on top of the 99 at 100–150%. `--admin-topbar-h` held the topbar's *measured* height on every one (the topbar runs 84 → 165px across the range), and nothing spilled. Two 200% overflows were found and one fixed (`.data-card__title` needs `overflow-wrap: anywhere` — a card title is often an email address, one long token with no break opportunity).
  - **`max-width` breakpoints were converted too**, on the same rule: the admin sidebar collapse (`860px` → `53.75em`), the table→cards swap (`640px` → `40em`), the Byggnader toolbar columns, `.prow__age`, and the calendar bar's bottom-docking. The admin sidebar's **fixed `236px` width** was the last text-derived `px` in the shell and is now `14.75rem`. Two `max-width` queries were deliberately left in `px` because they are not "does this fit" questions — `.mobile-cta` appearing at `640px`, and the `530px` find-here-map type sizes (art-direction values measured against the drawing's scale factor). *The `.mobile-cta` one is worth revisiting as a product decision: the docked CTA becomes more valuable at a large text size, since the header collapses to a hamburger.*
  - **Verified default-size behaviour is unchanged**, which the overflow sweep cannot see: every public grid's column count at a 16px root matches what the `px` breakpoints produced, and on admin the sidebar is still exactly 236px, still collapses at 860px, and the table→cards swap still happens at 640px.
  - **The dev database was reseeded (`DemoSeed:Reset`) and everything re-measured against real data — this found defects that an empty database had hidden, including one at 150%.** `/faciliteter` overflowed a 360px screen from a 24px root up, from two separate causes: `.lodging-list` carried a bare `1fr` (the flex half of that had been fixed before — `.lodging-item__body` already had `min-width: 0` — but the grid track had not), and `.lodging-item__tag` holds "Tillgänglighetsanpassad", a 23-character compound with no break opportunity. Admin surfaced four more once it had bookings: dashboard `.card` tracks, the `dt`/`dd` pairs in `.data-card__fields` ("Konferensbyggnad"), and `.data-card__title` / `__sub`, which hold guest email addresses. Final state, all against seeded data with the HTTP cache disabled: **540 public + 165 admin combinations, zero overflow.**
  - **`.detail-rail` was finally verified for real.** With seeded bookings the detail page has genuine sticky travel (229–1206px depending on text size), so the branch's core claim could be measured rather than inferred: the rail is *stuck* at its sticky offset and clears the topbar at 100 / 150 / 200% text, and that offset tracks the **measured** topbar exactly — 53.2→61.2, 79.3→91.3, 105.4→121.4, each +0.5rem.
  - **⚠️ The E2E reference seed is thinner than the demo seed, and that is a live blind spot.** `E2EFixture.SeedReferenceDataAsync` seeds "the minimum the public pages need", with short names. The `/faciliteter` guard in `TextScalingTests` was *green* while the page was in fact overflowing under realistic data — the defect was only found by measuring against `DemoDataSeeder` output. Any layout guard is only as strong as the fixture's longest string; worth seeding one deliberately long Swedish compound into the E2E reference data.
  - **🦊 A Firefox (and WebKit) pass is the biggest remaining hole, and the one worth doing first.** Everything above was measured in Chromium, driven over CDP — so **every "zero overflow" figure in this item means *zero overflow in Chromium*.** Two reasons that matters rather than being a formality: Chromium's own font-size control stops at 24px (150%), so the **200% that SC 1.4.4 names is reachable by a real user mainly through Firefox's *text-only* zoom** — the exact mode this work exists to serve, on the one engine never tested; and Gecko and WebKit differ from Blink precisely on the two mechanisms the fix leans on hardest, min-content track sizing via `minmax(0, 1fr)` and `overflow-wrap: anywhere` reducing min-content. Playwright already ships both engines, so the cheap version is running the existing `TextScalingTests` against them. The blocker is the harness, not the assertions: the root size is set over CDP today, which neither engine exposes — Firefox needs a profile pref (`font.size.variable.x-western`) instead.
  - **Pages unreachable from a dev database are covered by tests instead.** `.fac-grid`, `.lodging-list` and `.menu-cols` render empty without seeded reference data, and `/slutfor` needs a live token — so all four are asserted at 200% in `TextScalingTests`, against the E2E fixture's seeded data, rather than left unverified.

  The rules the first pass produced are in `design.md` under "Text scaling". Original note follows. Surfaced 2026-08-19 while fixing the absolute root font size (`html { font-size: 16px }`), which overrode the reader's own browser text-size setting site-wide. That fix is done; **it is not the whole job.** Making the root scale only reveals which *other* values don't: the stylesheet still carries fixed-`px` sizes that stay put while everything around them grows, and a layout is only as scalable as its least scalable value. The pass is: find every absolute `px` in `site.css`, decide case by case whether it is *text-derived* (must become `rem` — anything sized by or sized to fit type, and any layout anchor standing in for a rendered text height) or *genuinely physical* (may stay `px` — hairline borders, radii, shadow offsets, and raster/vector artwork like `.site-header__logo .logo`, pinned at 55px, which is an image and not text). Then verify at 100/125/150% browser text size across both hosts — **measure the composited page at the narrowest breakpoint**, since that is where every defect in this class has hidden so far. Related: the header was hardcoded to a 72px height and its own CTA button spilled out of the bar at a 24px root; that class of bug is what the sweep is looking for. The principle is already written down and already applied once: `design.md`'s find-here-map entry states that *"the reader's text size is an input"* and records why the map's labels are in `rem` — in `px` the map both ignored a raised root **and shrank**, because `.container`'s padding is in `rem`. That entry also records the trap for verifying this: **raising the root font size from inside a Playwright test does not take effect against this app and the cause was never found**, so the map's guard test asserts the *unit* rather than the behaviour. Any verification here must drive the text size from outside the page (CDP or browser settings), not by injecting it. **Not a live defect** — nothing is broken at default settings — but it is the difference between honouring the setting and merely not overriding it — **WCAG 2.1 SC 1.4.4 “Resize text” (AA)**, and so inside NFR-38's scope, though NFR-38's body does not currently name it.

- **Architecture and security review.** A second, post-launch-oriented pass distinct from Phase 7's pre-launch `security-review.md` (which was written before production existed and before any real guest PII flowed through the system). Re-examine the architecture now that it's running for real: production VNet isolation as actually configured (not just declared in Bicep), Key Vault/managed-identity wiring, DataProtection key durability under real restart/scale events, rate-limiter behaviour under real traffic, and whether any Phase 0–10 "accepted decision" (e.g. fail-open rate limiting, NIST-aligned password-reset-same-password) still holds given real usage patterns.
- **Technical SEO & social sharing.** `sitemap.xml`, `robots.txt`, per-page meta descriptions, Open Graph/Twitter Card tags so a shared `akerogarden.se` link renders a proper preview (title/description/image), and Google Search Console domain verification. Explicitly **no analytics** — the privacy policy's "no consent banner needed" position (Phase 10c) stays true; adding analytics would reopen that question and is out of scope here unless separately decided. *(The cookie assessment lives with the privacy policy, delivered in 10d and live since 2026-09-07.)*
- **Client delivery & handover documentation (Swedish).** A short, plain-language admin manual covering the day-to-day admin workflows (approve/deny a booking, resend/remove an invite, generate lists, read the dashboard) — not a developer doc. Paired with an **email-accounts inventory**: every mailbox/account involved in the system's operation (`info@akerogarden.se` guest-facing address, the SMTP sending account and its credentials location, the admin notification recipient(s), the retired staging-only `temp@amaeli.se` test mailbox, and **`dataskydd@akerogarden.se`, which is live and verified sending/receiving on 2026-09-08** — a real inbox, not an alias. **Monitoring it is the client's responsibility and they must be told so explicitly**, because the privacy policy now publicly names it as the route for rights requests and art. 12.3's one-month response clock runs whether or not anyone opens it) so the client knows what exists, where credentials live, and what each address is for.

**Satisfies:** operational maturity beyond the go-live NFR set — plus the new audit-trail obligations **FR-95, FR-96, FR-98 and NFR-36/37** (FR-97, the admin view, is deferred). Otherwise no new FR/NFR obligations, except the confirm-dialog item which reinforces the existing hard-delete guard rules (FR-16/20/26/30/88), and the Byggnader tree rework which extends admin-mobile usability (NFR-17) beyond its current documented floor for one specific page.

**Exit criteria:** cost/health alerts live on production (deliberately production-only for this pass — staging is powered down when idle and lower-stakes; a staging alerts pass can follow separately if wanted); a documented logging/diagnostics story exists; every admin hard-delete path requires confirmation; the test-suite honesty audit is complete with findings resolved or explicitly logged; a Content-Security-Policy and the accompanying security headers are live on both hosts and verified against the admin circuit (**blocking**); the post-launch architecture/security review is complete with findings resolved or explicitly logged; the fluid-typography pass over `site.css` is complete, with every remaining `px` value either converted to `rem` or consciously justified as physical, and verified at 100/125/150% text size on both hosts; sitemap/robots/OG tags/Search Console are live; the Byggnader page is rebuilt as a unified Physical Building → Building → Room tree with the approved mobile layout (✅ done, pending the completion gate before merge to main); the audit-trail backend (FR-95/96/98, NFR-36/37 — slices A–C) is built, tested, and verified to hold no guest PII (✅ the no-PII inspection ran 2026-07-22 and passed; FR-97 admin view explicitly deferred); the client has the admin manual and the email-accounts inventory in hand.

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
