# Åkerögården — Implementation Plan

**Based on:** requirements.md v0.35, erd.md
**Audience:** the developer building v1.
**Purpose:** a dependency-ordered, phased path from empty repo to production launch. Each phase lists its **goal**, **scope & deliverables**, the **requirements it satisfies**, and **exit criteria** (what "done" means before moving on).

---

## How to use this plan

- Phases are **dependency-ordered** — each builds on the one before. Don't start a phase until the prior phase's exit criteria are met.
- Every requirement ID (FR-xx / NFR-xx) traces back to requirements.md. If a phase seems to conflict with the SRS, the **SRS wins** — raise it, don't silently diverge.
- The data model is fixed in requirements.md §8 and visualised in erd.md. **Add EF Core migrations incrementally**, introducing each entity in the phase that first needs it (noted per phase).
- **Tombstoned requirements need no work:** FR-12, FR-41, FR-43 (merged/removed), and FR-46 (dropped). They're listed in the SRS only to keep numbering stable.

### Cross-cutting concerns (apply in every phase, not a separate phase)

- **Testing (NFR-26–28):** unit tests for business logic (pricing tiers, retention/deletion dates, token expiry, meal/participant validation) and integration tests for every API endpoint. CI fails the build on any test failure. Write tests *with* each feature, not after.
- **Swedish-only UI (NFR-32):** all public and admin text is Swedish from day one. No English strings, no i18n framework.
- **GDPR:** no personnummer (NFR-23); personal data only in EU regions (NFR-06/24); no personal data in URLs (NFR-05).
- **Data integrity:** editing/deactivating reference data must never alter saved bookings (NFR-14); snapshots back this (NFR-15/16/33/34).
- **UI approach — functional first, polish last:** In Phases 1–5, build screens that are functional and readable, not polished. No animations, no pixel-perfect spacing, no mobile optimisation — just enough UI to drive and test the feature. Exception: the Phase 0 design system (tokens, base layout, logo component) is foundational and must be done properly — skipping it means inline styles everywhere and a worse retrofit later. Polish, responsiveness, and visual refinement happen in Phase 6.

---

## Phase 0 — Foundation ✅ COMPLETE (merged to main 2026-06-29)

**Goal:** a deployable skeleton an admin can log into, with infrastructure, CI/CD, and the design system in place.

**Scope & deliverables:**

- .NET 10 Blazor solution scaffolded with the two render modes: **Static SSR** for public pages, **Interactive Server** for the admin panel (§1.5).
- EF Core + PostgreSQL wired up; initial empty migration; connection via VNet integration.
- **ASP.NET Core Identity** for admin auth: login at `admin.akerogarden.se/login`, "remember me" 30-day secure HTTP-only cookie, password reset (1-hour link), admin account create/deactivate (no self-deactivation).
- Admin panel served on the `admin.akerogarden.se` subdomain; unauthenticated admin routes redirect to login.
- **Design system:** tokens, base layout, key CSS classes, logo component per design.md (`wwwroot/css`, `Logo.razor`, scroll-aware header via JS/CSS).
- **Local dev environment (§11.1):** Dockerised PostgreSQL, Redis, and Azurite (Blob emulator) plus a local SMTP catcher (e.g. Mailpit) so the Mac mirrors prod services at no cost; `dotnet user-secrets` for local config.
- **Bicep templates written and checked in (early Phase 0 — no Azure spend yet):** `.bicep` files declare every resource (App Service, PostgreSQL Flexible Server, Blob Storage, Key Vault, managed identity, managed SSL, custom domains) with environment-specific parameter files for production and staging. Templates are committed before any resource is provisioned — if it's not in Bicep, it doesn't exist. *(Redis is deferred to Phase 3, where rate limiting is first built — it is not part of the Phase 0 templates.)*
- **CI/CD (early Phase 0):** GitHub Actions build + test + deploy pipeline wired up; test projects (unit + integration) created and running against ephemeral containers in CI. The deploy job exists but targets nothing until Azure is provisioned.
- **Staging Azure infra provisioned (end of Phase 0 — once admin auth works locally):** Run the Bicep templates to create the staging resource group (App Service B1, PostgreSQL Flexible Server VNet-only, Blob Storage, Key Vault at `staging.akerogarden.se`, synthetic data only, test SMTP inbox, powered down when idle). Validate VNet isolation and run the first real deploy through the pipeline. *(Production provisioning is deliberately deferred to Phase 6.)*
- **Secrets:** SMTP and connection strings as encrypted app settings / Key Vault — never in source.

**Satisfies:** FR-35, FR-70–74 · NFR-01, NFR-02, NFR-06, NFR-11, NFR-12, NFR-24, NFR-25, NFR-28, NFR-30, NFR-32 · §1.5 technology decisions.

**Exit criteria:** admin can log in / reset password / manage admin accounts over HTTPS; CI is green and runs the test suite; the local dev stack runs via Docker; **staging** Azure environment is provisioned and the first deploy runs through the pipeline with the DB unreachable from the public internet; design tokens applied to a base layout. *(Production provisioning is deliberately deferred to Phase 6. Staging runs on the Azure default `*.azurewebsites.net` hostname for now; binding the `staging.akerogarden.se` custom subdomain + managed SSL is also deferred to Phase 6, where DNS for all domains is configured.)*

---

## Phase 1 — Reference data + admin CRUD ✅ COMPLETE (merged to main 2026-07-01)

**Goal:** the admin can enter and manage all the "reference data" that bookings will later point at.

**Scope & deliverables:**

- EF Core entities + migrations for: **Building, Room, MealOption, Facility, PriceRule, CleaningZone** (erd.md).
- Admin CRUD screens (Interactive Server) for each, with: create, edit, deactivate (soft), display-order control, and **block on hard-delete when referenced**.
- **Facility image upload** to Blob: JPEG/PNG/WebP, max 5 MB, inline validation error.
- **CleaningZone** bundle support (`IncludesZoneIds`, e.g. Hela lägret) and the mutual-exclusion behaviour it drives later.
- First-setup data entry: the property's current buildings/rooms/meals/facilities/prices/cleaning zones can be fully populated.

**Satisfies:** FR-13–16 (buildings), FR-17–20 (rooms), FR-22–26 (meals), FR-27–30 (facilities), FR-31–34 (prices), FR-85–88 (cleaning zones) · NFR-09, NFR-14.

**Exit criteria:** every reference entity is fully CRUD-able with validation; deactivation hides items from future selection without touching history; hard-delete is blocked when referenced; image upload enforces format/size; unit tests cover the deactivate / no-hard-delete rules.

### Build order — five vertical slices

Phase 1 is built as five independently reviewable vertical slices, not one batch. Each slice = entity + EF Core migration + Interactive Server CRUD screen (create / edit / deactivate / display-order) + its hard-delete guard + unit tests, merged to the phase branch before the next begins. The only intra-phase dependency is **Room → Building**; the remaining entities are independent and ordered simplest-first so the CRUD/soft-delete/guard pattern established in slice 1 is proven before the two slices with novel mechanics (CleaningZone's array bundle, Facility's Blob upload).

| Slice | Migration | Entities | §ref | Notes |
|---|---|---|---|---|
| 1 | `AddBuildingAndRoom` | Building, Room | §8.2, §8.3 | Room FK → Building; establishes the reusable CRUD / soft-delete / display-order / guard pattern |
| 2 | `AddMealOption` | MealOption | §8.7 | Simplest standalone; reuses slice-1 pattern with no new mechanics |
| 3 | `AddPriceRule` | PriceRule | §8.9 | Standalone, no FKs; adds optional season-date fields |
| 4 | `AddCleaningZone` | CleaningZone | §8.11 | Introduces the `uuid[]` self-referential bundle (`IncludesZoneIds`) |
| 5 | `AddFacility` | Facility | §8.8 | Only slice touching Blob (Azurite locally); confirm storage wiring before starting |

### Phase 1 implementation notes

- **Hard-delete guard — unit-tested now, wired to real references later.** Every "block on hard-delete when referenced" rule (FR-16/20/26/30/88) targets entities that do not exist until Phase 3–4: Facility ← `BookingFacilitySelection` (§8.13) and CleaningZone ← `BookingCleaningSelection` (§8.12) arrive in Phase 3; MealOption ← `MealPreference` (§8.6) and Room ← `Participant.RoomId` (§8.5) arrive in Phase 4. Phase 1 implements a per-entity `CanHardDelete` check unit-tested against a stubbed reference-count source; the real reference counts are wired in as those tables land. **Building's guard is two-layered:** in Phase 1 it blocks deleting a building that still has rooms; the booking-transitive check (FR-16, "room assignments on any booking", via Room → `Participant.RoomId`) is added in Phase 4. Deactivation (soft) is fully implemented and tested in Phase 1.
- **CleaningZone bundle scope (slice 4).** `IncludesZoneIds` is a PostgreSQL `uuid[]` column (Npgsql), not a foreign key — so there is no DB constraint and validation lives in the app layer. Phase 1 stores the array and validates in the admin CRUD that included IDs reference existing active zones and that a bundle does not include another bundle. The mutual-exclusion *checkbox behaviour* on the inquiry form is a Phase 3 concern (it lives on the public form), not Phase 1.
- **No computed totals on CRUD screens (C8).** `AmountExVat` and `VatRate` on PriceRule and CleaningZone are stored and displayed exactly as entered. The admin edit screens must not render any derived "total incl. VAT" figure — that would violate the no-monetary-totals rule.

---

## Phase 2 — Public site ✅ COMPLETE (merged to main 2026-07-01)

**Goal:** the public-facing website, rendered dynamically from Phase 1 data.

**Scope & deliverables:**

- Static SSR pages: **home, about, contact** (hardcoded text/images — no admin CMS), **facilities** (FR-81), **menu** (FR-83), **prices** (FR-82, including cleaning zones).
- **Availability calculator (logic only, no page):** a pure, unit-tested component that computes a date's state (Free / Partially booked / Fully booked) from **effective capacity** — total active-room bed count (Phase 1 data, live) minus beds removed by blocks — against summed occupying-guest counts. Built and fully unit-tested here against directly supplied inputs; the visible calendar **page** that consumes it is deferred to Phase 3 (see note).
- **Privacy policy** page, linked from footer and (later) the inquiry form.
- Responsive (mobile-first) across all pages.

> **Calendar split (plan deviation from the original single-phase calendar — logged here, not silently diverged):** the calendar's occupancy inputs (bookings + blocks) do not exist until Phase 3, and the date-range → inquiry-form pre-fill (FR-11) targets a form that also does not exist until Phase 3. Building the *page* against stubbed/fake occupancy in Phase 2 would prove only that a grid renders (routine) while risking a fictional-availability page reaching staging, and can never be verified end-to-end until Phase 3. Therefore the feature is split: the **effective-capacity + state computation** — the only intellectually risky part, and largely property-shaped (bed counts, block rules) rather than booking-shaped — is built and unit-tested **here** in Phase 2; the **calendar page** (12 rolling months, no back nav, three-state display, disclaimer note, date-range pre-fill) is built in **Phase 3**, wired to real bookings/blocks via the already-proven calculator and verified end-to-end. FR-08, FR-09, FR-10, FR-10a, FR-11, FR-84 and NFR-07 accordingly move to Phase 3's Satisfies row.

**Satisfies:** FR-01 (availability-calendar and inquiry pages complete in Phase 3), FR-07, FR-81, FR-82, FR-83 · NFR-08, NFR-17, NFR-20, NFR-21 (form ack lands in Phase 3), NFR-22, NFR-23. *(The availability calculator built here is preparatory for FR-08–11/10a/84 and NFR-07, which are delivered in Phase 3.)*

**Exit criteria:** all public pages render from admin-managed data; site is responsive and public pages load within 3 s; the availability calculator's effective-capacity and state logic is unit-tested across the edge cases (exactly-full, capacity-minus-one, whole-property block, partial block, empty). *(The visible calendar page and its end-to-end verification against real bookings/blocks are Phase 3 exit criteria.)*

---

## Phase 3 — Inquiry & booking lifecycle

**Goal:** the core booking engine — from inquiry submission through the admin's approve / deny / close / cancel decisions.

**Scope & deliverables:**

- **Booking** and **BlockedPeriod** entities + migrations.
- **Inquiry forms:** stay request (incl. meal plan, day guests hidden when SelfCateringAll, facility multi-select, cleaning-zone checklist with bundle mutual-exclusion) and general inquiry; privacy-policy acknowledgement on submit.
- **Email infrastructure** (MailKit/SMTP) with logged retries that don't fail the booking; auto-acknowledgement (per type) and admin notification.
- **Rate limiting** on the inquiry endpoint (5 / IP / 10 min) backed by Redis.
- **Admin booking management:** list with filters, calendar view, internal notes, deposit/payment status tracking, manual booking creation on behalf of a guest.
- **Status state machine (§2.5) enforced** — invalid transitions blocked. Approve (+ token generation, + ≤14-day warning), deny (+ required reason), close (general inquiry only), cancel (Approved/DetailsComplete/Active; type + optional reason; conditional deposit email).
- **Blocking:** create/edit/delete blocks at whole-property / building / room scope.
- **Public availability calendar page (Static SSR):** 12 rolling months forward, no backward navigation (FR-84); three-state display (Free / Partially booked / Fully booked) with the FR-10a disclaimer note; exposes only aggregate availability, no personal data (NFR-07); date-range selection pre-fills the inquiry form (FR-11). Built on Phase 2's already-unit-tested effective-capacity calculator, now wired to real bookings + blocks and verified end-to-end. *(Deferred here from Phase 2 — see the Phase 2 "Calendar split" note.)*
- Emails: inquiry acknowledgement (×2), admin new-inquiry, denial, approval + magic link, cancellation.

**Satisfies:** FR-02–06, FR-08, FR-09, FR-10, FR-10a, FR-11, FR-21, FR-36–42, FR-47, FR-50, FR-51–53, FR-75–80, FR-84 · NFR-03, NFR-04, NFR-05, NFR-07, NFR-09, NFR-13. *(FR-08–11/10a/84 and NFR-07 moved here from Phase 2 with the calendar page — see the Phase 2 "Calendar split" note.)*

**Exit criteria:** an inquiry flows to a Pending booking with correct emails; the state machine blocks illegal transitions; approve/deny/close/cancel behave per spec with correct conditional emails; rate limiting holds; the public calendar page shows correct states from real bookings/blocks, exposes no personal data, and a date-range selection pre-fills the inquiry form; integration tests cover the inquiry, approval, and denial endpoints.

### Build order — six vertical slices

Like Phase 1, Phase 3 is built as independently reviewable vertical slices, each merged to the phase branch (with its tests) before the next begins. The ordering is dependency-driven: the data foundation and pure state machine come first; email infrastructure next (the inquiry flow needs it); the public inquiry form + Redis rate limiting follow; then the admin management and blocking; the **public calendar page is deliberately last** so it is verified end-to-end against bookings that actually reached an occupying status (`Approved`+, slice 4) and real blocks (slice 5), per the Phase 2 "Calendar split" rationale.

| Slice | Migration | Delivers | Key §/FR refs |
|---|---|---|---|
| 1 | `AddBookingLifecycleEntities` | `Booking`, `BlockedPeriod`, `BookingFacilitySelection`, `BookingCleaningSelection` entities; string enums; pure `BookingStateMachine` (§2.5), `RetentionCalculator` (FR-91), `CompletionTokenPolicy` (§2.6); wire Facility/CleaningZone hard-delete guards to real selection counts | §8.1/8.10/8.12/8.13, §2.5, §2.6, FR-30/88, FR-91, NFR-03 |
| 2 | — | Email infrastructure: logged SMTP retry (NFR-13); `BookingEmailService` composing the §9 Swedish templates (2 acknowledgements, admin new-inquiry, denial, approval + magic link, cancellation) | §9, FR-05/06, NFR-13 |
| 3 | — | Public inquiry forms `/boka` (stay request + general inquiry, Static SSR, server-authoritative conditional UI); privacy-policy acknowledgement; selection snapshots; ack + admin emails on submit; Redis-backed rate limiting (5/IP/10 min) **and its Bicep addition** (Azure Cache for Redis, both parameter files) | FR-02–06, FR-21, NFR-04/05, §8.12/8.13 |
| 4 | — | Admin booking management (Interactive Server): list with filters, detail, internal notes, deposit/payment flags, state-machine actions (approve + token gen + FR-75 warning, deny + reason, close, cancel + type/reason/conditional email), manual booking creation, admin calendar view | FR-36–42, FR-47, FR-49–53, FR-75–80 |
| 5 | — | Blocking: `BlockedPeriodService` + admin CRUD at whole-property / building / room scope | FR-21, FR-40, §3.3 |
| 6 | — | Public availability calendar page (Static SSR): EF-backed occupancy provider feeding the **existing** Phase 2 `AvailabilityCalculator`; 12 rolling months, no back-nav, three states + FR-10a disclaimer, aggregate-only (NFR-07); date-range pre-fills `/boka` (FR-11) | FR-08–11, FR-10a, FR-84, NFR-07 |

Only slice 1 introduces a migration — all four lifecycle entities land together, so a single `AddBookingLifecycleEntities` migration covers them (rather than splitting artificially within one slice). Slices 2–6 add no new tables. `BlockedPeriod.BuildingId`/`RoomId` are logical references validated in the service layer, not enforced foreign keys (mirroring `CleaningZone.IncludesZoneIds`), so a stale block never blocks a building/room hard-delete at the DB level.

---

## Phase 4 — Completion flow

**Goal:** approved guests complete their booking details via a magic link, with no account.

**Scope & deliverables:**

- **Participant, MealPreference, BookingCleaningSelection, BookingFacilitySelection** entities + migrations, with snapshot fields populated on save (NFR-15/16/33/34).
- **Token-authenticated completion page** (URL token is the sole auth; single-use; expiry per §2.6 with the 48-hour floor; admin resend/regenerate while `Approved`).
- **Completion form:**
  - Per overnight participant: name, age category, full-board override, **meals per applicable day** (meals-by-day rule: arrival = dinner, middle = lunch+dinner, departure = none), dietary needs, and room assignment when `GuestChoosesRooms`.
  - Per day guest: name, age category, single attendance date, breakfast checkbox, lunch/dinner if attending, dietary needs.
  - "Same for all days" meal shortcut; org/invoice details.
- **Room assignment:** picker excludes blocked and already-occupied rooms via the live `RoomId`; admin panel flags room conflicts.
- On submit: snapshots captured, token invalidated, status → `DetailsComplete`, admin notified.
- **Admin edit** of participant data after submission (the only post-submission edit path — no guest reopen).

**Satisfies:** FR-48, FR-49, FR-54–62 · NFR-15, NFR-16, NFR-18, NFR-33, NFR-34.

**Exit criteria:** a guest completes a booking end-to-end from the email link; token single-use/expiry/regeneration all behave; snapshots are stored; room conflicts are detected; admin can correct details post-submission; unit tests cover token expiry and meal/participant validation, integration test covers the completion submission.

---

## Phase 5 — Notifications, lists & retention

**Goal:** the automated lifecycle — reminders, downloadable lists, and GDPR auto-deletion.

**Scope & deliverables:**

- **Scheduled background-job** infrastructure.
- **14-day reminder (FR-63):** standard reminder if `DetailsComplete`+; if still `Approved`, regenerate the token and send the guest a nudge with a fresh link **plus** an admin alert.
- **Thank-you email** on Completed (fixed text).
- **List downloads:** participant list and food-preference list, each in **PDF and Excel**, performant to 300 participants.
- **GDPR retention:** `DeleteAfter` computation for every state (FR-91); scheduled deletion of Completed (2 yr), Denied/Cancelled (12 mo), Pending (12 mo), Closed (12 mo); 30-day pre-deletion admin warning, de-duplicated via `DeletionWarningEmailSentAt`.

**Satisfies:** FR-44, FR-45, FR-63–69, FR-89–91 · NFR-10 · email catalogue rows: two-week reminder, completion nudge, admin-not-completed, thank-you, pre-deletion warning.

**Exit criteria:** the 14-day job sends the right variant per status; lists generate correctly within the 10 s / 300-participant target; the retention job deletes exactly the right records and the warning fires once each; unit tests cover retention/deletion-date computation.

---

## Phase 6 — Hardening & go-live

**Goal:** verify the non-functionals, clear the operational blockers, and launch.

**Scope & deliverables:**

- **Security:** full pass over NFR-01–07, NFR-25, NFR-30; confirm no PII in URLs/calendar; auth/rate-limit review. **Wire `ForwardedHeaders`** so the inquiry rate limiter (NFR-04) keys on the real client IP behind the App Service load balancer rather than `RemoteIpAddress` (deferred from Phase 3 — the distributed-store requirement is met there; accurate per-client IP is the hardening step).
- **Performance:** verify NFR-08 (public < 3 s), NFR-09 (admin < 5 s), NFR-10 (lists).
- **Reliability:** confirm backups (NFR-12) and run the **quarterly restore drill** (NFR-29); email failure handling (NFR-13).
- **Email E2E (NFR-31):** verify every guest/admin email against the **production** SMTP before go-live — not assumed from a successful build.
- **Operational blockers (from operational-tasks.md):** privacy-policy legal text drafted/reviewed; image migration completed and wired to Facility/page assets; **VAT rates per category confirmed with the client's accountant**.
- **UAT** with the admin **on the staging environment** (non-technical usability, NFR-18/19); Swedish-text review (NFR-32).
- **Public inquiry-form input polish (found during Phase 3 manual testing):** (a) stricter phone-number validation on the stay/general forms (currently the optional `[Phone]` check is lenient and accepts letters); (b) a safeguard against a mistyped guest email — a well-formed but wrong address is currently accepted silently (the admin still receives the inquiry via FR-06, but the guest never gets their acknowledgement and bounces are not surfaced in-app). Pragmatic fix: a "confirm email" second field on the public forms. Product decision on scope.
- **Production Azure environment provisioned:** run `main.bicep` with `prod.parameters.json` to create the production resource group (App Service B1, PostgreSQL Flexible Server VNet-only, Blob Storage, Redis, Key Vault); configure DNS for `akerogarden.se` and `admin.akerogarden.se`; run `ssl.bicep` for managed SSL; wire GitHub secrets for the production deploy job; validate VNet isolation.
- **Staging custom domain bound:** configure DNS for `staging.akerogarden.se` (CNAME + `asuid` TXT), re-add it to the staging `customHostnames`, redeploy `main.bicep`, and run `ssl.bicep` for the managed certificate. *(Deferred here from Phase 0, which ran staging on the Azure default hostname.)*
- **Production deploy** + go-live checklist.

**Satisfies:** NFR-08, NFR-09, NFR-10, NFR-29, NFR-31 · all residual NFRs · operational-tasks.md items.

**Exit criteria:** go-live checklist complete; production Azure environment provisioned and VNet isolation validated; backups proven restorable; all emails verified in production; privacy policy live and accurate; admin signed off via UAT; production reachable on both domains.

---

## Phase → primary requirements map (quick reference)

| Phase | Functional | Non-functional |
| --- | --- | --- |
| 0 Foundation | FR-35, 70–74 | NFR-01,02,06,11,12,24,25,28,30,32 |
| 1 Reference data | FR-13–34, 85–88 | NFR-09,14 |
| 2 Public site | FR-01,07,81–83 | NFR-08,17,20–23 |
| 3 Inquiry & lifecycle | FR-02–06,08–11,10a,21,36–42,47,50,51–53,75–80,84 | NFR-03,04,05,07,09,13 |
| 4 Completion | FR-48,49,54–62 | NFR-15,16,18,33,34 |
| 5 Notifications/lists/retention | FR-44,45,63–69,89–91 | NFR-10 |
| 6 Hardening & go-live | — | NFR-08,09,10,29,31 + residual |

*Testing (NFR-26–28) and Swedish UI (NFR-32) are cross-cutting and apply to every phase.*

---

*Derived from requirements.md v0.35 and erd.md — Åkerögården Booking System.*
