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

- **Testing (NFR-26–28):** three tiers — **unit** tests for business logic (pricing tiers, retention/deletion dates, token expiry, meal/participant validation); **integration** tests for every API endpoint against ephemeral containers; and **end-to-end / UI** tests (Playwright for .NET — `Microsoft.Playwright`, introduced in Phase 6) covering key browser journeys, accessibility (axe-core), and responsive reachability at 360/768/1280. **No pixel/visual-regression diffing** — it is flaky across OS/font rendering (local macOS ↔ CI Linux) and high-maintenance; assert behaviour and structure instead. CI fails the build on any test failure. Write tests *with* each feature, not after.
- **Swedish-only UI (NFR-32):** all public and admin text is Swedish from day one. No English strings, no i18n framework.
- **GDPR:** no personnummer (NFR-23); personal data only in EU regions (NFR-06/24); no personal data in URLs (NFR-05).
- **Data integrity:** editing/deactivating reference data must never alter saved bookings (NFR-14); snapshots back this (NFR-15/16/33/34).
- **UI approach — functional first, polish last:** In Phases 1–5, build screens that are functional and readable, not polished. No animations, no pixel-perfect spacing, no mobile optimisation — just enough UI to drive and test the feature. Exception: the Phase 0 design system (tokens, base layout, logo component) is foundational and must be done properly — skipping it means inline styles everywhere and a worse retrofit later. Polish, responsiveness, and visual refinement happen in Phase 6 (UI/UX).

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
- **Staging Azure infra provisioned (end of Phase 0 — once admin auth works locally):** Run the Bicep templates to create the staging resource group (App Service B1, PostgreSQL Flexible Server VNet-only, Blob Storage, Key Vault at `staging.akerogarden.se`, synthetic data only, test SMTP inbox, powered down when idle). Validate VNet isolation and run the first real deploy through the pipeline. *(Production provisioning is deliberately deferred to Phase 7.)*
- **Secrets:** SMTP and connection strings as encrypted app settings / Key Vault — never in source.

**Satisfies:** FR-35, FR-70–74 · NFR-01, NFR-02, NFR-06, NFR-11, NFR-12, NFR-24, NFR-25, NFR-28, NFR-30, NFR-32 · §1.5 technology decisions.

**Exit criteria:** admin can log in / reset password / manage admin accounts over HTTPS; CI is green and runs the test suite; the local dev stack runs via Docker; **staging** Azure environment is provisioned and the first deploy runs through the pipeline with the DB unreachable from the public internet; design tokens applied to a base layout. *(Production provisioning is deliberately deferred to Phase 7. Staging runs on the Azure default `*.azurewebsites.net` hostname for now; binding the `staging.akerogarden.se` custom subdomain + managed SSL is also deferred to Phase 7, where DNS for all domains is configured.)*

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

## Phase 3 — Inquiry & booking lifecycle ✅ COMPLETE (merged to main 2026-07-02)

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
| 3 | — | Public inquiry forms `/boka` (stay request + general inquiry, Static SSR, server-authoritative conditional UI); privacy-policy acknowledgement; selection snapshots; ack + admin emails on submit; Redis-backed rate limiting (5/IP/10 min) **and its Bicep addition** (Azure Managed Redis — Balanced B0; classic Azure Cache for Redis was retired mid-phase, both parameter files) | FR-02–06, FR-21, NFR-04/05, §8.12/8.13 |
| 4 | — | Admin booking management (Interactive Server): list with filters, detail, internal notes, deposit/payment flags, state-machine actions (approve + token gen + FR-75 warning, deny + reason, close, cancel + type/reason/conditional email), manual booking creation, admin calendar view | FR-36–42, FR-47, FR-49–53, FR-75–80 |
| 5 | — | Blocking: `BlockedPeriodService` + admin CRUD at whole-property / building / room scope | FR-21, FR-40, §3.3 |
| 6 | — | Public availability calendar page (Static SSR): EF-backed occupancy provider feeding the **existing** Phase 2 `AvailabilityCalculator`; 12 rolling months, no back-nav, three states + FR-10a disclaimer, aggregate-only (NFR-07); date-range pre-fills `/boka` (FR-11) | FR-08–11, FR-10a, FR-84, NFR-07 |

Only slice 1 introduces a migration — all four lifecycle entities land together, so a single `AddBookingLifecycleEntities` migration covers them (rather than splitting artificially within one slice). Slices 2–6 add no new tables. `BlockedPeriod.BuildingId`/`RoomId` are logical references validated in the service layer, not enforced foreign keys (mirroring `CleaningZone.IncludesZoneIds`), so a stale block never blocks a building/room hard-delete at the DB level.

---

## Phase 4 — Completion flow ✅ COMPLETE (merged to main 2026-07-04)

**Goal:** approved guests complete their booking details via a magic link, with no account.

**Scope & deliverables:**

- **Participant and MealPreference** entities + migration, with snapshot fields populated on save (NFR-15/16). New fixed-value enums: `AgeCategory` (`Adult`/`Child`/`Infant`), `ParticipantType` (`Overnight`/`DayGuest`), `DietaryRequirement` (§4.6, multi-select) plus free-text `DietaryNotes` (≤200 chars). *(The two selection entities `BookingFacilitySelection`/`BookingCleaningSelection` already landed in Phase 3 slice 1 — they are not Phase 4 work; the earlier draft of this section listed all four, which was stale.)*
- **Token-authenticated completion page** (URL token is the sole auth; single-use; expiry per §2.6 with the 48-hour floor; admin resend/regenerate while `Approved`). `CompletionTokenPolicy`, token generation on approval, the approval email link to `/slutfor/{token}`, and admin `RegenerateCompletionLinkAsync` were all built in Phase 3 — Phase 4 **consumes** them and builds the page they point at.
- **Completion form:**
  - Per overnight participant: name, age category, full-board override, **meals per applicable day** (meals-by-day rule: arrival = dinner, middle = lunch+dinner, departure = none), dietary needs, and room assignment when `GuestChoosesRooms`.
  - Per day guest: name, age category, single attendance date, breakfast checkbox, lunch/dinner if attending, dietary needs.
  - "Same for all days" meal shortcut; org/invoice details.
- **Room assignment:** picker excludes blocked and already-occupied rooms via the live `RoomId`; admin panel flags room conflicts.
- On submit: snapshots captured, token invalidated, status → `DetailsComplete`, admin notified.
- **Admin edit** of participant data after submission (the only post-submission edit path — no guest reopen).

**Satisfies:** FR-48, FR-49, FR-54–62 · NFR-15, NFR-16, NFR-18, NFR-33, NFR-34.

**Exit criteria:** a guest completes a booking end-to-end from the email link; token single-use/expiry/regeneration all behave; snapshots are stored; room conflicts are detected; admin can correct details post-submission; unit tests cover token expiry and meal/participant validation, integration test covers the completion submission.

### Render mode — Static SSR + progressive JS (consistent with the public Static-SSR rule)

The completion form stays **Static SSR + progressive-enhancement JS**, like the Phase-3 public forms — *not* an Interactive Server circuit. This was reconsidered deliberately (a token-gated Interactive Server island was weighed and rejected), with the decision resting on three axes:

- **Security surface:** Static SSR is the simpler thing to reason about on a public endpoint — every interaction is a stateless HTTP POST that re-carries and re-validates the token; no server-held circuit state, no unauthenticated interactive principal, and the existing Redis IP rate limiter applies directly. (The `/_blazor` SignalR endpoint already exists app-wide for the admin panel, so an interactive public page would not newly expose it — but it *would* newly have anonymous, token-only principals running server-side interactive components, which Static SSR avoids.)
- **Performance (this workload):** completion is low-volume and fill-once, so server capacity is a non-issue either way; what the guest actually feels is in-form responsiveness on a possibly-rural/mobile connection. Static SSR runs the toggles/meal-matrix/"same for all days" **client-side with zero latency** and degrades gracefully (a post works or you retry); an Interactive Server circuit round-trips every trivial UI update to the server and a dropped circuit interrupts the whole session (risk of lost input). Static SSR performs better on the axis that matters here.
- **Cost:** Interactive Server's only real advantage was avoiding hand-rolled nested binding + JS. That risk is bounded by the design constraints below, so it does not outweigh the security/performance edge of Static SSR.

**Implementation-risk controls (these are the plan, not a hope) — they turn the "variable nested collection" fear into a fixed-cardinality, server-authoritative form:**

1. **Overnight rows are fully server-pre-rendered**, count = the booking's `OvernightGuestCount` (known at render). There is *no* client-side add/remove for overnight participants, so the participant/meal indices are fixed and known when the HTML is generated — the nested `participant × day × meal` grid is a fixed-size structure with deterministic field names, not a variable-length collection assembled on the client.
2. **The per-day meal grid is rendered server-side** from `MealsByDayCalculator` (the stay dates are known), so which day/meal `<select>`s exist is decided on the server, not built in JS.
3. **Day guests use server round-trip add/remove** via named submit buttons on the SSR `EditForm` (Blazor SSR's supported multi-button postback re-renders the bound model with one more/fewer row) — *not* client-side row cloning. Each day-guest row is a **flat** record (single `AttendanceDate` + breakfast + optional lunch/dinner + dietary), so even added rows involve no nested client binding.
4. **JS is confined to pure presentation** that also works with JS disabled: the `IsFullBoard` show/hide, the "same for all days" convenience fill, and the day-guest section visibility. The **server handler is authoritative** — it re-derives (drops meal data for self-catering, drops day guests for `SelfCateringAll`, ignores meals on non-applicable days) regardless of client input, exactly the Phase-3 `InquiryNormalizer` pattern.
5. **Reuse the proven Phase-3 mechanics:** public `MainLayout` already disables Blazor enhanced-nav (`data-enhance-nav="false"`); no explicit `<AntiforgeryToken/>` (Phase-0 400 bug); and every SSR form is **curl-POST + real-browser smoke-tested** before a slice is called done (the Phase-3 lesson — construct-in-memory tests miss form-binding nulls and JS-timing bugs).

Token handling is unchanged by the render mode: `/slutfor/{token}` validates the token server-side on GET (invalid / expired / used / non-`Approved` → static Swedish error), and the submit handler re-validates token + booking status inside the state transition before committing.

### Scope boundary — Phase 4 ends at `DetailsComplete`

Phase 4 stops when the guest submits and the booking reaches `DetailsComplete`. The admin transitions `DetailsComplete → Active` ("mark arrived") and `Active → Completed` ("mark finished"), and the **thank-you email on `Completed`**, are **Phase 5** work (the §2.5 state machine already permits both transitions; only the admin UI actions and the email are deferred). Phase 4's Satisfies row is therefore exactly FR-48/49/54–62 · NFR-15/16/18/33/34 — no `Active`/`Completed` behaviour.

### Build order — six vertical slices (Phase 4)

Like Phases 1 and 3, Phase 4 is built as independently reviewable vertical slices, each merged to the phase branch (with its unit **and** integration tests) before the next begins. The ordering is dependency-driven: the data foundation, pure meals-by-day logic, and the long-deferred hard-delete guards come first; the token page + render-mode decision are proven early (slice 2) before the form is built on top; overnight-participant capture + submit is the core (slice 3); day guests reuse that machinery (slice 4); room assignment with its live exclusions + conflict detection is a self-contained concern (slice 5); and admin post-submission edit comes **last** (slice 6) because it depends on all participant data existing. Only slice 1 introduces a migration; slices 2–6 add no new tables.

| Slice | Migration | Delivers | Key §/FR refs |
|---|---|---|---|
| 1 | `AddParticipantAndMealPreference` | `Participant` + `MealPreference` entities (incl. snapshot fields `RoomAssignmentSnapshot`, `MealOptionSnapshot`); string enums `AgeCategory`/`ParticipantType`/`DietaryRequirement` + `DietaryNotes`; pure **`MealsByDayCalculator`** (§4.1: arrival = dinner, middle = lunch + dinner, departure = none, one-night = single dinner); **wire the three deferred Phase-1 hard-delete guards to real counts** — MealOption ← `MealPreference.MealOptionId` (FR-26), Room ← `Participant.RoomId` (FR-20), Building's transitive check (FR-16, via Room → `Participant.RoomId`) | §8.5, §8.6, §4.1, FR-16/20/26, NFR-15/16 |
| 2 | — | Token completion page `/slutfor/{token}` (Static SSR): server-side token validation states (valid / invalid / expired / used / non-`Approved`), Swedish static error for bad links; a valid token renders the Static-SSR form shell (proves the render-mode decision + token lookup end-to-end early) | §2.6, FR-53/54 |
| 3 | — | Completion form — overnight participants: name, `AgeCategory`, `IsFullBoard` (pre-filled from booking `MealPlan`, overridable), meals-by-day lunch/dinner pickers (full-board only), "same for all days" shortcut (FR-59), dietary + notes, org/invoice (FR-60); **submit** → build `Participant`+`MealPreference` with `MealOptionSnapshot`, `Approved → DetailsComplete` via `BookingStateMachine`, invalidate token (FR-62), send **admin "details complete" email** (FR-61). No room field / day guests yet | FR-55/57/59/60/61/62, §4.1, NFR-15/33 |
| 4 | — | Day guests: name, `AgeCategory`, `AttendanceDate`, `AttendsBreakfast`, lunch/dinner if attending, dietary; add/remove rows; shown only when `MealPlan` ∈ {`FullBoardAll`, `Mixed`} (server-enforced, hidden for `SelfCateringAll`) | FR-58, §4.2 |
| 5 | — | Room assignment (guest side, when `GuestChoosesRooms`): per-overnight-participant room picker excluding blocked rooms/buildings (`BlockedPeriod`) **and** rooms already occupied via live `Participant.RoomId` on date-overlapping bookings; capture `RoomAssignmentSnapshot`. Pure **`RoomExclusionCalculator`** + **`RoomConflictDetector`** (room assigned beyond bed count for overlapping dates) | FR-56, §3.3, §8.5, NFR-16 |
| 6 | — | Admin post-submission edit (Interactive Server): edit participant / meal / room data on a `DetailsComplete` booking — **no status change, no guest reopen** (FR-48); admin room assignment for the `GuestChoosesRooms = false` path; surface slice-5 room-conflict flags in `BookingDetail` | FR-48/56 (admin side) |

---

## Phase 5 — Notifications, lists & retention ✅ COMPLETE (merged to main 2026-07-05)

**Goal:** the automated lifecycle — reminders, downloadable lists, and GDPR auto-deletion.

**Scope & deliverables:**

- **Scheduled background-job** infrastructure.
- **14-day reminder (FR-63):** standard reminder if `DetailsComplete`+; if still `Approved`, regenerate the token and send the guest a nudge with a fresh link **plus** an admin alert.
- **Thank-you email** on Completed (fixed text).
- **List downloads:** participant list and food-preference list, each in **PDF and Excel**, performant to 300 participants.
- **GDPR retention:** `DeleteAfter` computation for every state (FR-91); scheduled deletion of Completed (2 yr), Denied/Cancelled (12 mo), Pending (12 mo), Closed (12 mo); 30-day pre-deletion admin warning, de-duplicated via `DeletionWarningEmailSentAt`.

**Satisfies:** FR-44, FR-45, FR-63–69, FR-89–91 · NFR-10 · email catalogue rows: two-week reminder, completion nudge, admin-not-completed, thank-you, pre-deletion warning.

**Exit criteria:** the 14-day job sends the right variant per status; lists generate correctly within the 10 s / 300-participant target; the retention job deletes exactly the right records and the warning fires once each; unit tests cover retention/deletion-date computation.

### Scheduled-job mechanism — in-process `BackgroundService` + Always On (NOT app-only deploy)

The daily automation (14-day reminders, 30-day pre-deletion warnings, GDPR deletion) runs from a single in-process hosted **`BackgroundService`** (`DailyJobRunner`) — no new Azure resource. It ticks once per day (computes the delay to the next configured run hour in **Europe/Stockholm**, sleeps, runs), and on each tick it opens a **fresh DI scope per run** (the booking/email services are scoped) and runs each job in turn; a job that throws is logged and never aborts the others (NFR-13 style). Each job is **idempotent** — guarded by a persisted sent-marker (`DeletionWarningEmailSentAt` for the warning, a new `ReminderEmailSentAt` for the reminder) so a restart or double-run cannot double-send or double-delete.

On App Service **B1 the app is unloaded after ~20 min idle**, which would stop the `BackgroundService` and skip runs. Reliable daily execution therefore requires **`alwaysOn: true`** in `main.bicep`'s `siteConfig`. Always On is free and does not change the flat B1 price (the server is billed whether awake or idle). Because this is an infra/config change and the pipeline deploys only app code, **Phase 5 is not an app-only deploy** (like Phase 3's Redis): the Always On setting must be applied to staging by hand (`az webapp config set --always-on true`, or a `main.bicep` redeploy) **before the phase branch merges**, and reconciled into prod at Phase 7. B1 is single-instance, so no distributed-lock/leader-election is needed.

### List downloads — libraries & delivery

Participant and food-preference lists are generated **on demand from an admin button** and **streamed directly** as a file download (in-memory, not persisted to Blob) — the lists are ≤300 rows, always reflect current snapshot data, and persisting copies would only add cleanup and stale-copy risk. **PDF** via **QuestPDF** (Community license — free for orgs under the revenue threshold; set `QuestPDF.Settings.License = LicenseType.Community` at startup), **Excel (`.xlsx`)** via **ClosedXML** (MIT). Both build off the **snapshot** fields (`MealPreference.MealOptionSnapshot`, `Participant.RoomAssignmentSnapshot`, dietary, attendance date) so a list reflects what was agreed even if reference data changed since (NFR-15/16).

### Scope note — admin lifecycle transitions deferred from Phase 4

`DetailsComplete → Active` ("markera anländ") and `Active → Completed` ("markera avslutad") — plus the thank-you email on `Completed` (FR-65) — were deferred from Phase 4 (the §2.5 state machine already permits both). They land here in **slice 1**. Completing a booking is the one remaining transition that sets `DeleteAfter` (check-out + 2 years, FR-91); all other retention-relevant transitions (Pending on create, Denied, Cancelled, Closed) already set it in Phase 3, so slice 1 closes the last `DeleteAfter` gap before the retention job (slice 2) begins deleting.

### Build order — four vertical slices (Phase 5)

Like Phases 1, 3, and 4, Phase 5 is built as independently reviewable vertical slices, each merged to the phase branch (with its unit **and** integration tests, plus a per-slice curl/browser smoke) before the next begins. The ordering is dependency-driven: slice 1 is self-contained and finishes the `DeleteAfter` wiring the deletion job relies on; slice 2 introduces the `DailyJobRunner` host (and the Always On infra change) with the simplest, guarded job to prove the daily tick + email dispatch; slice 3 adds the richer, multi-variant reminder job on the now-proven host; slice 4 (list downloads) is independent and last — the only non-job feature and the only slice adding NuGet packages. Only slice 3 introduces a migration.

| Slice | Migration | Delivers | Key §/FR refs |
|---|---|---|---|
| 1 | — | Admin lifecycle end: `DetailsComplete → Active` ("Markera anländ") + `Active → Completed` ("Markera avslutad") buttons on `BookingDetail`; `MarkArrivedAsync`/`MarkCompletedAsync` in `BookingService` (routed through `BookingStateMachine`). `MarkCompleted` sets `DeleteAfter` = check-out + 2 yr via `RetentionCalculator` (closes the last `DeleteAfter` gap) and sends the **thank-you email** (new §9 template). No background infra | §2.5, FR-65, FR-91 |
| 2 | — | **`DailyJobRunner`** hosted `BackgroundService` (daily tick, Europe/Stockholm, scope-per-run, resilient) + **Always On** in `main.bicep`. First job: GDPR retention — **deletion** off `Booking.DeleteAfter` (Completed 2 yr, Denied/Cancelled 12 mo, Pending 12 mo, Closed 12 mo) with cascade to participants/meals/selections; **30-day pre-deletion admin warning** deduped via `DeletionWarningEmailSentAt` (new §9 template) | FR-66/67/68/69/89/90/91, §2.7 |
| 3 | `AddReminderSentAt` | **14-day reminder job** (idempotent via new `ReminderEmailSentAt`): if `DetailsComplete`+ → standard guest reminder (participant/day-guest counts, meal summary, 14:00/12:00, address — FR-64); if still `Approved` → regenerate completion token (fresh link ≥48 h) + guest nudge + admin alert (FR-63). Refactor the token-regen core out of `RegenerateCompletionLinkAsync` so both callers share it. Three new §9 templates | FR-63/64, §2.6, §9 |
| 4 | — | List downloads: participant list + food-preference list, each as **PDF (QuestPDF)** and **Excel (ClosedXML)**, **streamed** from admin buttons on `BookingDetail`, gated on `DetailsComplete`+. Pure list-shaping builder off snapshot fields; NFR-10 perf test (≤10 s for 300 participants). Adds `QuestPDF` + `ClosedXML` packages | FR-44/45, NFR-10, §8.5/8.6 |

---

## Phase 6 — UI/UX ✅ COMPLETE (merged to main 2026-07-07)

**Goal:** turn the functional-but-unstyled app into a considered end-user experience — a public site that builds trust and **converts visitors into inquiries**, and an admin panel that is efficient to operate — grounded in the actual users, the brand feeling, and the guest journey. This is the visual and experiential design deferred from Phases 1–5 under "functional first, polish last".

### Design approach (this phase is run as UX/product design, not page styling)

- **Discovery before pixels.** The first slice defines users, journey, brand feeling, content/voice, and an agreed art direction *before* any component is built. The design system is an **output** of a chosen direction, not the starting point.
- **Design by user journey & jobs-to-be-done**, not page-groups: *discover → build trust → convert → complete → arrive*. The **booking funnel (home → availability → inquiry) is the primary success metric** — the public site exists to convert, and the build is prioritised around that.
- **Content/copy and accessibility-in-context are design drivers, not end-of-phase cleanup.** Retreat guests may skew older; a contact person completes a long form for a whole group on a phone on a rural connection — that context dictates type size, tap targets, form simplicity, and trust signals.
- **Public = persuasion design; admin = productivity design.** Two disciplines: the admin track optimises for data density, scannable lists, and efficient flows (NFR-18/19), not the public persuasion aesthetic.
- **Define "good" and validate it.** A closing slice runs a heuristic pass against the agreed principles, a lightweight usability walk of the booking funnel, and an accessibility (WCAG AA) spot-check.
- **Per build-slice workflow:** HTML mockup → user approves the look → implement in Blazor → browser-verify across breakpoints. (HTML-mockup-first, not Figma-first: the target medium *is* HTML/CSS, so mockups translate ~1:1; Figma is optional, only if a shareable artifact is later needed.)
- **Cross-cutting in every build slice** (drivers, enforced per slice — not deferred): mobile-first responsiveness; accessibility (semantic HTML, colour contrast, keyboard/focus, alt text, labels/aria); Swedish copy quality (NFR-32); **self-hosted fonts only / no external requests** (NFR-35 — a design pass is exactly where a stray Google Font or CDN slips in); and no regression to existing behaviour or the test suite.

**Scope & deliverables:** an agreed art direction + design principles; a documented design system (tokens + components with all states) evolving the Phase-0 tokens (`UX/design.md`); a redesigned, responsive, accessible **public site** across all pages with the booking funnel as the centrepiece; a redesigned, efficiency-focused **admin panel**; and a validated result (usability + accessibility). Imagery strategy (real photography vs. curated placeholders swapped at go-live) and the exact admin visual ambition are **decided in slice 1** (discovery), not pre-judged here.

**Satisfies:** NFR-17, NFR-18, NFR-19, NFR-20–23, NFR-32, NFR-35 (supports NFR-08 public performance, formally verified in Phase 7).

**Exit criteria:** the chosen art direction is implemented consistently across public + admin; the booking funnel is usability-validated; an accessibility AA spot-check passes; every page is responsive across breakpoints; the design system is documented; Swedish copy is reviewed; the **Playwright E2E/UI suite** (behaviour + axe a11y + responsive reachability) is green in CI; and no functional behaviour or test regressed.

### Build order — seven slices (Phase 6)

Unlike the feature phases, this phase is **bookended by design discipline**: it opens with a no-code discovery/direction slice (the north star every later slice answers to) and closes with a validation slice. The middle slices follow the **guest's journey in order** — because each step emotionally sets up the next — with the **conversion funnel as the climax the trust pages build toward**, then the post-approval completion task, and finally the admin panel as a distinct productivity-design track. Slice 1 has no code; slices 2–6 each = mockup → approval → Blazor implementation → browser verification; slice 7 is QA/validation.

| Slice | Focus | Delivers |
|---|---|---|
| 1 | **Discovery & art direction** *(no code)* | Guest & admin personas + jobs-to-be-done; the guest journey map; brand/emotional positioning + Swedish voice & tone; content audit + home value proposition & primary CTA; 4–6 design **principles**; **2–3 divergent art-direction concepts** (home hero + key components as HTML mockups) → user chooses ONE; **imagery strategy** and **admin visual-ambition** decisions; success criteria + usability/a11y validation plan. Output: agreed direction + principles + journey — the north star. |
| 2 | **Design system + Home** | The chosen direction expressed as a real, documented system evolving `UX/design.md`: tokens (contrast-checked colour, type scale, spacing, radius, elevation, motion); core components with **all states** (default/hover/focus/active/disabled/loading/empty/error) — buttons, links, inputs/selects/checkboxes, field+validation, cards, tables, alerts, badges, modals; the public **shell** (header, nav, mobile nav, footer); self-hosted fonts wired. Proven end-to-end on the flagship **Home** (`/`). Also **stands up the Playwright E2E harness** (`Microsoft.Playwright` + axe-core + 360/768/1280 helpers, booting the app against an ephemeral DB in CI) and covers Home with its first UI tests (renders, CTA navigates, axe-clean, breakpoint reachability). |
| 3 | **Explore & build trust** (public content) | The pages that answer "is this place right for my group, can I trust it": `/om-oss`, `/faciliteter`, **`/mat-och-priser`** (menu + prices merged — SRS v0.37), `/kontakt`, `/integritetspolicy`, plus `/not-found` & `/Error`. Shared "chapter opener" page-header pattern; trust-builders that feed the inquiry; data-driven presentation for facilities/menu/prices. |
| 4 | **Convert** (the booking funnel — primary KPI) | The money path designed as one coherent flow, not separate pages: availability calendar (`/kalender`) → inquiry form (`/boka`) → acknowledgement. Trust signals, friction reduction, one clear next step, the three-state calendar's legibility, form UX (progressive disclosure, inline validation, "this is a request, not a confirmed booking" reassurance). Mobile-first. **Crown-jewel E2E:** the full funnel journey (home → kalender → boka → acknowledgement) as a Playwright browser test — the primary KPI, previously only integration-tested at the API level. |
| 5 | **Complete** (guest completion flow) | The magic-link completion form (`/slutfor/{token}`) — a high-effort task done on mobile for a whole group: reassurance, progress, error recovery, per-day meal clarity. **Folds in the parked completion-form polish** (meal-section "same for all days" layout/labelling from the Phase-7 backlog). |
| 6 | **Admin** (productivity design) | Efficiency-first redesign of the admin surface (its own register, not the public aesthetic): shell + nav, auth pages, dashboard, bookings list + detail + manual create, admin calendar, blocks, the six reference-CRUD screens, accounts. Data density, scannable lists, fast booking workflows, clear status, keyboard-friendly. |
| 7 | **Validate & polish** | Heuristic review against the slice-1 principles; a lightweight usability walk of the booking funnel (user or proxy guest); accessibility audit (contrast, keyboard, screen-reader basics, WCAG AA spot-check) — **automated across pages via the Playwright + axe-core suite**; responsive QA across breakpoints; consistency cleanup. "Done" = principles met + funnel validated + a11y pass (manual + automated). |

*Note: the non-visual items still parked in Phase 7 (phone validation, confirm-email safeguard, approval-time block/capacity warning, admin participant-editing depth) stay there — they are functional/hardening, not design.*

---

## Phase 7 — Hardening & go-live

**Goal:** verify the non-functionals, clear the operational blockers, and launch.

**Scope & deliverables:**

- **Security:** full pass over NFR-01–07, NFR-25, NFR-30; confirm no PII in URLs/calendar; auth/rate-limit review. **Wire `ForwardedHeaders`** so the inquiry rate limiter (NFR-04) keys on the real client IP behind the App Service load balancer rather than `RemoteIpAddress` (deferred from Phase 3 — the distributed-store requirement is met there; accurate per-client IP is the hardening step).
- **Persist DataProtection keys (found during Phase 6 manual testing):** the app does not persist ASP.NET Core DataProtection keys to durable, shared storage — only `DataProtectionTokenProviderOptions` (token lifespan) is configured. On App Service a recycle/redeploy/scale-out regenerates the key ring, which **invalidates every antiforgery token and auth cookie** — a guest mid-way through `/boka` then hits *"A valid antiforgery token was not provided"* on submit (reproduced locally when the dev server restarted with an open form). Fix: `AddDataProtection().PersistKeysToAzureBlobStorage(...).ProtectKeysWithAzureKeyVault(...)` (Blob for the key ring, Key Vault for the master key) so keys survive restarts and are shared across instances; provision the blob container + KV key in Bicep. Infra/hardening — the antiforgery **middleware itself is correctly wired**; this is only about key durability.
- **Performance:** verify NFR-08 (public < 3 s), NFR-09 (admin < 5 s), NFR-10 (lists).
- **Reliability:** confirm backups (NFR-12) and run the **quarterly restore drill** (NFR-29); email failure handling (NFR-13).
- **Email E2E (NFR-31):** verify every guest/admin email against the **production** SMTP before go-live — not assumed from a successful build.
- **Operational blockers (from operational-tasks.md):** privacy-policy legal text drafted/reviewed; image migration completed and wired to Facility/page assets; **VAT rates per category confirmed with the client's accountant**.
- **UAT** with the admin **on the staging environment** (non-technical usability, NFR-18/19); Swedish-text review (NFR-32).
- **Public inquiry-form input polish (found during Phase 3 manual testing):** (a) stricter phone-number validation on the stay/general forms — a *non-empty* value is still lenient and accepts letters (the separate bug where a *blank* optional phone wrongly blocked submission was fixed in Phase 6 via `[OptionalPhone]`); (b) a safeguard against a mistyped guest email — a well-formed but wrong address is currently accepted silently (the admin still receives the inquiry via FR-06, but the guest never gets their acknowledgement and bounces are not surfaced in-app). Pragmatic fix: a "confirm email" second field on the public forms. Product decision on scope. (c) **Non-negative guest counts (found during Phase 6 manual testing):** the overnight/day-guest count number inputs (and the completion form's participant/day-guest counts) still accept negative values such as `-1`; add a `min` floor on the inputs **and** a server-side guard so a negative count can never be submitted — the field styling is Phase-6 but the missing floor is functional. (d) **Client-side validation (found during Phase 6 manual testing):** the public forms validate only on a full-page SSR post-back (a round-trip that scrolls the guest away from the error); consider progressive, SSR-safe client-side validation to surface errors inline before submit — a funnel-UX improvement, especially on slow rural connections. **[Phase 7 status — (a)–(d) IMPLEMENTED on branch `phase_7` (commits `d292549` A2 core: stricter `[OptionalPhone]`, confirm-email on both forms, count `min` floors; `377a81f` A2d: progressive SSR-safe client-side validation that eliminates the invalid-submit postback; `f803f97` A2c: `min` floors + server `[Range]` on every numeric input across public **and** admin). Not yet merged.]**
- **Reduced-motion scroll override defeated by source order (found during Phase 7 A2 testing):** in `wwwroot/css/site.css` the `@media (prefers-reduced-motion: reduce) { html { scroll-behavior: auto } }` rule (~line 43) is overridden by the later base `html { … scroll-behavior: smooth }` (~line 85), so reduced-motion users still get animated smooth-scroll (minor WCAG/motion-sensitivity a11y defect). Fix: move the base `scroll-behavior: smooth` before the reduced-motion media query, or scope the override to win (e.g. place it after the base rule). Trivial CSS reorder; no behaviour change for default-motion users.
- **Admin approval guard (found during Phase 4 manual testing):** warn the admin at approval time when the requested dates overlap a whole-property/building/room block or when the overnight guest count exceeds the beds available for those dates — the current flow lets a booking be approved onto fully-blocked dates, leaving the guest with an empty room picker. A non-blocking warning (admin keeps override authority), not a hard stop.
- **Completion-form polish (found during Phase 4 manual testing):** (a) **meal-section layout** — the per-participant "Välj samma för alla dagar" shortcut sits directly above the first dated row with no visual separation, so it reads as if the check-in day has both lunch and dinner (the per-day data is correct — arrival is dinner-only). Fix: box/relabel the shortcut and add a "Per dag" heading, or move the shortcut below the day list. (b) **Day-guest edge days** — a day guest may currently be booked for the arrival day (dinner only) or the departure day (breakfast only); per §4.1 those meals are served, so it is valid, but confirm with the client whether breakfast-/dinner-only day guests on the edge days are a real scenario, else drop the edge days from the day-guest date dropdown. (c) **Admin participant editing depth (gap #1)** — the admin editor changes existing meal choices but cannot add/remove meal days or flip a participant's `IsFullBoard`; decide whether that fuller edit is needed. (d) minor: the overnight "same for all days" shortcut selects reset their own displayed value on an add/remove day-guest postback (the per-day values persist). (e) **Self-catering vs per-participant Helpension (found during Phase 6 manual testing)** — the day-guests section is gated on the booking-level `MealPlan` (§4.2: hidden for `SelfCateringAll`), but the completion form still shows a per-participant "Helpension (måltider ingår)" checkbox that can be ticked even on a `SelfCateringAll` booking, which would give that overnight guest catered meals — contradicting "self-catering has no catered meals". Decide the intended rule and reconcile §4.1 (C2 per-participant override) with §4.2 (booking-level gate): either (i) don't render / disable the per-participant Helpension override on `SelfCateringAll` bookings, or (ii) make day-guest applicability follow "any participant is full-board" rather than the booking `MealPlan`. Functional/spec decision — Phase 6 only restyled this; the gate and the checkbox are both pre-existing.
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
| 6 UI/UX | — | NFR-17,18,19,20–23,32,35 (supports 08) |
| 7 Hardening & go-live | — | NFR-08,09,10,29,31 + residual |

*Testing (NFR-26–28) and Swedish UI (NFR-32) are cross-cutting and apply to every phase.*

---

*Derived from requirements.md v0.35 and erd.md — Åkerögården Booking System.*
