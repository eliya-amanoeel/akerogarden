# Åkerögården — Architect's Review
## Pre-Development Findings

**Date:** June 2026  
**Based on:** requirements.md v0.7, design.md, operational-tasks.md  
**Purpose:** Track all gaps, contradictions, and open decisions identified during architectural review. Each item must be resolved and reflected in requirements.md before the relevant feature is built.

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

*Generated from architect's review of requirements.md v0.7 — all 19 items resolved as of requirements.md v0.18, June 2026*