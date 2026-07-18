# Åkerögården — Booking System
## SRS changelog (archived)

Frozen record of how the SRS evolved from its first draft to the retirement of document versioning.
No longer maintained — `requirements.md` is now a living document edited in place, and the history of
changes lives in git (`git log --follow docs/product/requirements.md`).

Rationale that still governs a live constraint has been folded into the body of `requirements.md`
itself; what remains here is history.

---

**Changes from v0.52:**

- **§11 hosting cost table reconciled to the deployed shape (no cost change — still ~$45/month).** Reality-checked the table against the Bicep templates and the live 2026-07-17 staging deployment: every billed SKU still matches (App Service B1, PostgreSQL B1ms + 32 GB, Blob Hot/LRS, Redis Balanced B0). Corrected two stale/missing items that do not move the total: (1) the App Service and custom-domain rows said the two hostnames "point to the same App Service" — post-Phase-10b they are **two sites on one B1 plan** (the plan is the billing unit, so the second site is $0); (2) added rows for the **Application Insights + Log Analytics** telemetry (Phase 11; ingestion capped at 0.1 GB/day, 30-day retention — within the free grant, ~$0) and **Key Vault** (Standard, per-operation, negligible), neither of which was previously tabulated.

**Changes from v0.51:**

- **FR-70 (admin/public host separation) genuinely satisfied for the first time (Phase 10b).** FR-70 was nominally scoped in Phase 0 but never actually built — one web app served every route on both hostnames, with separation only nominal (admin pages were `[Authorize]`-gated, so `/admin` on the public host redirected to login rather than 404ing). Phase 10b makes it real, structurally: the code is split into three projects (`AkeroGarden.Web.Core` shared RCL + `.Public` pure Static SSR + `.Admin` Interactive Server + Identity), deployed as **two App Service sites on one Basic B1 plan** (`akerogarden.se` → public, `admin.akerogarden.se` → admin). The internet-facing public process loads no Identity, SignalR, or admin assemblies, so an admin route such as `/bokningar` on the public host is now a genuine 404 — process isolation, not edge gating. **No FR/NFR text change** (FR-70's wording already described the target); this is an implementation milestone. Zero added cost (the plan is the billing unit). `architecture.md` (note 1 + diagram), `infra/azure/README.md`, `infra/ops/dns-cutover-runbook.md`, and `implementation-plan.md` (Phase 10b) updated in step. Staging cut over to the two-app topology and verified live 2026-07-17.

**Changes from v0.50:**

- **CI/CD platform changed from GitHub Actions to Azure DevOps (Phase 10a, mid-phase).** The client's Azure DevOps access (via their subscription, no GitHub presence) made this preferable once actually assessed — the repo (all branches, full history) is mirrored to Azure Repos (`dev.azure.com/akerogarden/akerogarden`); deploys authenticate via a service-principal-backed service connection instead of a publish-profile secret; the three GitHub Actions workflows are ported to Azure Pipelines YAML. §2 (tech-stack decision table) and §11.1 updated; `infra/azure/README.md` and `implementation-plan.md` updated to match. The GitHub repository is being retired as a **read-only archive** (frozen, no further pushes; history stays reachable) — decided 2026-07-17; Azure Repos is the single source of truth going forward.

**Changes from v0.49:**

- **Phase 10 split into 10a (staging re-provisioning) and 10b (production go-live); staging moves to the client's Azure tenant.** The client now has their own Azure subscription (Pay-As-You-Go, new tenant) — both staging and production will be provisioned there instead of the developer's personal MSDN subscription that has hosted staging until now. Since Azure's resource-move feature does not work across tenants, staging is **recreated from the existing Bicep templates** in the client's tenant (not migrated), and the old MSDN-hosted copy is deleted once the new one is verified — tracked as the new **Phase 10a**. At the same time, staging's long-deferred custom-subdomain binding is no longer deferred: it gets the same public/admin split as production (`staging.akerogarden.se` + `admin.staging.akerogarden.se`, vs. today's single Azure default hostname). Production provisioning (previously all of "Phase 10") becomes **Phase 10b**, unchanged in scope but now explicitly sequenced after 10a and provisioned directly in the client's tenant from the start. §11/§11.1 (hosting tables) and `implementation-plan.md` updated in step; `infra/azure/README.md` and `infra/ops/dns-cutover-runbook.md` updated to match.

**Changes from v0.48:**

- **Whole-physical-building blocking removed from Phase 11 and deferred to a future release.** The `BlockedPeriod` `PhysicalBuilding` scope (block all lodging units of a physical building — e.g. all of Stora bostadslängan — in one action) was carried as an open Phase 11 item. Reassessed and dropped from committed scope: it is not a capability gap (the admin can already block each lodging building separately), only an ergonomic shortcut for a low-frequency operation, and it would add a permanent fourth `BlockScope` value to every blocking switch/validator/calculator for little routine benefit. Now listed in §10 "Deferred to future releases" — to be built only if the client confirms it is a recurring real need. No code, data-model, or ERD change (the scope was never implemented); `implementation-plan.md` Phase 11 updated in step (item and Satisfies row). The `PhysicalBuilding` entity's design rationale (§8.14, changelog v0.32) is unchanged — it still exists for cleaning pricing and still *permits* this future scope.

**Changes from v0.47:**

- **FR-97 (audit-trail admin view) now tracked in §10 "Deferred to future releases."** The audit-trail backend — capture + retention (FR-95/96/98, §6.9, §8.15) — ships in Phase 11, but its admin-facing UI (a per-booking "Aktivitetslogg" panel and a global, filterable audit-log page) has no committed delivery, and Phase 11 is the final phase. FR-97 stays a numbered requirement in §6.9 (wording unchanged — it already reads "UI deferred") but is now **also** listed as explicit deferred scope in §10, alongside the other future-release items, rather than an open-ended in-phase "deferred" note with nowhere to land. Until it is built, the trail is read directly from the database. No data-model, email, or ERD impact; `implementation-plan.md` Phase 11 (Slice D) updated in step to point at the §10 deferral.

**Changes from v0.46:**

- **Admin profile fields — first name, last name, phone (all required) — added during audit-trail implementation.** Admins previously carried only an email. FR-74 and the §6.4 storage note now require every admin to have a **first name, last name and phone number**, all mandatory. Decision taken with the client-developer: the **invitee enters their own** first/last/phone when they activate their account (the setup/password page), so the invite itself stays email-only. `PhoneNumber` is the built-in ASP.NET Identity field; `FirstName`/`LastName` are added to `ApplicationUser` (Identity infrastructure — still no custom app entity). This pairs with the audit trail: `AuditLog.ActorNameSnapshot` (§8.15, new) captures the acting admin's **name** alongside their email at write time, so the trail reads "Anna Andersson gjorde X" rather than an address. The admin name (staff data, not guest data) in the audit trail does not affect NFR-37, which governs *guest* personal data. `docs/product/erd.md` updated (AUDITLOG gains `ActorNameSnapshot`).

**Changes from v0.45:**

- **New audit trail (Phase 11 — post-production; backend now, admin view deferred).** The system had no record of who changed what: no reference-data or booking service logged the acting user, no entity carried a `ModifiedBy` field, and no `AuditLog` table existed. Added a first-class **`AuditLog`** entity (§8.15) and a new **§6.9 Admin — Audit Trail** (FR-95–98). Design decisions taken with the client-developer: (1) **no personal data in the trail** — booking-change entries record only *which* action occurred (approve, cancel, payment marked, participants edited, …) plus the acting user and the `BookingReference`, never the guest field values, so no guest names/contact details and no special-category dietary/allergy data (GDPR art. 9) are ever duplicated into the log (NFR-37); reference-data entries (prices, rooms, zones — no personal data) may carry a before/after summary; (2) the **actor** is one of `Admin` / `Guest` (completion via magic link) / `System` (background jobs), so a change is always attributable; (3) the trail is **append-only** — no admin edit-or-delete path (NFR-36); (4) **reads are not audited** — only changes; (5) **retention matches the booking** — booking-scoped entries are deleted with the booking (FR-67/68/89/90), non-booking entries after 2 years (FR-98). The admin-facing view (FR-97) is captured in scope but its UI is a later slice — the trail is written from the outset and, until the view exists, is queryable directly against the database. Entity count 13 → 14. `docs/legal/Integritetspolicy.md` and `docs/product/erd.md` updated in step; no change to any go-live requirement.

**Changes from v0.44:**

- **FR-74 extended — pending-invite resend/remove (staging feedback 2026-07-11).** An invited admin's one-time setup link expires after 1 hour; there was no way to resend a fresh invite or remove a stale, never-completed one. FR-74 now covers **resend a fresh invitation** and **remove a pending invite**, both limited to never-activated invites (activated accounts keep deactivate-only, no delete). Scheduled in the new **Phase 9 — Pre-go-live** (a phase inserted before go-live, which becomes Phase 10); agreed, not yet built. No data-model change stated here beyond the plan's `ApplicationUser.SetupCompletedAt` implementation note.

**Changes from v0.43:**

- **Cleaning reworked: moved from inquiry to the completion form as self-clean/buy toggles, and a new `PhysicalBuilding` entity introduced (Phase 8 client feedback + design review).** The client's demo feedback was that offering the full cleaning list (incl. "Hela lägret") at inquiry is illogical — a guest should only be offered cleaning for what they actually occupy, and at inquiry no rooms/buildings are chosen yet. Cleaning therefore leaves the inquiry form (FR-03) and is chosen on the **completion form** (new FR-93), where occupancy is known. The guest sees up to three self-clean/buy toggles: **Huvudbyggnad** and a **combined sleeping-building** toggle (whenever there is overnight lodging), and **Konferensbyggnad** (only when the Konferensrum facility is booked). The sleeping toggle is *combined* because which residential building the group sleeps in is not known when cleaning is chosen — it is resolved from the room assignment. "Hela lägret" is no longer a guest option (per-area toggles make it redundant, 4 × 2 000 = 8 000); it stays a rate-card line for manual invoicing.
  - **Modelling decision (why `PhysicalBuilding`, not a lookup array):** cleaning is priced **per physical building**, but the accommodation `Building` entities (Blidö, Tjockö, …) are really *lodging units that sit inside* larger physical buildings (Lilla/Stora bostadslängan); the physical buildings that cleaning is priced on did not exist as entities. Rather than bolt a hand-maintained `uuid[]` mapping onto `CleaningZone`, a first-class **`PhysicalBuilding`** entity (§8.14) is introduced: `Building` gets a `PhysicalBuildingId` FK, `Facility` gets a nullable `PhysicalBuildingId` FK (Konferensrum → Konferensbyggnad), and `CleaningZone` links to a `PhysicalBuilding` by FK instead of ID arrays. Sleeping-cleaning then resolves by a structural join (room → building → physical building → zone) with referential integrity, and the model **unlocks whole-physical-building blocking** later (e.g. block all of Stora bostadslängan in one action) — a `BlockedPeriod` `PhysicalBuilding` scope is a future slice, not built here. Chosen over a `CleaningZone`↔`Building` join table (which would fix integrity but not give the reusable building concept) because a concrete future use — whole-längan blocking — exists. Pre-go-live with no production data, the migration + seed backfill is painless.
  - **Data-model impact:** new `PhysicalBuilding` (§8.14, admin-managed, FR-94); `Building.PhysicalBuildingId` FK (§8.2, FR-13); `Facility.PhysicalBuildingId` nullable FK (§8.8); `CleaningZone` keeps `Kind` but replaces the building/facility ID arrays with a single `PhysicalBuildingId` FK (§8.11, FR-85); `Booking` gains three cleaning-choice booleans (§8.1); `BookingCleaningSelection` is populated at completion, sleeping rows resolved + price-snapshotted when room assignment is set/changed (deferred snapshot, §8.12) so NFR-33 holds. **App entity count 12 → 13.** `CleaningZone` stays the admin rate card, still shown on Mat & priser (FR-82). See §3.1, §5.5, FR-03, FR-13, FR-85, FR-87, FR-93, FR-94, §8.1, §8.2, §8.8, §8.11, §8.12, §8.14; erd.md updated.

**Changes from v0.42:**

- **FR-92 refined after local manual smoke testing.** Two fixes to the booking-reference feature, no data-model impact: (1) the reference is now appended to every email **subject line** (e.g. "Bokningsuppgifter inskickade (AG-2026-0007)"), not just the body, so it's visible in an inbox list without opening the email; (2) the Swedish label shown in emails and the admin search box changed from **"Bokningsreferens"** ("booking reference") to the type-neutral **"Referensnummer"** ("reference number") — a general inquiry isn't a booking, so the booking-specific label read wrong on that email. The underlying field, format (`AG-YYYY-NNNN`), and generation logic are unchanged.

**Changes from v0.41:**

- **New FR-92: human-readable booking reference number (Phase 8, discussion item raised in `implementation-plan.md`).** Bookings previously carried only an opaque `Guid Id`, leaving guests and admin with no shared, speakable identifier for phone/email support. Every booking (stay request or general inquiry) is now assigned a `BookingReference` (format `AG-YYYY-NNNN`, e.g. `AG-2026-0042`) at creation, generated from a database-identity `SequenceNumber` — a global monotonic sequence, not a per-year-reset counter, so no locking is needed and no race condition is possible at this booking volume. `YYYY` reflects the booking's creation year; the sequence itself does not restart at 1 each January. Not PII (safe in URLs, NFR-05) and not a security credential (the magic-link completion token remains the authenticated lookup). Shown in every guest and admin email, the admin booking list (searchable) and detail page. Data-model addition — see §8.1.

**Changes from v0.40:**

- **FR-45 (Matlista) redesigned from a per-person table into a kitchen prep summary (Phase 8 client UAT).** Superseding the v0.40 per-day matrix: testing a real 80-guest/5-day export showed that *any* per-person layout (one row per guest) is the wrong shape — ~80 near-identical rows over ~12 pages. A caterer needs "how many of each dish per meal per day", and names only matter for special handling. The Matlista now has two parts: a **tillagningsöversikt** (per-day/per-meal dish counts + breakfast headcount, no names) and a **kostbehov & allergier** list (only participants with a dietary requirement/note, named, with their meals). The full named roster stays in the participant list (FR-44), so no data is lost and PII is not duplicated. No data-model impact.

**Changes from v0.39:**

- **FR-59 refined from an "apply to all days" shortcut to a per-day group default (Phase 8 client UAT).** Testing a real 80-guest, 5-day full-board booking showed that applying one meal choice to *every* day of the stay is unrealistic — real groups vary meals by day. The "Standard för hela gruppen" panel now offers a lunch/dinner dropdown **per serving day**, each cascading to that day's slot for every participant (per-participant override retained). Presentation-only; no data-model impact. FR-45 (food-preference list) was first re-laid-out as a per-day matrix (later superseded — see v0.41). Admin save/action feedback (NFR-17/18) now renders as a fixed in-viewport toast rather than a top-of-page line the admin had to scroll up to see.

**Changes from v0.38:**

- **NFR-17 extended to the admin panel's mobile usability (Phase 8 client feedback).** The client indicated the admin panel will often be operated from a mobile phone. NFR-17 previously scoped full mobile responsiveness to the public website only; it now also requires the admin panel's **primary navigation** (a collapsible hamburger menu ≤860px) and the **Översikt dashboard** (reflows to a single column) to be usable on a phone. Dense admin data tables may remain horizontally scrollable on small screens (an explicit, accepted boundary — the target is "every day-to-day task is completable on a phone", not pixel-identical to desktop). Implemented in Phase 8 (admin hamburger + dashboard reflow); no data-model impact.

**Changes from v0.37:**

- **§11 hosting cost table corrected against live Azure retail prices (North Europe, July 2026).** The Azure Managed Redis (Balanced B0) line was overstated at **~$45/month**; the actual retail price is **$0.018/hour ≈ ~$13/month** (single consumption meter, verified via the Azure Retail Prices API — the ~$45 figure in the v0.35 changelog below was an unvalidated estimate made during the Azure Cache → Managed Redis migration). The Postgres row now shows its 32 GB storage (~$4/month) separately from compute (~$13/month). Corrected **production total: ~$72–73 → ~$45/month.** §11.1 staging estimate and `infra/azure/README.md` updated to match. No infrastructure or SKU change — figures only.

**Changes from v0.36:**

- Public **Menu and Prices pages merged into a single "Mat & priser" page** (`/mat-och-priser`), decided during the Phase 6 (UI/UX) redesign — a group organiser weighs food and cost together, so they read as one page. **FR-82 and FR-83 keep their numbers** but now describe content served on the combined page; FR-01 and the §1.2 in-scope page list updated (menu, prices → "mat & priser"); the §4 and §5 public-display notes updated. No data-model or admin change — meal options and price rules/cleaning zones are still separately admin-managed. `implementation-plan.md` slice 3 updated.

**Changes from v0.35:**

- Rate-limit store changed from **Azure Cache for Redis (Basic C0)** to **Azure Managed Redis (Balanced B0)** — forced by Microsoft retiring the classic Azure Cache for Redis and blocking new instance creation (the deploy failed with "Azure Cache for Redis is retiring, create Azure Managed Redis instance instead"). Azure Managed Redis is the successor, speaks the same Redis protocol (no application code change — StackExchange.Redis is unchanged), and is provisioned from the same Bicep via `Microsoft.Cache/redisEnterprise` (Balanced B0, EnterpriseCluster policy, TLS on port 10000). Cost rises from ~$16 to ~$45/month; §1.5, §11 cost table, §11.1 and NFR-04 updated. NFR-04's intent (distributed rate-limit store, correct under multi-instance scale-out) is unchanged.

**Changes from v0.34:**

- `Facility` (§8.8) gains `AmountExVat` and `VatRate` fields, mirroring `CleaningZone`. This resolves an internal inconsistency found during Phase 3: `BookingFacilitySelection` (§8.13) and NFR-34 require snapshotting the facility's agreed **amount**, and the erd.md modelling note calls facility and cleaning selections symmetric, but the `Facility` entity had no price to snapshot. Facility prices (§5.4) now have a home in the model. As with `CleaningZone`, these are reference figures only — no totals are computed (C8). erd.md updated; a Phase 1 retrofit (entity + migration + admin CRUD price inputs) is applied in the Phase 3 branch. Public display is unchanged: FR-81 still shows name/description/image and FR-82 still lists price rules + cleaning zones (facility prices remain admin-facing reference data).

**Changes from v0.33:**

- Bicep adopted as the Azure infrastructure-as-code tool (§11): both production and staging are provisioned from `.bicep` templates with environment-specific parameter files. Click-through portal provisioning is not used. Azure provisioning in Phase 0 is deferred to the end of that phase — Bicep templates and CI pipeline are written first, Azure resources are actually provisioned only once there is a working deployable artifact (admin auth working locally). implementation-plan.md Phase 0 updated accordingly.
- NFR-35 added (§7.6): fonts must be self-hosted; no external font CDN requests permitted (GDPR — loading Google Fonts sends user IPs to Google without a legal basis per Austrian DPA January 2022 ruling). docs/ux/design.md updated with font hosting instructions.

**Changes from v0.32:**
- Environment strategy added (§11.1): the spec previously detailed only production. Now defines all four environments — local dev (Dockerised PostgreSQL/Redis/Azurite + a local SMTP catcher on the developer's Mac), automated test (ephemeral containers in CI), a separate minimal Azure **staging** environment (`staging.akerogarden.se`, mirrors prod on cheapest tiers, powered down when idle, synthetic data only — no real PII), and production (unchanged). Deploy flow: dev → CI → staging → production. implementation-plan.md Phase 0 and Phase 6 updated accordingly.

**Changes from v0.31:**
- Facility selections aligned to the snapshot pattern (surfaced during the ERD pass; mirrors C6): `Booking.RequestedFacilityIds[]` replaced by a `BookingFacilitySelection` child entity (Section 8.13) — live `FacilityId` FK plus facility name and amount snapshots — so the agreed facility price survives later edits, consistent with `BookingCleaningSelection`, meals, and rooms. New NFR-34 (facility snapshot); §8.1 updated; ERD updated.

**Changes from v0.30:**
- Post-review consistency pass (Opus): caught inconsistencies introduced across the v0.19–v0.30 edits. FR-76 aligned to the §2.5 state machine — cancellation is allowed from `Approved`, `DetailsComplete`, or `Active` (was "any status except Completed/Denied", which wrongly implied `Pending`/`Closed` were cancellable). §5.5 terminology aligned to "cleaning zones" (was "building zones" in two places). §1.3 in-scope blocking bullet now includes whole-property scope (I8). §10 out-of-scope calendar wording updated to the three-state model (was the stale "booked/free"). §8.1 `DayGuestCount` clarified as the inquiry-stage estimate, with the day-guest `Participant` records authoritative for the kitchen list.

**Changes from v0.29:**
- I14 resolved (Round 2 architect review): room assignments now carry a live `RoomId` foreign key on `Participant` (alongside the existing `RoomAssignmentSnapshot` text, kept for history per NFR-16). Previously the assignment was text-only, so the system could not detect room conflicts, show occupancy, or exclude taken rooms. FR-56 updated to also exclude already-occupied rooms and to flag double-assignment conflicts in the admin panel. §8.5 updated. **This was the last open Round 2 item — all 20 are now resolved.**

**Changes from v0.28:**
- I11 resolved (Round 2 architect review): meals-by-day-of-stay rule defined for the check-in 14:00 / check-out 12:00 schedule — arrival day = dinner only, middle days = breakfast + lunch + dinner, departure day = breakfast only. The completion form shows a meal picker only for meals that actually occur on each day (so a one-night stay collects a single dinner selection). §4.1 and FR-57 updated.
- I12 resolved (Round 2 architect review): day guests are single-day — a new `AttendanceDate` field (day guests only) records the one date attended; a visitor across multiple days is entered as one day-guest record per date, which keeps per-day kitchen headcount exact and lets `AttendsBreakfast` apply to a specific day. §4.2, FR-58, and §8.5 updated.

**Changes from v0.27:**
- I13 resolved (Round 2 architect review): the completion token could already be expired by the time the 14-days-before-check-in nudge fired (the token expires at the earlier of issue+30 days or check-in−14 days, so for bookings approved well in advance it dies before the nudge). Fix: when the nudge fires and the booking is still `Approved`, the system regenerates the completion token (fresh link, ≥48 h per §2.6, invalidating the prior link) and includes it in the nudge; the admin alert states the new expiry. FR-63 and the §9 nudge/alert rows updated.

**Changes from v0.26:**
- I10 resolved (Round 2 architect review): boundary conditions pinned down. (1) Deposit: cancellation **more than** 2 months before arrival → full refund; **2 months or less** → forfeited (exactly 2 months = forfeited). §5.6, FR-79, §9 updated. (2) Age tiers: the free/paid line is **age 4** — `Infant` = 0–3 (free), `Child` = 4–12, `Adult` = 13+ (the Child/Adult split is kitchen-portion-only, no price effect). §5.1/5.2 and §8.5 updated. (3) Completion-token thresholds are evaluated as exact timestamps (valid up to the expiry instant, rejected at/after it); §2.6 clarified. (4) FR-75 admin warning fires when check-in is 14 days away or sooner.

**Changes from v0.25:**
- I9 resolved (Round 2 architect review): the `IsPubliclyVisible` flag is **removed** from both `Booking` and `BlockedPeriod`. It was a footgun — a hidden booking or block left the public calendar showing dates as available when they were not, prompting guests to inquire on closed dates — and since the calendar exposes only aggregate Free/Partial/Full (NFR-07), hiding a booking served no privacy purpose. Now all occupying bookings and all blocks always count toward public availability. The "private stay / internal hold" need is covered by an admin-created booking (FR-47) or a block, both of which now correctly reduce availability. FR-12 and FR-41 removed; FR-10 simplified (no "publicly visible" qualifier); the field is dropped from §8.1 and §8.10.

**Changes from v0.24:**
- I8 resolved (Round 2 architect review): the two overlapping blocking entities (`RoomBlock` and `BlockedPeriod`) are merged into a single `BlockedPeriod` entity with a `Scope` field (`WholeProperty | Building | Room`). Previously a building could be blocked two different ways with different effects — and a building blocked via `RoomBlock` did not reduce public-calendar capacity. Now one feature, one admin screen: every block reduces the date's effective capacity for its scope, and the calendar reflects it. `RoomBlock` (§8.4) merged away; `AffectedBuildingIds` dropped from `BlockedPeriod`; FR-10 now computes against effective capacity; FR-21 and §3.3 reworded.

**Changes from v0.23:**
- Round 2 documentation-hygiene sweep:
  - L8: FR-01 page list now includes the menu and prices pages (was inconsistent with §1.3 and FR-82/83).
  - L10: §9 email catalog now includes the completion-nudge (guest) and not-completed-alert (admin) emails introduced by FR-63; the two-week reminder row scoped to `DetailsComplete`-or-later.
  - L13: §8.1 now states that stay-specific fields are null/unused for `GeneralInquiry` bookings.
  - L14: NFR-10 reconciled with unbounded day guests (§4.2) — target raised to 300 participants; larger lists must still succeed beyond the 10-second guarantee.
  - L15: §5 no longer asserts a blanket 12% VAT — VAT is stored per `VatRate` (price rule / cleaning zone); exact rates per category to be confirmed with the client's accountant.
  - (L11 design.md and L12 operational-tasks.md updated separately — outside requirements.md.)

**Changes from v0.22:**
- C10 resolved (Round 2 architect review): the contradiction between FR-49 ("resend the completion link regardless of whether it has been used") and the §2.5 state machine (which has no path back through the completion form) is resolved by an **admin-edits-only** model. After a guest submits, the booking is `DetailsComplete` and the completion link is never re-issued; any correction is made by the admin in the panel (FR-48), with no status change. FR-49 rewritten and scoped to the `Approved` (awaiting-completion) state; FR-43 merged into FR-49 (token regeneration and resend are now one requirement); §2.5 annotated to make the no-reopen rule explicit.

**Changes from v0.21:**
- C9 resolved (Round 2 architect review): retention now has a functional requirement for all four §2.7 cases. Previously only Completed (FR-67) and Denied/Cancelled (FR-68) had deletion FRs; Pending-no-response and Closed inquiries would have been left in the database indefinitely. New FR-89 (delete `Pending` 12 months after submission), FR-90 (delete `Closed` 12 months after submission), and FR-91 (defines when `DeleteAfter` is (re)computed for every state and confirms live in-progress states are never auto-deleted). §8.1 `DeleteAfter` note updated.

**Changes from v0.20:**
- C8 resolved (Round 2 architect review): (1) Confirmed as a deliberate design choice that the system stores and computes **no monetary amounts** — no booking total, deposit amount, or balance. Deposit/final tracking is purely paid/unpaid status (FR-42); the 20%/80% figures in emails are fixed policy wording, not computed values. Stated explicitly in Section 5.6. (2) New `AgeCategory` field (`Adult | Child | Infant`) on Participant (Section 8.5), so accommodation pricing tiers (standard rate vs free infant) and the free-infant headcount are derivable, and the kitchen can plan child portions. Collected per participant on the completion form (FR-57, FR-58); Sections 5.1/5.2 annotated. Exact Child/Infant age threshold deferred to I10.

**Changes from v0.19:**
- C7 resolved (Round 2 architect review): the public availability calendar now counts bookings in `Approved`, `DetailsComplete`, or `Active` status (termed "occupying bookings") — previously it counted only `Approved`, so a confirmed group vanished from the calendar the moment it advanced to `DetailsComplete`/`Active`, making fully-booked dates appear free. `Pending` inquiries still do not count (availability subject to admin confirmation); terminal statuses never count. FR-10 reworded; Section 2.3 diagram updated. (The separate `IsPubliclyVisible` interaction remains tracked under I9.)

**Changes from v0.18:**
- C6 resolved (Round 2 architect review): cleaning "building zones" now have a dedicated admin-managed `CleaningZone` entity (Section 8.11) — distinct from both bookable Facilities (resolving the I4 violation in the data model) and accommodation Buildings. The old `Booking.CleaningSelections` (GUID[] → Facility) is replaced by a `BookingCleaningSelection` child entity (Section 8.12) that snapshots zone name and amount at booking time, so the agreed price survives later edits to the zone. Hela lägret is modelled generically via `IncludesZoneIds`. New Section 6.3.6 + FR-85–88 (cleaning zone CRUD, which was entirely missing); FR-03 and Section 5.5 reworded (full active zone list shown at inquiry — no per-booking filtering); FR-82 extended to render cleaning zones on the public prices page; NFR-14 extended; new NFR-33 (cleaning snapshot). Stale document footer version stamp corrected (was "0.6"), closing L9.

**Changes from v0.17:**
- L1 resolved: public calendar shows 12 rolling months forward from today; no backward navigation — FR-84 added
- L2 resolved: image upload accepts JPEG, PNG, WebP; max 5 MB; inline error on violation — FR-27 updated
- L3 resolved: denial email uses a fixed Swedish line for alternative dates ("Kontakta oss för att diskutera alternativa datum") — no admin-entered dates field; Section 9 updated
- L4 resolved: booking status transition state machine defined in Section 2.4; invalid transitions are blocked by the system
- L6 resolved: when the 2-week reminder fires and booking is still `Approved` (completion form not submitted), the system sends both a guest nudge and an admin alert — FR-63 updated
- L7 resolved: Hela lägret covers all four building zones (Huvudbyggnad, Lilla bostadslängan, Konferensbyggnad, Stora bostadslängan); selecting it disables individual zone checkboxes on the inquiry form — Section 5.5 updated

**Changes from v0.16:**
- I5 resolved: `DeletionWarningEmailSentAt` field added to Booking model to prevent duplicate pre-deletion warning emails from the daily background job
- I6 resolved: framework pinned to .NET 10 LTS (released November 2025, supported until November 2028); .NET 9 is EOL as of May 2026; Section 1.5 updated
- I7 resolved: Blazor rendering mode specified — Static SSR for all public-facing pages; Interactive Server for admin panel; Section 1.5 updated

**Changes from v0.15:**
- I4 resolved: FR-03 wording corrected — "cleaning service selection per relevant facility" replaced with "per applicable building zone" to resolve terminology collision with bookable facilities

**Changes from v0.14:**
- I3 resolved: `AttendsBreakfast` boolean added to Participant model; FR-58 updated; breakfast-only day guest validation is a deferred minor detail that does not affect the model

**Changes from v0.13:**
- I2 resolved: calendar uses three states (Free / Partially booked / Fully booked) based on total booked guest count vs. property capacity (79 beds); FR-09 and FR-10 updated; public site note about partial availability added

**Changes from v0.12:**
- I1 resolved: FR-46 (admin management of public site text content) dropped — hero text, about text, contact details, and social links are hardcoded as they are not subject to change
- Prices and menu are now shown publicly: "not displayed publicly" notes removed from Sections 4.1 and 5; "Public display of menu or pricing" removed from Section 10 Out of Scope
- New FR-81: public facilities page renders dynamically from admin-managed facility records
- New FR-82: public prices page renders dynamically from admin-managed price rules
- New FR-83: public menu page renders dynamically from admin-managed meal options

**Changes from v0.11:**
- C5 resolved: General inquiry lifecycle defined — admin reads and replies outside the system, then marks `Closed`; no approve/deny applies; new `Closed` status added (general inquiries only)
- New FR-80: general inquiries have no approve/deny — only action is mark Closed
- FR-05 updated: auto-reply content differs by inquiry type
- `Closed` status added to Section 2.4; booking-type applicability noted on all statuses
- Closed general inquiry retention added to Section 2.6 (12 months from submission)
- Phase 1 lifecycle diagram updated to show the general inquiry branch
- Section 9 inquiry acknowledgement split into two variants

**Changes from v0.10:**
- C4 resolved: Cancellation fully defined — admin-only, from any non-terminal status; cancellation type (Guest-initiated / Property-initiated) required; reason optional; cancellation email sent automatically with deposit clause conditional on type and timing
- New FR-76 through FR-79: cancel action, cancellation type, optional reason, cancellation email
- `CancelledAt`, `CancellationInitiator`, `CancellationReason` fields added to Booking model (Section 8.1)
- Cancellation email added to Section 9
- Section 2.4 status reference updated with reachability notes
- Lifecycle diagrams updated to show cancellation paths

**Changes from v0.9:**
- C3 resolved: Magic link minimum window rule added — token always valid for at least 48 hours from issue regardless of check-in proximity; Section 2.5 and FR-53 updated
- New FR-75: admin receives a warning when approving a booking with check-in within 14 days, stating the shortened token expiry window; approval is not blocked

**Changes from v0.8:**
- C2 resolved: Full-board vs self-catering is declared at booking level on the inquiry form (`MealPlan`: FullBoardAll / SelfCateringAll / Mixed) and overridable per participant on the completion form (`IsFullBoard` boolean, pre-populated from booking default)
- `MealPlan` enum field added to Booking model (Section 8.1)
- `IsFullBoard` boolean field added to Participant model (Section 8.5)
- FR-03 updated: inquiry form now includes meal plan selection
- FR-57 updated: completion form collects `IsFullBoard` per participant; meal selection only shown for full-board participants
- Section 4.1 updated to describe the three meal plan options and per-participant override
- Day guests clarified: only applicable when `MealPlan` is `FullBoardAll` or `Mixed` — not valid for `SelfCateringAll` bookings

**Changes from v0.7:**
- C1 resolved: Admin authentication fully defined — ASP.NET Core Identity, email + password login at `admin.akerogarden.se`, 30-day "remember me" persistent cookie, password reset via one-time email link; multiple admin accounts supported in v1 (designed for 2–3); all admins have equal access — role differentiation remains deferred
- New FR-70 through FR-74: admin login page, remember-me session, password reset, admin account creation, admin account deactivation
- New Section 6.4 "Admin — Authentication and Account Management"; existing Sections 6.4–6.7 renumbered to 6.5–6.8
- ASP.NET Core Identity added to technology decisions (Section 1.5)
- "Multi-admin with role-based permissions" in Section 10 updated: multiple accounts now v1; role-based permissions remain deferred

**Changes from v0.6:**
- Q8 resolved: No buildings or rooms at Åkerögården are accessible — all `IsAccessible` values are false; Section 3.1 updated for Tallstugan (was "Unknown"), Ljusterö, Vätö 1, and Vätö 2 (were "Accessible") — Q8 removed from Open Questions
- Q10 resolved: Spelrum price is 1 500 kr per day, cleaning included — price updated in Section 5.4; no separate cleaning fee
- Q11 resolved: Spelrum cleaning is included in the 1 500 kr/day facility price; standalone Spelrum cleaning row removed from Section 5.5 — Q10 and Q11 removed from Open Questions

**Changes from v0.5:**
- Approval email scope clarified: must state cancellation/deposit policy (Section 5.6) in addition to the expiry date
- Denial email now requires an admin-entered reason, shown to the guest — new `DenialReason` field on Booking
- New FR-50: admin must enter a denial reason when setting a booking to `Denied`, which is included in the denial email
- Resolved contradiction between deferred Swedish admin UI and "Out of Scope" multi-language exclusion: English admin UI confirmed for v1, with Swedish admin UI now explicitly tracked as a near-term post-launch item (not someday-maybe), given NFR-19's non-technical Swedish-speaking admin
- New NFR-25: PostgreSQL Flexible Server uses private network access only (VNet integration), replacing the old Windows-firewall-based "block port 5432" approach
- New Section 7.7 Testing (NFR-26–28): unit tests for business logic (pricing, retention, token expiry) and integration tests for all API endpoints, required to pass in CI — addresses the old system's documented "no test suite" gap
- NFR-04 rate limiter now requires a distributed store (Azure Cache for Redis) from day one rather than in-memory state, avoiding the old system's documented scaling known issue. Added to Technology Decisions (1.5) and Hosting cost table (Section 11, ~$16/month added)
- New NFR-29: quarterly backup restore drill required, addressing README's "test the restore procedure" step that had no equivalent here
- New NFR-30: SMTP credentials stored as encrypted secrets, not plain text/source control
- New NFR-31: end-to-end email delivery verified against production SMTP before go-live
- README.md deleted — all content migrated to requirements.md (this document), design.md, and operational-tasks.md
- Q7 resolved: early check-in/late check-out surcharge is admin discretion, no fixed price, arranged outside the system — Section 2.7 updated, Q7 removed from Open Questions
- Q9 resolved: game room named "Spelrum" — Q9 removed from Open Questions
- Q10 resolved (structurally): Spelrum is selectable in the booking inquiry form and has its own cleaning line item, same as Kapell/Konferensrum. Q10 narrowed to just the pending price figures (placeholders added to Sections 5.4 and 5.5)
- Section 2.1 lifecycle diagram updated: denial node now shows the admin-entered reason, approval node now shows the deposit/cancellation policy — diagram had drifted out of sync with Section 9/FR-50
- Reversed the v0.6 "English admin UI" decision: the entire system (public site and admin panel) is Swedish-only — English was never actually needed, not even as a deferred item. New NFR-32; removed "Swedish admin UI" from both Out of Scope lists in Section 10
- New Q11: whether Spelrum's cleaning should be its own line item in Section 5.5 (which is otherwise building-level zones only) or folded into its containing building's package — flagged, not yet resolved
- Resolved conflict between booking-level `GuestChoosesRooms` and the per-participant "choose for me" flag: room choice is booking-level only. FR-56/57 rewritten — the completion form is filled in by the booking's contact person on behalf of the whole group (not by each participant individually), and only shows a room field at all when `GuestChoosesRooms` is true. Clarified that participant names are collected for fire-safety/headcount purposes
- Renumbered FR-50 through FR-69 (previously the denial-reason requirement was appended out of sequence as FR-69 after FR-49). It's now FR-50 in its correct position in Section 6.4, and every subsequent FR shifted up by one — FR-01 through FR-69 now read sequentially top to bottom

**Changes from v0.4:**
- Q1 resolved: meal selection is per day (lunch & dinner), with a "same for all days" shortcut on the completion form
- Q2 resolved: admin can resend the magic link manually; approval email must state the deadline date explicitly
- Q3 resolved: both options available — admin can edit data directly in the panel, or resend the magic link for the guest to resubmit
- Q4/Q5/Q6 resolved: pricing is per person (headcount), not per room/bed capacity. Section 5.3 rewritten accordingly
- Day guests added as a new participant type: no bed, meals only (Q6 insight)
- New FR: admin can block a specific room or building for a date range (e.g. renovation)
- New FR: admin can create a booking on behalf of a guest, bypassing the public inquiry form
- New FR: day guest participant type supported (meals, no overnight)
- New FR: completion form includes "same for all days" shortcut for meal selection
- Smedjan corrected: separate cabin, 1 room, 1 double bed, 2 persons, not accessible
- New facility added: Spelrum (game room) — foosball, pinball, shuffleboard
- Check-in 14:00 / check-out 12:00 confirmed; early/late surcharge is an open question
- Section 5.3 room pricing model moved to Out of Scope (future release)
- New open questions: Q7 (check-in/out surcharge), Q8 (Tallstugan accessibility), Q9 (Spelrum name), Q10 (Spelrum in booking/cleaning flow)

