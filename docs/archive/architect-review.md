# Åkerögården — Architect's Review
## Pre-Development Findings

**Date:** June 2026  
**Based on:** requirements.md v0.7, design.md, operational-tasks.md  
**Purpose:** Track all gaps, contradictions, and open decisions identified during architectural review. Each item must be resolved and reflected in requirements.md before the relevant feature is built.

> **Archived — closed, not maintained.** Every item below is marked RESOLVED and folded into `requirements.md`, which supersedes this document on all points of fact. It is kept for one reason: it is the only record of the *pre-build design debate* — why several requirements are worded the way they are, and which alternatives were considered and rejected. Read it for rationale, never for current truth. The `v0.x` references throughout point at SRS versions that no longer exist; they resolve against `requirements-changelog.md` (frozen history). Not to be appended to — this is a closed artefact, and Phase 11's post-launch architecture review is a separate document.

---

## How to Use This File

Work through items top to bottom — Critical items first, as many of them affect the data model. Resolving a lower item may become easier once a higher one is settled. When an item is resolved: check it off here, update requirements.md, and note the decision briefly in the changelog.

---

## Critical — Blocks Development

These items have direct implications on the data model schema or core architecture. Do not start building until all five are answered.

---

### C1 — Admin authentication is completely unspecified

> **RESOLVED — requirements.md v0.8**

**Decisions made:**
- **Credential system:** ASP.NET Core Identity (email + password)
- **Login URL:** `admin.akerogarden.se/login`
- **Session:** "Remember me" 30-day persistent secure HTTP-only cookie
- **Password reset:** One-time link sent to registered email, expires after 1 hour
- **Accounts:** Multiple admin accounts supported in v1 (designed for 2–3); all admins have equal access; one admin can create or deactivate others (cannot deactivate own account)
- **User storage:** ASP.NET Core Identity tables — no custom `AdminUser` entity in the application data model
- **Role-based permissions:** Still deferred (Section 10 updated)

**Changes made to requirements.md v0.8:** New FR-70–74; new Section 6.4; Section 1.5 updated; NFR-02 updated; FR-35 updated; Section 10 deferred list updated; Section 11 hosting table updated.

---

### C2 — Self-catering vs full-board is not captured anywhere

> **RESOLVED — requirements.md v0.9**

**Decisions made:**
- **Booking-level default** declared on the inquiry form: `MealPlan` enum (`FullBoardAll | SelfCateringAll | Mixed`)
- **Participant-level override** on the completion form: `IsFullBoard` boolean per participant, pre-populated from the booking default, overridable by the contact person and by the admin
- **Day guests** are only applicable for `FullBoardAll` or `Mixed` bookings — hidden from the inquiry form when `SelfCateringAll` is selected
- Meal selection on the completion form renders only for participants where `IsFullBoard = true`

**Changes made to requirements.md v0.9:** Section 4.1 rewritten; Section 4.2 updated with day guest restriction; FR-03 updated; FR-55 and FR-57 updated; `MealPlan` added to Booking model; `IsFullBoard` added to Participant model; pricing sections 5.1/5.2 annotated.

---

### C3 — Magic link can be expired the moment it is generated

> **RESOLVED — requirements.md v0.10**

**Decisions made:**
- **Minimum window:** 48 hours from issue, always — regardless of check-in proximity
- **Approval behaviour:** system warns the admin ("completion link will expire in 48 hours — consider contacting the guest directly") but does not block approval
- Token invalidation on re-generation also captured here: generating a new token immediately invalidates any existing token on that booking

**Changes made to requirements.md v0.10:** Section 2.5 updated with minimum floor rule, warning behaviour, and token invalidation on re-generation; FR-53 updated; new FR-75 added. L5 also resolved as a side effect.

---

### C4 — Cancellation has no functional requirements and no email

> **RESOLVED — requirements.md v0.11**

**Decisions made:**
- **Who can cancel:** Admin only (guest requests via phone/email; admin acts on it in the panel)
- **From which statuses:** `Approved`, `DetailsComplete`, `Active` — not from `Completed` or `Denied`
- **Cancellation type required:** `GuestInitiated | PropertyInitiated` — controls deposit clause in the email
- **Cancellation reason:** Optional free-text internal note
- **Cancellation email:** Sent automatically; deposit clause is conditional — Guest-initiated + >2 months: full refund; Guest-initiated + <2 months: forfeited; Property-initiated: full refund regardless

**Changes made to requirements.md v0.11:** New FR-76–79; `CancelledAt`, `CancellationInitiator`, `CancellationReason` added to Booking model; cancellation email added to Section 9; lifecycle diagrams updated; Section 2.4 status reference updated.

---

### C5 — General inquiry lifecycle is not defined

> **RESOLVED — requirements.md v0.12**

**Decisions made:**
- **Lifecycle:** `Pending` → admin reads and replies outside the system → admin marks `Closed`. No approve/deny
- **`Closed`** is a new status, general inquiries only — "Denied" is not repurposed
- **Auto-reply email** has different content from the stay-request variant (FR-05 updated)
- **GDPR retention:** 12 months from submission date (same as pending no-response)
- **Promotion to stay request:** not supported — guest submits a new inquiry if they want to book
- General inquiries appear in the same booking list as stay requests (filterable by type via FR-36)

**Changes made to requirements.md v0.12:** Phase 1 diagram updated; `Closed` added to Section 2.4; booking-type column added to status table; Section 2.6 retention table updated; FR-05 updated; FR-80 added; Section 9 acknowledgement email split into two variants.

---

## Important — Resolve Before the Affected Feature is Built

---

### I1 — SiteSettings entity is missing from the data model

> **RESOLVED — requirements.md v0.13**

**Decision:** FR-46 dropped entirely. Hero text, about text, contact details, and social links are hardcoded — they are not subject to change and do not need admin management. No `SiteSettings` entity needed.

Prices and menu are now shown publicly (new FR-82, FR-83), served dynamically from the existing admin-managed records. The public facilities page (FR-81) is also explicitly connected to the admin-managed facility records. No separate content management layer required.

---

### I2 — Public calendar: 2 states or 3 states — pick one

> **RESOLVED — requirements.md v0.14**

**Decision:** Three states — Free / Partially booked / Fully booked.

**Logic:** Based on sum of `OvernightGuestCount` across approved bookings for that date vs. total capacity (79 beds). Free = 0, Partial = 1–78, Full = 79+. A publicly visible admin blocked period covering all buildings also makes a date fully booked.

A disclaimer note on the public calendar page clarifies that partial availability is subject to admin confirmation (FR-10a).

**Changes made to requirements.md v0.14:** FR-09 updated to three states; FR-10 rewritten with bed-count logic; FR-10a added for the disclaimer note. Section 2.3 diagram was already correct.

---

### I3 — Day guest breakfast attendance has no data model representation

> **RESOLVED — requirements.md v0.15**

**Decision:** `AttendsBreakfast` boolean added to `Participant` (day guests only — overnight guests always attend breakfast, no selection needed). Breakfast attendance is a simple checkbox on the completion form; lunch/dinner use `MealPreference` records as normal.

Minor open note: whether breakfast-only day guests (no lunch/dinner) are a valid scenario is not yet decided — it does not affect the model and can be resolved as a form validation detail during development.

---

### I4 — FR-03 uses "facility" to mean "building zone" in the cleaning context

> **RESOLVED — requirements.md v0.16**

**Fix:** FR-03 reworded to distinguish "bookable facilities" (Kapellet, Spelrum, etc.) from "building zones" (cleaning packages). Both concepts now use consistent terminology throughout the requirement.

---

### I5 — `WarningEmailSentAt` field missing from Booking

> **RESOLVED — requirements.md v0.17**

`DeletionWarningEmailSentAt` (nullable datetime) added to the Booking model. The daily background job checks this field before sending — sends once, then sets the field so it never fires again for the same record.

---

### I6 — .NET version is not specified

> **RESOLVED — requirements.md v0.17**

Pinned to **.NET 10 LTS** (released November 2025, supported until November 2028). .NET 9 is EOL as of May 2026; .NET 8 ends November 2026 — both poor choices for a new project. Section 1.5 updated.

---

### I7 — Blazor rendering mode should be explicitly specified

> **RESOLVED — requirements.md v0.17**

Rendering modes specified in Section 1.5: **Static SSR** for all public-facing pages (plain HTML, SEO-friendly, no SignalR circuit overhead); **Interactive Server** for the admin panel. Section 1.3 scope description updated accordingly.

---

## Low Priority — Resolve Before Affected Feature Starts

---

### L1 — Public calendar navigation range not defined

> **RESOLVED — requirements.md v0.18**

12 rolling months forward from today; no backward navigation. FR-84 added.

---

### L2 — Image upload mechanics not specified

> **RESOLVED — requirements.md v0.18**

JPEG, PNG, WebP accepted; 5 MB max; inline error on violation. FR-27 updated.

---

### L3 — "Suggest alternative dates" in denial email is ambiguous

> **RESOLVED — requirements.md v0.18**

Fixed Swedish template line: *"Kontakta oss för att diskutera alternativa datum."* No admin-entered field. Section 9 updated.

---

### L4 — Booking status transition rules not defined

> **RESOLVED — requirements.md v0.18**

Full state machine added to Section 2.5. All transitions enforced by the system — invalid transitions blocked.

---

### L5 — Token behaviour on re-generation is not explicit

> **RESOLVED — requirements.md v0.10** (captured in C3)

---

### L6 — Two-week reminder when completion form is not yet submitted

> **RESOLVED — requirements.md v0.18**

When booking is still `Approved` at the 14-day mark: guest receives a nudge ("complete your booking details") AND admin receives an alert ("guest hasn't completed — consider following up"). FR-63 updated.

---

### L7 — "Hela lägret" cleaning package scope is not defined

> **RESOLVED — requirements.md v0.18**

Hela lägret covers all four building zones (Huvudbyggnad, Lilla bostadslängan, Konferensbyggnad, Stora bostadslängan). Spelrum cleaning handled by its facility fee — excluded. Selecting Hela lägret disables individual zone checkboxes — mutually exclusive. Section 5.5 updated.

---

## Summary Checklist

| ID | Item | Status |
|---|---|---|
| C1 | Admin authentication mechanism | **Resolved v0.8** |
| C2 | Self-catering vs full-board field | **Resolved v0.9** |
| C3 | Magic link minimum window rule | **Resolved v0.10** |
| C4 | Cancellation — FRs, email, `CancelledAt` field | **Resolved v0.11** |
| C5 | General inquiry lifecycle | **Resolved v0.12** |
| I1 | `SiteSettings` entity definition | **Resolved v0.13** |
| I2 | Calendar: 2 states vs 3 states | **Resolved v0.14** |
| I3 | Day guest breakfast attendance model | **Resolved v0.15** |
| I4 | FR-03 "facility" vs "building zone" terminology | **Resolved v0.16** |
| I5 | `DeletionWarningEmailSentAt` field on Booking | **Resolved v0.17** |
| I6 | .NET version pinned | **Resolved v0.17** |
| I7 | Blazor rendering mode per page type | **Resolved v0.17** |
| L1 | Calendar navigation range | **Resolved v0.18** |
| L2 | Image upload mechanics | **Resolved v0.18** |
| L3 | Denial email — specific dates or generic line | **Resolved v0.18** |
| L4 | Booking status transition state machine | **Resolved v0.18** |
| L5 | Token invalidation on re-generation | **Resolved v0.10** (captured in C3) |
| L6 | Two-week reminder when form not yet submitted | **Resolved v0.18** |
| L7 | "Hela lägret" cleaning package scope | **Resolved v0.18** |

---

# Round 2 — Review of v0.18 (pre-ERD)

**Date:** June 2026
**Based on:** requirements.md v0.18, design.md, operational-tasks.md
**Reviewer:** Senior solution architect pass (cross-section consistency + edge analysis)

Round 1 (C1–C5, I1–I7, L1–L7) was a question-resolution pass — each item raised one open question and recorded a decision. This round is different: it reads the spec as one integrated system and checks the **seams between sections**, the **follow-through of earlier decisions into the data model**, and the **edges the prose glosses over**. As a result, most items below are not new product questions — they are contradictions introduced when sections were edited in isolation, decisions from Round 1 that were never propagated into §8, or undefined boundary behaviour.

**Numbering:** continues the Round 1 series — Critical `C6–C10`, Important `I8–I14`, Low/hygiene `L8–L15`.

---

## Critical — Blocks Development (data-model impact)

Resolve all five before the ERD is drawn. C6 in particular adds at least one entity.

---

### C6 — Cleaning "building zones" have no entity and contradict I4

> **RESOLVED — requirements.md v0.19**

**Decisions made:**
- **New `CleaningZone` entity** (admin-managed: name, description, amount ex. VAT, VAT rate, `IncludesZoneIds`, active, display order) — Section 8.11. Distinct from both `Facility` and accommodation `Building`, resolving the I4 violation.
- **`Booking.CleaningSelections` (GUID[] → Facility) removed**, replaced by a `BookingCleaningSelection` child entity (Section 8.12) — one row per selected zone, snapshotting zone name and amount at booking time so the agreed price survives later edits to the zone.
- **No zone↔building mapping** — unnecessary, since the system does no automatic pricing or filtering. The full list of active zones is shown on the inquiry form; FR-03's uncomputable "applicable zones" filter dropped.
- **Hela lägret** modelled generically via `IncludesZoneIds` (the four zone IDs it covers); selecting it disables those zones on the form.
- **Admin CRUD for cleaning zones was entirely missing** — new Section 6.3.6 (FR-85–88) added.

**Changes made to requirements.md v0.19:** New entities Section 8.11 and 8.12; `Booking.CleaningSelections` row replaced (Section 8.1); FR-03 and Section 5.5 reworded; new Section 6.3.6 (FR-85–88); FR-82 extended to render cleaning zones publicly; NFR-14 extended; new NFR-33 (cleaning snapshot). Stale footer version stamp corrected (L9 closed as a side effect).

---

### C7 — Public calendar counts only `Approved` bookings, so confirmed bookings disappear from it

> **RESOLVED — requirements.md v0.20**

**Decision:** Calendar occupancy counts bookings in `Approved`, `DetailsComplete`, or `Active` status — termed "occupying bookings." `Pending` does not count (availability is subject to admin confirmation); `Denied`, `Cancelled`, `Completed`, and `Closed` never count. (The separate question of how the `IsPubliclyVisible` flag interacts with real availability is tracked under I9, not changed here.)

**Changes made to requirements.md v0.20:** FR-10 reworded to define the occupying-status set; Section 2.3 calendar diagram updated to match.

---

### C8 — No monetary total is stored, yet deposit is "20% of total"; participants have no age category

> **RESOLVED — requirements.md v0.21**

**Decisions made:**
- **Money:** Confirmed as a deliberate choice — the system stores and computes **no** monetary amounts (no total, deposit amount, or balance). Deposit/final tracking stays purely paid/unpaid status; the 20%/80% email figures are fixed policy wording. Stated explicitly in §5.6.
- **Age:** New `AgeCategory` enum (`Adult | Child | Infant`) on `Participant`, collected per participant on the completion form — so accommodation tiers (standard vs free infant) and the free-infant headcount are derivable, and the kitchen can size child portions. No birthdates stored (GDPR-friendly). Captured at completion, not inquiry (inquiry keeps a single `OvernightGuestCount` for availability).

**Note — feeds I10:** the exact Child/Infant age boundary (the existing "4 and over" vs "under 3" gap) is still open and now belongs to I10; C8 fixes the data shape, I10 will fix the threshold.

**Changes made to requirements.md v0.21:** §5.6 note extended; §5.1/§5.2 annotated; `AgeCategory` added to §8.5 Participant; FR-57 and FR-58 updated.

---

### C9 — Retention has functional requirements for only 2 of its 4 cases

> **RESOLVED — requirements.md v0.22**

**Decisions made:**
- New **FR-89** — `Pending` records auto-deleted 12 months after submission.
- New **FR-90** — `Closed` general inquiries auto-deleted 12 months after submission.
- New **FR-91** — defines exactly when `DeleteAfter` is (re)computed for every state, and confirms the live in-progress states (`Approved`/`DetailsComplete`/`Active`) are never auto-deleted. Both the FR-66 warning and the FR-69 job run off `DeleteAfter`.

All four §2.7 retention cases now have a matching functional requirement.

**Changes made to requirements.md v0.22:** New FR-89–91 in §6.8; §8.1 `DeleteAfter` note updated to reference FR-91.

---

### C10 — "Resend the link anytime" contradicts the state machine

> **RESOLVED — requirements.md v0.23**

**Decision: admin-edits-only.** After a guest submits, the booking is `DetailsComplete` and the completion link is never re-issued. Any correction is made by the admin directly in the panel (FR-48), with no status change. This keeps the §2.5 state machine one-way (no reopen) and removes the contradiction. The more guest-friendly "admin reopen" option was considered and declined for v1 (more machinery, edge cases around already-downloaded lists).

- FR-49 rewritten and scoped to the `Approved` (awaiting-completion) state, whether or not the previous link has expired.
- FR-43 merged into FR-49 — token regeneration and resend are now a single requirement.
- §2.5 annotated with an explicit "no reopen after completion" note; §2.6 magic-link rule scoped to match (was "at any time").

**Changes made to requirements.md v0.23:** FR-49 rewritten; FR-43 tombstoned (merged); §2.5 and §2.6 updated. FR-62 (single-use, invalidated on submit) unchanged — now consistent.

---

## Important — Resolve Before the Affected Feature is Built

---

### I8 — Two overlapping blocking mechanisms (`RoomBlock` vs `BlockedPeriod`)

> **RESOLVED — requirements.md v0.25**

**Decision: merged into one entity.** `RoomBlock` and `BlockedPeriod` are now a single `BlockedPeriod` entity with a `Scope` field (`WholeProperty | Building | Room`). One feature, one admin screen. Every block reduces the date's effective capacity for its scope, so a blocked building now correctly reduces public-calendar availability (the old bug). `AffectedBuildingIds` dropped.

**Changes made to requirements.md v0.25:** §8.10 generalized with `Scope`/`BuildingId`/`RoomId`; §8.4 `RoomBlock` tombstoned (merged); FR-10 recomputed against effective capacity; FR-21 and §3.3 reworded. The `IsPubliclyVisible` vs real-availability nuance remains tracked under I9.

---

### I9 — Hidden blocks let guests inquire on unavailable dates

> **RESOLVED — requirements.md v0.26**

**Decision: remove `IsPubliclyVisible` entirely.** It was a footgun (a hidden booking/block showed dates as available when they weren't) and served no privacy purpose — the calendar already exposes only aggregate Free/Partial/Full (NFR-07). All occupying bookings and all blocks now always count toward public availability. The "private stay / internal hold" need is covered by an ordinary admin-created booking (FR-47) or a block — both of which now correctly reduce availability.

**Changes made to requirements.md v0.26:** `IsPubliclyVisible` dropped from `Booking` (§8.1) and `BlockedPeriod` (§8.10); FR-12 and FR-41 removed (tombstoned); FR-10 simplified (no "publicly visible" qualifier).

---

### I10 — Boundary conditions undefined at the edges

> **RESOLVED — requirements.md v0.27**

**Decisions made:**
- **Deposit:** more than 2 months before arrival → full refund; **2 months or less → forfeited** (exactly 2 months = forfeited). §5.6, FR-79, §9 updated.
- **Age tiers:** free/paid line is **age 4** — `Infant` = 0–3 (free), `Child` = 4–12, `Adult` = 13+ (Child/Adult split is kitchen-portion-only, no price effect). §5.1/5.2 and §8.5 updated.
- **Token thresholds** evaluated as exact timestamps — valid up to the expiry instant, rejected at/after it. §2.6 clarified.
- **FR-75** admin warning fires when check-in is **14 days away or sooner** (≤14). FR-75 and §2.6 wording aligned.

**Changes made to requirements.md v0.27:** §5.1/5.2 age rows; §8.5 `AgeCategory` bands; §5.6 deposit table; FR-79; §9 cancellation + approval rows; §2.6 token bullet + warning wording; FR-75.

---

### I11 — Per-day meal counting is ambiguous (arrival/departure days)

> **RESOLVED — requirements.md v0.29**

**Decision:** arrival day = dinner only; middle days = breakfast + lunch + dinner; departure day = breakfast only (based on check-in 14:00 / check-out 12:00). The completion form shows a meal picker only for meals that occur on each day, so a one-night stay collects a single dinner selection.

**Changes made to requirements.md v0.29:** §4.1 meals-by-day rule added; FR-57 scoped to applicable days.

---

### I12 — Multi-day day-guest attendance is not modelled

> **RESOLVED — requirements.md v0.29**

**Decision:** day guests are single-day. A new `AttendanceDate` field (day guests only) records the one date attended; a visitor across multiple days is entered as one day-guest record per date. This makes per-day kitchen headcount exact and lets `AttendsBreakfast` apply to a specific day.

**Changes made to requirements.md v0.29:** `AttendanceDate` added to §8.5 Participant; §4.2 and FR-58 updated.

---

### I13 — Token expiry collides with the reminder/nudge timing

> **RESOLVED — requirements.md v0.28**

**Decision:** when the 14-day nudge fires and the booking is still `Approved`, the system **regenerates the completion token** (fresh link, ≥48 h per §2.6, invalidating the prior one) and includes it in the nudge; the admin alert states the new expiry. This also covers the broader case where the token expired even earlier (bookings approved well in advance, where it dies at issue+30 days). No change to the expiry formula needed.

**Changes made to requirements.md v0.28:** FR-63 updated to regenerate-and-include the link; §9 completion-nudge and admin-alert rows updated.

---

### I14 — Room assignments are text-only, so conflicts can't be detected

> **RESOLVED — requirements.md v0.30**

**Decision: add a live `RoomId` FK to `Participant`** (alongside the existing `RoomAssignmentSnapshot` text, kept for history per NFR-16). A separate `RoomAssignment` entity was considered but rejected — one overnight participant maps to exactly one room, so a foreign key on `Participant` is sufficient and simpler. The FK enables room-conflict detection, occupancy views, and exclusion of taken rooms.

**Changes made to requirements.md v0.30:** `RoomId` added to §8.5 Participant; FR-56 updated to exclude already-occupied rooms and flag double-assignment conflicts in the admin panel.

---

## Low Priority — Documentation Hygiene & Minor Consistency

---

### L8 — FR-01 page list is stale

> **RESOLVED — requirements.md v0.24** — FR-01 now lists the menu and prices pages (was inconsistent with §1.3 and FR-82/FR-83).

### L9 — Footer version stamp is wrong

> **OPEN** — The document is v0.18 but the closing line reads *"Version 0.6 Working Draft."* Correct to current version.

### L10 — §9 email catalog is incomplete

> **RESOLVED — requirements.md v0.24** — §9 now includes the completion-nudge (guest) and not-completed-alert (admin) emails; the two-week reminder row scoped to `DetailsComplete`-or-later.

### L11 — design.md describes the wrong stack

> **RESOLVED — design.md updated** — Architecture and Logo sections rewritten for .NET 10 Blazor (Static SSR, `wwwroot/css`, `Logo.razor`/`Header.razor`, scroll behaviour via JS/CSS since public pages are non-interactive). Tokens/colours unchanged.

### L12 — operational-tasks.md is stale

> **RESOLVED — operational-tasks.md updated** — Spelrum task unblocked (Q9/Q10 noted resolved); `SiteSettings` reference removed (hero/about images are hardcoded page assets).

### L13 — General inquiries share the Booking table with non-nullable stay fields

> **RESOLVED — requirements.md v0.24** — §8.1 now states stay-specific fields are null/unused for `GeneralInquiry` bookings (stored nullable).

### L14 — NFR-10 vs unbounded day guests

> **RESOLVED — requirements.md v0.24** — NFR-10 target raised to 300 participants; larger lists must still succeed but aren't covered by the 10-second guarantee.

### L15 — Flat 12% VAT assumption

> **RESOLVED — requirements.md v0.24** — §5 no longer asserts a blanket 12%; VAT is per `VatRate` (price rule / cleaning zone). Residual: exact rate per category is admin-entered config to confirm with the client's accountant (operational, not a spec gap).

---

## Round 2 Summary Checklist

| ID | Item | Severity | Status |
|---|---|---|---|
| C6 | Cleaning zones: no entity, contradicts I4 | Critical | **Resolved v0.19** |
| C7 | Calendar counts only `Approved` bookings | Critical | **Resolved v0.20** |
| C8 | No monetary total stored; no participant age category | Critical | **Resolved v0.21** |
| C9 | Retention FRs cover only 2 of 4 cases | Critical | **Resolved v0.22** |
| C10 | Resend-link contradicts state machine | Critical | **Resolved v0.23** |
| I8 | `RoomBlock` vs `BlockedPeriod` overlap | Important | **Resolved v0.25** |
| I9 | Hidden blocks vs public availability | Important | **Resolved v0.26** |
| I10 | Undefined boundary conditions | Important | **Resolved v0.27** |
| I11 | Per-day meal counting (arrival/departure) | Important | **Resolved v0.29** |
| I12 | Multi-day day-guest attendance not modelled | Important | **Resolved v0.29** |
| I13 | Token expiry collides with reminder timing | Important | **Resolved v0.28** |
| I14 | Room assignments text-only, no conflict detection | Important | **Resolved v0.30** |
| L8 | FR-01 page list stale (menu/prices) | Low | **Resolved v0.24** |
| L9 | Footer version stamp wrong | Low | **Resolved v0.19** (with C6) |
| L10 | §9 email catalog incomplete | Low | **Resolved v0.24** |
| L11 | design.md describes wrong (React) stack | Low | **Resolved** (design.md) |
| L12 | operational-tasks.md stale (Q9/Q10, SiteSettings) | Low | **Resolved** (ops-tasks) |
| L13 | General-inquiry shared-table nullability | Low | **Resolved v0.24** |
| L14 | NFR-10 cap vs unbounded day guests | Low | **Resolved v0.24** |
| L15 | Flat 12% VAT assumption | Low | **Resolved v0.24** |

**Recommended order of attack:** C6 first (adds an entity — needed before the ERD), then C7–C10, then I8–I14, then sweep L8–L15. Re-check each resolution against §8 *and* the affected FRs before marking it resolved — that propagation step is what Round 1 missed.

---

*Round 2 generated from architect's review of requirements.md v0.18, June 2026 — 20 items raised (C6–C10, I8–I14, L8–L15). C6 and L9 resolved in v0.19, C7 in v0.20, C8 in v0.21, C9 in v0.22, C10 in v0.23; hygiene sweep L8 and L10–L15 resolved in v0.24 (L11 in design.md, L12 in operational-tasks.md); I8 in v0.25, I9 in v0.26, I10 in v0.27, I13 in v0.28, I11+I12 in v0.29, I14 in v0.30; **0 open — all 20 Round 2 items resolved.** A post-review consistency pass (v0.31) then fixed inconsistencies introduced across the incremental edits: FR-76 aligned to the §2.5 state machine; §5.5 "building zones"→"cleaning zones"; §1.3 + §10 stale scope wording; §8.1 DayGuestCount clarified. The ERD pass (erd.md) then surfaced one asymmetry — requested facilities were a bare ID array while cleaning had a snapshot child entity — resolved in v0.32 by adding `BookingFacilitySelection` (mirrors `BookingCleaningSelection`). **requirements.md is at v0.32; erd.md produced.***

*Generated from architect's review of requirements.md v0.7 — all 19 items resolved as of requirements.md v0.18, June 2026*