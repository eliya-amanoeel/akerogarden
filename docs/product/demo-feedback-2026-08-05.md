# Demo feedback — client + end-user demo, 2026-08-05

Feedback captured from the client demo held 2026-08-05. This document is the working
record for that feedback: what was asked, what the system does today, what is still
undecided, and where each item lands.

**Status of this document:** all items are captured, triaged (launch-blocking except 1b) and
**framed** — each under a "Framing" subsection recording the decision *and its reasoning*, so
a future reader can tell a deliberate choice from an accident. The agreed working order is:
(1) capture — this document; (2) frame each item into precise language together; (3) reflect
every agreed change in `requirements.md`, `erd.md`, `architecture.md`,
`implementation-plan.md` and `docs/legal/` **before any code is written**; (4) implement.

**Framed and fully decided:** 1a (group-wide meals), 2a (building assignment), 2b (`Samordning` role), 3 (check-in/check-out times), 4a (turnover days), 6a (token-expiry nudge), 6b (email in approve dialog), 6c (footer fix), 6d (payment checkboxes removed).
**Design corrections carried alongside (§7):** meal-occasion source of truth; availability "unavailable reasons".
**Deferred:** 1b (participant-name upload, after launch — decided, not drifted).
**Deferred:** 5a (review feature — postponed as a future feature, decided not drifted).
**Nothing left to frame.** Conflict audit run 2026-08-06 (§8) — four blocking findings resolved. Next step is the document updates (step 5 of the working order).

## How this work is sequenced

This feedback is **not a new phase**. Phase 10d (cutover) is blocked on the jurist review
of the legal text, which is outside the project's control — this work runs inside that
waiting window at no schedule cost.

**Triage decision (user, 2026-08-06): every item below is BLOCKS-LAUNCH, with one
deliberate exception — 1b (participant-name file upload) is AFTER-LAUNCH.** All other demo
feedback ships before the 10d cutover: the client saw the system and asked for these
changes, and going live on the version they critiqued is not the intent.

**Why 1b is the exception (decided 2026-08-06).** It was the only item whose *motivation*
was unknown — every other item has an obvious driver, while this one was being reasoned
about backwards from a proposed solution. Critically, leaving it **undecided** would have
blocked the launch: the privacy policy cannot be sent to the jurist while it is unresolved
whether the system stores uploaded files containing guest names, and that review is the
launch gate. Deferring it *as an explicit decision* keeps the policy clean and lets the
review proceed. Deferring it *by drift* would not have — the two look identical in
conversation and are opposite in effect. There is no technical dependency: 1b changes how
names are entered, not what participant data is held, so it is additive to whatever shape
1a and 2a produce.

Two consequences follow, and they are the reason this decision is worth stating explicitly
rather than assuming:

1. **The jurist review must not start until the legal-affecting items are settled.** Items
   1b (guest-name PDFs) and 2b (a second role reading art. 9 dietary data) both change what
   personal data the system holds and who processes it. If the review starts first, those
   changes buy a second round and a later launch. Decide and document 1b and 2b **early**,
   even if they are built last.
2. **Launch is now gated on this work as well as on the jurist.** Previously the jurist was
   the sole long pole and this work was free inside its shadow. If the feedback work runs
   longer than the review, it becomes the critical path.

**Database:** the schema may be freely reshaped. Production holds no real data and the
pre-go-live migration-baseline exception is being extended from "10c migrates production"
to "the 10d cutover" (see `implementation-plan.md`). Any schema change here regenerates
the baseline; both staging and production databases are dropped and recreated. No
backward compatibility is owed. **Known landmine:** a regenerated baseline cannot apply to
a database that still holds the old tables — `MigrateAsync` crashes the container on boot
(this bit staging on 2026-07-14). Drop + recreate per `infra/ops/db-access-runbook.md`,
never migrate in place.

---

## 1. Completion form

### 1a. Meal selection moves from per-participant to per-booking — BLOCKS-LAUNCH, framed

**Client asked:** meals should no longer be chosen per person. There should still be
several menu options per meal sitting, but the choice applies to **the whole group**. The
client has no interest in who eats what — assigning meals per participant is irrelevant to
them. Allergies and special dietary needs stay exactly as they are (per participant).

**Current state:** meal selection is per participant, per day. `MealPreference` holds one
row per participant per meal; `Participant.IsFullBoard` is per person and overridable;
§4.1 requires a lunch and dinner selection per full-board participant per applicable day,
with a "same for all days" shortcut. FR-57 collects meal selection per participant.

**Impact — one of the two structurally large items.** It touches the `MealPreference`
entity and its snapshot pattern (NFR-15/33), FR-57, §4.1, the completion form UI, the
kitchen prep list (`Matlista`), and the ERD.

**Verified impact (code read 2026-08-06) — the kitchen list barely changes.** Phase 8
already redesigned the Matlista from a per-person table into a **kitchen prep summary**
(`ListShaper`): one row per *(day, meal, option)* with a count — "Tis 21/7 · Lunch ·
Köttbullar · 78" — plus a separate named special-diets list. The system therefore *already*
discards who-eats-what before the data reaches the kitchen: it collects 78 individual
choices and aggregates them to counts. This change makes the **input** match what the
**output** already is. `ListShaper` and the PDF/Excel generators keep the same grouping;
it simply yields one option row per meal instead of several.

> **Correction to an earlier reading of this file:** special-diet participants **are**
> included in the prep totals. The caption between the two sections says *"deras portion ska
> anpassas, inte läggas till"* — the kitchen cooks 78 lunches, five of which are adapted,
> not 83. There is no under-counting risk from a group-wide dish.

### 1a — Framing (decided 2026-08-06)

**The change.** The group makes **one meal choice per meal sitting per day**, applying to
everyone. Nobody chooses individually. Allergies remain per named person; dietary
preferences become a count on the booking.

**Decisions:**

| # | Decision | Outcome |
|---|---|---|
| Q1 | Does per-participant `IsFullBoard` survive? | **No — `Mixed` is removed entirely.** A booking is either full-board for all or self-catering for all. `MealPlan` drops to two values and `Participant.IsFullBoard` disappears, being fully derivable from the booking |
| Q2 | Group choice per day, or one for the stay? | **Per (day, meal sitting)** — the group eats whatever it wants each day, as long as it is the same for everyone. The existing "same for all days" shortcut is retained |
| Q3 | Day guests | **Named, with one meal-attendance setting shared by all day guests on the booking**, eating what the group eats. See the forced change below |
| Q4 | Does `MealPreference` survive? | **Yes, re-parented from `Participant` to `Booking`** — one row per (booking, date, meal type) holding `MealOptionId` + snapshot. Renamed **`BookingMealSelection`**, since "preference" stops being true |
| Q5 | Dietary requirements | **Split into allergies vs. preferences** — see below |

**A forced change nobody asked for, discovered while framing Q3.** Today, *which meals a
day guest attends* is implied by which `MealPreference` rows exist for them — no lunch row
means no lunch. Removing per-person meal selection **destroys that signal**: there would
otherwise be no way to distinguish a day guest who comes only for dinner from one who eats
all three, and the kitchen counts would be wrong. Explicit meal-attendance flags are
therefore required — this part is not optional. Overnight guests are unaffected; their
attendance is derived from the stay dates.

**Where those flags live — decided (user, 2026-08-06): one setting for all day guests on
the booking**, not per day guest. Three booking-level flags (breakfast / lunch / dinner)
apply to every day guest, whichever date they attend. Rationale: the client sees no
realistic case where one day guest eats one meal and another eats three.
`Participant.AttendsBreakfast` is therefore removed as well — overnight guests always
attend breakfast (derived), and day guests follow the booking-level setting.

> **Tradeoff, accepted knowingly:** if a booking ever *does* have day guests with different
> meal patterns, the model cannot express it and the kitchen count will be wrong. Accepted
> on the client-developer's read of how the property actually operates.

**Day guests stay named** (not reduced to a headcount) because allergies are recorded per
named person and the special-diets list is by name; a day guest with a nut allergy is a
safety matter.

**Q5 — allergies vs. preferences.** Today `DietaryRequirements` is a single per-person
multi-select mixing medical constraints and choices: `NONE`, `VEGETARIAN`, `VEGAN`,
`GLUTEN_FREE`, `LACTOSE_FREE`, `NUT_ALLERGY`, `OTHER` (§4.6). A nut allergy and
vegetarianism are recorded identically. **Decision: split them** — a kitchen reads "2 nut
allergies (safety)" very differently from "3 vegetarians (preference)".

One consequence is forced: **the vegetarian *flag* must stay.** Today a vegetarian has two
routes — flag themselves, or simply pick `LUNCH_VEGETARIAN`. Under group-wide choice the
second route disappears, so the flag becomes the only way to record an individual
vegetarian.

**How the split is captured — decided (user, 2026-08-06):**
- **Allergies → per named participant.** The kitchen must know *who* receives the adapted
  plate, and this is special-category health data (GDPR art. 9) already handled carefully
  throughout the system.
- **Preferences → a count on the booking.** "4 vegetarian portions" is everything the
  kitchen uses; nobody needs to know *which* four people. This stores strictly less personal
  data — a GDPR improvement, not merely a simplification.
- **The vegetarian menu options stay** (`LUNCH_VEGETARIAN` / `DINNER_VEGETARIAN`), so a
  group can choose an all-vegetarian meal for everyone. They remain published on the Mat &
  priser page (FR-83).

**Entity changes:** `MealPreference` → `BookingMealSelection`, re-parented to `Booking`
(keeps the snapshot pattern NFR-15/33 and the FR-26 hard-delete guard, which counts usage
via `MealOptionId`). `Participant` loses `IsFullBoard`, `MealPreferences` and
`AttendsBreakfast`, and keeps only **allergies** of the former `DietaryRequirements`.
`Booking` gains the three day-guest meal-attendance flags and the **dietary-preference
counts**. `MealPlan` enum drops `Mixed`.

> **Combined with 2a, `Participant` loses `IsFullBoard`, `MealPreferences`, `RoomId` and
> `RoomAssignmentSnapshot`** — reducing to a name, a type, an age category, attendance, and
> dietary data. Close to what it arguably should always have been.

**Requirement changes:** §4.1 (meal structure, `Mixed` row, meals-by-day), §4.6 (dietary
split), FR-57 (per-participant meal selection and meal-plan override removed), FR-45
(Matlista shape), FR-48 (admin per-participant overrides), §8.5 `Participant`, §8.6
`MealPreference` → `BookingMealSelection`, the day-guest rule in §4.2, and `erd.md`.

### 1b. PDF upload for the participant name list — AFTER-LAUNCH (deferred 2026-08-06)

**Client asked:** when filling in participant names, allow uploading a PDF containing all
the names instead of typing them one by one.

**Current state:** names are typed into the completion form, one row per overnight
participant, by the contact person (FR-57). Names are required for fire-safety/headcount
purposes.

**Impact:** new file-upload surface on a **public, unauthenticated** page (the completion
form is reached by magic link, not login). Blob Storage already exists. This is the item
with the largest security and GDPR surface relative to its apparent size.

**Open questions:**
- **Is the PDF a replacement for the typed list, or an attachment alongside it?** This is
  the deciding question. If it replaces typed names, the system no longer holds structured
  participant data — which breaks the participant list generation (FR), the fire-safety
  headcount, per-participant allergies (which the client wants to keep, per 1a), and
  arguably the point of the completion form. If it is an attachment the admin reads
  manually, it is a much smaller feature.
- Are the names extracted from the PDF, or is it stored as an opaque file?
- Retention: a PDF full of guest names is personal data. It must be covered by the same
  retention/deletion rules as the booking (FR-67/68/89/90/91) and named in the privacy
  policy — **which the jurist has not reviewed yet**. Adding a new category of personal
  data after the jurist review starts means a second review round.
- Upload limits, MIME/type validation, malware surface, and rate limiting on an
  unauthenticated endpoint.
- Does the client actually want PDF specifically, or "a file / a spreadsheet"? A list of
  names is far more usefully an Excel/CSV upload than a PDF, if extraction is wanted.

**Deferred to after launch (2026-08-06) — see the triage note at the top for why.**

**The open question is not technical, it is "why".** This is the only item whose motivation
is unknown. Four plausible drivers lead to four different builds:

| If the client means… | The right build is… |
|---|---|
| "Typing 78 names into a web form is miserable" | An **Excel/CSV import** — they almost certainly hold the list already |
| "We already receive a name list from the organisation" | The same import |
| "We want the original document kept on file" | A plain **attachment**; names stay typed |
| "The group leader won't use a web form at all" | A different problem; a file upload does not solve it |

**Ask the client what they are trying to avoid before designing anything.** It is one
question and it separates a half-day import feature from a week of PDF extraction that
produces unreliable fire-safety data.

**A template round-trip was proposed and reconsidered.** The idea (offer a downloadable
template, the group leader fills it in and uploads it) is a better shape than accepting an
arbitrary file, since the format is known. Two caveats: **(1)** if filled digitally as an
AcroForm, extraction is reliable — but if printed, handwritten and scanned, which many
people will do with a downloadable form, it needs OCR on handwriting, and the failure mode
is *silently wrong names on a fire-safety list*; **(2)** a template is strictly more steps
than an import — if the names are already in a spreadsheet, importing is one action, while
a template means transcribing into our format first. **PDF is the wrong format unless the
client specifically wants something printable; Excel/CSV parses perfectly and needs no
OCR.**

If day guests are ever included in such a file, it needs a participant-type column plus, for
day guests, the attendance date and meal-attendance flags (§1a framing) — natural in a
spreadsheet, awkward in a PDF. A further argument for Excel.

---

## 2. Admin

### 2a. Room assignment replaced by building assignment — BLOCKS-LAUNCH, framed

**Client asked:** the admin never assigns rooms, and who sleeps in which room is
irrelevant to the client. Instead: when the admin reviews a booking inquiry, **before
approving**, the admin assigns the **building(s)** the group will sleep in. The guests then
sort out among themselves who sleeps where inside those buildings.

**Current state:** `Booking.GuestChoosesRooms` is a booking-wide flag. True = the contact
person assigns a room per overnight participant on the completion form (FR-56/57); false =
the admin assigns rooms in the panel. `Participant.RoomId` + `RoomAssignmentSnapshot` hold
the per-person assignment. Room-level conflict detection exists (a room assigned beyond
its bed count).

**Verified impact (code read 2026-08-06) — smaller than it first appeared.**

- **The public availability calendar is untouched.** `OccupancyProvider.ComputeStates`
  computes availability as *total active-room beds − beds removed by blocks* versus *summed
  `Booking.OvernightGuestCount`*. It never reads `Participant.RoomId`. FR-10, the public
  calendar and the inquiry-form date validation are unaffected by this change.
- Room assignment is load-bearing for exactly four things: **(1)** sleeping-cleaning zone
  resolution (`ResolveSleepingCleaningSelectionsAsync` walks participant → `Room` →
  `Building` → `PhysicalBuildingId`, FR-93/§8.11/8.12); **(2)** room-conflict detection
  (`RoomConflictDetector`, `RoomAvailabilityService`); **(3)** the completion-form room
  picker (FR-56/57); **(4)** a hard-delete guard in `BuildingService` (a room referenced by
  a participant cannot be deleted).

**Assessment:** a genuine simplification, not just a preference — and it makes cleaning
pricing *simpler*, not harder. Today sleeping-cleaning rows can only resolve **after** the
guest submits the completion form, because that is when rooms are assigned; hence the
"resolve and refresh whenever assignment changes" machinery. Under the new model buildings
are known at **approval**, which is earlier than completion, so the rows can be written at
submission in one pass exactly like the `MainBuilding` and `Conference` rows already are.
The refresh path disappears entirely.

### 2a — Framing (decided 2026-08-06)

**The change.** The admin assigns one or more lodging **buildings** to a stay request before
approving it. Per-participant room assignment is removed from the system in both directions
— neither the admin nor the guest records who sleeps in which room. The group leader divides
the group across the rooms of the assigned buildings offline.

**Decisions:**

| # | Decision | Outcome |
|---|---|---|
| D1 | Is building assignment required in order to approve? | **Yes** — a precondition of approving any stay request with overnight guests. Amends §2.5 and FR-38 |
| D2 | Validate assigned beds ≥ overnight guest count? | **Warn, do not block.** Matches the existing project pattern (FR-75 warns without blocking; room conflicts were flagged, never prevented). The admin knows things the system does not |
| D3 | Keep the snapshot obligation (NFR-16)? | **Yes** — snapshot the building name + physical building name at assignment time, one level up from the removed `RoomAssignmentSnapshot`. Without it, renaming a building rewrites history |
| D4 | Does the guest see the assignment? | **Yes** — the completion form shows the assigned buildings and their rooms read-only, so the group leader can divide people up. Only *who sleeps where* is untracked, not *what we have* |
| D5 | Can the assignment change after approval? | **Yes** — editable through the booking's life (a group grows, a building breaks). Re-resolves sleeping-cleaning selections on change; needs a new audit action |
| D6 | Day guests and general inquiries | **Unaffected** — no building assignment applies |

**Entity changes:**

- **New** booking↔building assignment (many-to-many, one row per assigned building) carrying
  the D3 name snapshot.
- **`Room` survives** as capacity reference data — `OccupancyProvider` sources total capacity
  from `activeRooms.Sum(r => r.BedCount)`, so removing it would leave the availability
  calendar with no capacity at all. It simply stops being an assignment target.
  `BlockedPeriod`'s `Room` scope also stays meaningful.
- **Removed:** `Participant.RoomId`, `Participant.RoomAssignmentSnapshot`,
  `Booking.GuestChoosesRooms` (+ `SetGuestChoosesRoomsAsync`,
  `AuditAction.GuestChoosesRoomsChanged`), `RoomConflictDetector`, `RoomAvailabilityService`,
  `ParticipantEdit.RoomId`, and the completion-form room picker.

**Requirement changes:**

- **FR-56 → tombstoned.** The room-picker requirement genuinely ceases to exist; same
  treatment as FR-12/41/43/46, keeping numbering stable.
- **FR-57 amended** — drop the room-assignment clause from the per-participant collection.
- **FR-99 (new)** — the admin shall assign lodging building(s) to a stay request, required
  before approval (D1), editable afterwards (D5), with a capacity warning (D2).
- **FR-93 amended** — sleeping-cleaning zones resolve from the **assigned buildings**, not
  from the assigned rooms' buildings, and are written at completion submission rather than
  refreshed on room assignment.
- **FR-38 / §2.5 amended** — approval precondition.
- **§8.5 `Participant`** loses two fields; **new §8.x** for the assignment entity; **§8.11 /
  §8.12** resolution notes updated; **`erd.md`** redrawn.

**FR-64 amended (confirmed 2026-08-06):** the two-week reminder email also names the
assigned buildings, alongside the participant/day-guest counts, meal summary and
check-in/check-out times it already restates — the group leader needs them in writing to
divide the group up.

### 2b. A second, restricted admin role — BLOCKS-LAUNCH, framed

**Client asked:** alongside the existing admin role, a second role with much less access,
whose sole purpose is access to the **food list** and the **participant list**.

**Current state:** there is exactly one role. All admin accounts have full access.
Role-based permissions are explicitly listed in `requirements.md` §10 as **deferred to a
future release** ("multiple admin accounts are supported in v1 — role differentiation and
per-admin access control are deferred"). This item un-defers that.

**Impact:** ASP.NET Core Identity already supports roles, so the mechanism is cheap. The
cost is in the sweep: every admin page, every nav entry, and every service entry point
needs an authorisation decision, and the admin-invite flow needs to carry a role choice.
The audit trail already records the acting admin, so attribution needs no change.

**Verified impact (code read 2026-08-06) — two useful findings.**

- **The mechanism already exists.** `Program.cs:40` registers
  `AddIdentity<ApplicationUser, IdentityRole>` (not `AddDefaultIdentity`), so the role
  infrastructure is live and `AspNetRoles` / `AspNetUserRoles` are already in the schema
  baseline. No migration is needed for the plumbing itself.
- **The role already exists in the SRS as a stakeholder.** §1's stakeholder table lists
  *"Coordinator (external) — receives participant name list from admin — access: **None,
  sent manually**"* and *"Kitchen manager (external) — receives food preference list from
  admin — **None, sent manually**"*. This item turns those two manual hand-offs into
  self-service accounts. That is the real framing — not "a lesser admin".
- **The design problem:** both lists are reached through `BookingDetail.razor`, a 37 KB page
  exposing guest contact details, admin notes, payment flags and every state-machine action.
  The downloads themselves are a route group at `/bokningar/{id}/deltagarlista.{pdf,xlsx}`
  and `/matlista.{pdf,xlsx}`. Every admin page today carries a blanket `[Authorize]` with no
  role checks anywhere.

### 2b — Framing (decided 2026-08-06)

**The change.** A second role with read-only access to the two per-booking lists and nothing
else, formalising the external coordinator / kitchen-manager hand-offs that §1 already
describes as manual.

| # | Decision | Outcome |
|---|---|---|
| D1 | Page-level separation or conditional rendering? | **Page-level.** The role gets its own bookings list and list-download view, and **no access to `BookingDetail` at all**. Conditionally hiding fragments inside a 37 KB page means every future addition needs a role check, and one miss leaks guest contact data. Authorise by page, never by fragment |
| D2 | Role names | **`Administratör`** (existing, full access) and **`Samordning`** (new, restricted) |
| D3 | Which bookings are visible? | **Only those where lists exist** — `DetailsComplete`, `Active`, `Completed` (already the gate in `ListShaper`, FR-44/45). No pending inquiries, no denied or cancelled bookings. Limits exposure for free |
| D4 | Guest contact details visible? | **No.** Booking reference, dates and headcount only — enough to pick the right booking. Participant names and allergies live *inside the lists*, which is the point of the role; the contact person's email and phone are not theirs to have |
| D5 | Who assigns the role? | **Any `Administratör`**, via the existing invite lifecycle (FR-74) — role chosen at invite time, changeable afterwards. No super-admin tier; too few accounts to justify one |
| D6 | Audit | Role assignment and change are audited. `AuditAction` already carries the `Admin*` verbs from Slice B, so this is one new verb |
| D7 | Privacy policy | **Must be updated before the jurist review.** The role reads participant names and special-category dietary data (GDPR art. 9), widening who processes guest data |

**Do not forget the landing page.** The admin host's `/` is the dashboard, which `Samordning`
must not see. That role's landing page is its own list view — small, and easy to miss until
someone logs in and hits an authorisation wall on the home page.

**Requirement changes:** `requirements.md` §10 currently lists role-based admin permissions
as **deferred to a future release** — that entry is removed, not merely contradicted. §1's
stakeholder table updated (the two external roles gain system access). FR-74 amended to
carry a role. New FR for the restricted role's scope. `Integritetspolicy.md` updated per D7.

---

## 3. Check-in / check-out times and arrival/departure day meals — BLOCKS-LAUNCH, framed

**Client asked:** check-in and check-out times are currently fixed. The client wants guests
to choose their **exact** check-in and check-out time, and to be able to choose **1, 2 or
all 3 meals** on the arrival day and the departure day.

**Current state:** §2.8 fixes check-in at **14:00** and check-out at **12:00**. Early/late
is arranged outside the system entirely — there is deliberately no field on either form and
no price rule. Those two times drive the meals-by-day rule in §4.1:
- Arrival day: **dinner only**
- Middle days: breakfast (buffet, no selection), lunch, dinner
- Departure day: **breakfast only**

FR-64 hardcodes "14:00" and "12:00" into the two-week reminder email, and both times appear
in the reminder email spec in §9.

**Impact:** §2.8, §4.1, FR-64, the §9 email templates, the inquiry form, the completion
form, `Booking.CheckIn`/`CheckOut` (currently `date`, would need time or a separate field),
and the kitchen prep list — the meal counts for the first and last day stop being derivable
and become explicit data.

### 3 — Framing (decided 2026-08-06)

**The change.** The fixed 14:00/12:00 schedule stops being a system constant. Guests state
their intended check-in and check-out times, and choose which of breakfast / lunch / dinner
they take on the arrival day and on the departure day. Middle days are unchanged and still
derived (always breakfast + lunch + dinner).

**Decisions:**

| # | Decision | Outcome |
|---|---|---|
| Q1 | Captured at inquiry or completion? | **Times at inquiry**, editable at completion and by the admin (FR-48). **Edge-day meals at completion**, with every other meal decision. The admin therefore knows the intended arrival time when deciding whether to approve |
| Q2 | Is the stated time binding? | **A request, not a commitment.** The guest states it, the admin may adjust it, and there is **no accept/decline workflow in the system** — negotiation (and any early/late surcharge) stays a phone conversation, exactly as §2.8 already prescribes. This preserves the standing decision that this is a booking system, not a ticketing/CRM tool |
| Q3 | Free text or a picker? | **A constrained time picker**, 30-minute steps, defaulting to 14:00 / 12:00 so the ordinary case stays one click. Free text produces "sometime in the afternoon" and is unusable downstream |
| Q4 | Are edge-day meals constrained by the stated time? | **No — free choice.** A group arriving at 11:00 may still want breakfast, and that is fine. The chosen time is displayed beside the meal checkboxes as context, never as a rule. Inventing a cutoff (11:59 qualifies for lunch, 12:01 does not) would be wrong often enough to be worse than trusting the group |
| Q5 | How are the times stored? | **Two new nullable `TimeOnly` fields**, *not* a conversion of `CheckIn`/`CheckOut` to `DateTime`. The `DateOnly` values are load-bearing for availability computation, retention arithmetic, blocked periods and the meals-by-day rule; converting them would ripple through all of it for no benefit |

> **Why Q1's split is safe.** The original worry was a stated time contradicting the chosen
> edge-day meals ("arriving 16:00" alongside "we want breakfast and lunch"). Q4 removes the
> contradiction by construction: meals are never constrained by the time, so the two cannot
> disagree. Q1 and Q4 must therefore be revisited together if either is ever reopened.

**The structural change to the meals-by-day rule.** Today `MealsByDayCalculator` *derives*
the edge-day meals from the fixed schedule — arrival day dinner only, departure day
breakfast only — and stores nothing. After this change the edge days become **explicit
stored data** (three flags each for the arrival and departure day) while middle days stay
derived. The calculator keeps its middle-day logic and takes the edge-day selections as
input.

**Combines with 1a.** Both items change how meal attendance is determined, and both touch
`MealsByDayCalculator`, the completion form's meal section and `ListShaper`. They should be
implemented as one piece of work rather than touching the same code twice.

**Admin warning on non-standard times — confirmed (user, 2026-08-06).** Because the time is
captured at inquiry and treated as a request, a guest whose booking is approved will
reasonably assume their 08:00 arrival was approved too. An inline admin-panel warning when a
stated time deviates from the standard ("Incheckning 08:00 avviker från standard 14:00")
makes the surcharge conversation hard to miss. Same pattern as FR-75, which already warns
about a short completion-link expiry without blocking the action.

**Picker bounds — confirmed (user, 2026-08-06): 07:00–22:00, 30-minute steps**, for both
check-in and check-out, defaulting to 14:00 / 12:00.

**Cross-item interaction with 4a.** A late check-out (say 20:00) consumes most of the
turnover day that item 4a reserves for cleaning. Whether the buffer must account for the
stated check-out time, or whether a late departure simply makes the turnover tighter, is a
question for 4a — flagged here so the two are not decided in isolation and left
contradicting each other.

**Requirement changes:** §2.8 rewritten (fixed times → stated times, surcharge handling
retained); §4.1 meals-by-day rule (edge days become data); FR-64 and the §9 reminder-email
spec stop hardcoding 14:00/12:00 in favour of the booking's real times; FR-57 (edge-day meal
selection); the inquiry form (FR-02/03) and completion form; `Booking` gains the two time
fields and the edge-day meal flags; `erd.md`.

---

## 4. Public site

### 4a. Turnover day between bookings in the availability calendar — BLOCKS-LAUNCH, framed

**Client asked:** a day is needed between bookings to clean and prepare for the next group.
That day must not show as available in the public availability calendar. This applies
regardless of whether the group paid for cleaning or cleaned themselves.

**Current state:** **no turnover buffer exists anywhere** — verified, there is no such
concept in `requirements.md` or in the availability/occupancy code. A booking occupies
exactly its check-in→check-out range and the next day is immediately bookable.

**Impact:** availability calculation (FR-10 and the occupancy provider), the public
calendar, and the inquiry form's date validation. Relatively contained, and clearly
correct — this is a real operational constraint that the system currently gets wrong.

**Verified impact (code read 2026-08-06) — two findings that shape the whole item.**

**1. The check-out date is currently bookable.** The model uses **nightly semantics**: a stay
occupies the nights from check-in up to but *not including* check-out — "guests leave at
12:00, so the check-out date's bed is free" (`OccupancyProvider`). A group checking out on
the 10th therefore leaves the 10th **already available** to a group arriving that same
afternoon. This is not strictly a defect — with check-out at 12:00 and check-in at 14:00 a
same-day turnover is technically possible — but it is not how the client wants the property
run. Both the check-out date and the following day now become unavailable (see the framing
below).

**2. Availability is advisory, not enforced.** `OccupancyProvider` has exactly one consumer —
the public `Calendar.razor`. It does not validate inquiry submission (`StayRequestInput`
only checks check-out > check-in), so a guest can already submit for a fully-booked date and
the admin decides. **This item therefore changes what the calendar *shows*, not what the
system *permits*** — and deliberately stays that way. No enforcement is added that the
calendar has never had.

### 4a — Framing (decided 2026-08-06)

**The change.** A booking's occupancy extends by **two days** — the check-out date **and the
day after it** — both shown as fully unavailable on the public calendar. Applies regardless
of the check-out time, and regardless of whether the group bought cleaning or cleans
themselves.

```text
 5th   6th   7th   8th   9th   10th          11th        12th
 |-----|-----|-----|-----|-----|              |           |
 sleep sleep sleep sleep sleep  leave 12:00   cleaning    next group
                                + cleaning                may arrive
                                └──── both non-bookable ──┘
```

**Why two and not one (user, 2026-08-06 — explicit client request).** A single day means the
cleaners share the departure day with the departing group, who may not be gone until late.
The client asked for a full clear day beyond the departure, **irrespective of check-out
time**, so the rule carries no time-of-day condition and no exceptions. Cost, stated plainly:
each booking removes two bookable days from the public calendar rather than one. Accepted
deliberately at this property's volume.

**Implementation — corrected 2026-08-06.** An earlier note in this section claimed the change
was simply extending occupancy to `CheckOut + 1`. **That is wrong and would not deliver what
was decided.** `AvailabilityCalculator` derives its state from *guest count vs. capacity*: a
ten-person group leaving a 79-bed property would leave the two turnover days reading
**`PartiallyBooked`** — still apparently bookable — not `FullyBooked`. Extending the date
range alone does not make a day unavailable.

The turnover days need their own **first-class "unavailable reason"**, in the same spirit as
the existing `WholePropertyBlock` signal, and must not be smuggled in as a synthetic booking
or a synthetic `BlockedPeriod`. See §7.2 — this is one of the two design corrections carried
alongside the feedback.

| # | Decision | Outcome |
|---|---|---|
| D1 | Whole property, or only the buildings used? | **Whole property.** Every group uses the shared spaces (dining hall, kitchen) whichever sleeping building they occupied, so those need turnover regardless. *(Note: 2a makes per-building turnover possible for the first time — assigned buildings' beds could be removed via the existing "beds removed by blocks" mechanism. Deliberately not taken: the accuracy is not worth the complexity for a property this size)* |
| D2 | Stored `BlockedPeriod` rows, or computed at query time? | **Computed.** A stored row would duplicate information the booking already holds, and would need lifecycle management on every booking mutation — dates changed, cancelled, denied, deleted — with orphan risk on each. A missed update shows a cleaning day for a group that is not coming and silently costs a bookable day. Computed cannot go stale, and costs nothing because the query already loads the occupying bookings |
| D3 | Admin override for a known back-to-back? | **No override.** Availability is advisory only — the admin can already approve back-to-back stays; the day merely looks taken. An override would be a mechanism with nothing to override |
| D4 | Late check-outs (item 3 allows up to 22:00) | **No special rule — and with the two-day buffer, none is needed.** An earlier draft protected only the check-out day, which made a 22:00 departure a problem worth special-casing; a conditional rule was rejected then for having an arbitrary cliff (16:59 gets one day, 17:01 gets two). The two-day buffer removes the question entirely: a late departure still leaves a full clear day for cleaning. The admin's non-standard-time warning (item 3) remains, and a genuinely exceptional case can still be blocked by hand |

**Still to settle during the SRS edits:** whether the turnover day is rendered distinctly from
an ordinary fully-booked date on the public calendar and in the admin calendar. It is
neither a booking nor a `BlockedPeriod`, and showing it as plain "fully booked" may be fine —
but it is a presentation decision, not a data one.

**Requirement changes:** FR-10 (availability computation) and §2.8 / §3.3 as applicable; the
nightly-semantics note in `OccupancyProvider`; `erd.md` unaffected (no new entity, by D2).

---

## 5. Prioritisation request

### 5a. The "review feature" — DEFERRED to a future release (2026-08-06)

> **Decided 2026-08-06: postponed as a future feature; out of scope for launch.** Initially
> parked pending client input, then resolved deliberately rather than left to drift — the same
> reasoning as 1b. Leaving it merely "parked" would have kept it nominally launch-blocking
> while unidentified, which is the one state that silently holds up the privacy policy, the
> jurist review and therefore the cutover. Deciding it out of scope keeps the policy clean and
> removes the only item in the whole set that could have required new Azure infrastructure.
>
> The analysis below is retained for whenever it is picked up — the questions to ask the
> client, and the fork between a public review surface and private feedback, do not expire.

**Client asked:** prioritise the future planned review feature.

**Blocker:** there is **no review feature documented anywhere** in the repository —
`requirements.md` §10 "Deferred to future releases" does not list one, and a search across
`docs/` for review / recension / omdöme / betyg / testimonial finds nothing relevant. This
needs to be identified before it can be scoped.

**Candidates to confirm with the client:** guest reviews/testimonials on the public site? A
post-stay feedback email? An internal review step in the booking flow? Something discussed
verbally at the demo and never written down?

### 5a — Pre-framing (2026-08-06): cannot be framed until identified

**The one question that decides everything: is it published on the public website, or is it
private feedback to the client?** Every other detail follows from that answer, and the two
readings differ by roughly an order of magnitude in cost.

#### Variant A — public guest reviews / testimonials

The most common reading for a retreat venue: past guests submit a review, the admin
moderates it, approved ones appear on the public site as social proof.

**If this is what the client means, it is the single largest item in the entire feedback
set — larger than 1a and 2a combined.** It introduces things this system has deliberately
never had:

- **A public, unauthenticated write surface.** Today the only public write paths are the
  inquiry form and the completion form; both are narrow and rate-limited. Free-text content
  submitted by strangers brings spam, abuse and moderation as permanent obligations. The
  Redis rate limiter (NFR-04) exists and would be reused, but the moderation workflow is new.
- **A new entity + moderation UI** in the admin panel (approve / reject / unpublish), plus
  the design-system work to present reviews on the public site (Phase 6 tokens).
- **New personal data**: reviewer name, possibly email, plus free text that guests may fill
  with anything about themselves. Requires privacy-policy coverage → **jurist critical path**.
- **A retention conflict worth deciding early.** Booking data is deleted on a schedule
  (12 months / 2 years, FR-67/68/89/90/91). A *published testimonial* that self-deletes after
  two years is odd, but a named review is personal data and cannot simply be exempted without
  a stated basis. This needs an explicit answer, not a default.
- **Solicitation**: reviews presumably invited by email after the stay — a natural extension
  of the existing thank-you email (FR-65).
- **Verified vs. open submission.** Tying a review to a real booking via a single-use token
  would reuse the completion-link infrastructure (§2.6) almost unchanged, and prevents
  fabricated reviews. Strongly preferable to an open form, and cheap *because* that machinery
  already exists.
- **Upside worth noting:** it dovetails with the Phase 11 technical-SEO item —
  `schema.org/Review` structured data can surface ratings in search results.

#### Variant B — private post-stay feedback

Guests receive a link after their stay and answer a few questions; results are visible only
to the admin. **Far smaller**: the same token mechanism, no public surface, no spam exposure,
no moderation UI (just a read view), no public-site design work, no SEO angle. Still adds
personal data and a retention rule, so it still touches the privacy policy.

#### Variant C — an internal review step in the booking workflow

E.g. a second admin reviews a booking before approval. Considered unlikely given the
wording, and it would interact with the new role model (2b). Cheap if it is this.

**Recommendation: ask the client three questions before any design work.**
1. Are these reviews shown publicly on the website, or only to you?
2. Should only real past guests be able to leave one, or anyone?
3. Do you want to approve each review before it appears?

**Schedule risk, stated plainly.** If it turns out to be Variant A it is large enough to
move the launch date on its own. It is also the second item (after 1b) that changes what
personal data the system holds, so it **must be settled before the privacy policy goes to
the jurist** — the launch gate.

#### Parked status and the one condition attached

Parked 2026-08-06 pending information from the client. **The condition: 5a cannot remain
parked past the point where the privacy policy is sent to the jurist.** Three states are
possible and only two of them are safe:

| State | Effect |
|---|---|
| **Blocking, with a deadline** | Stays in scope; "ask the client the three questions above" becomes a dated action that must complete **before** the policy goes to the jurist |
| **After-launch** | Same treatment as 1b — the policy is written without it and the jurist proceeds immediately |
| **Blocking and undecided** | ⚠️ What "parked" silently decays into. The policy cannot be finalised, so the jurist cannot start, so the launch slips — with nothing visibly blocking it |

The third row is the failure mode this note exists to prevent. It is the same trap that 1b
was pulled out of by being explicitly deferred; the difference is that 1b could be decided
unilaterally, while 5a needs the client. **Resolve to row 1 or row 2 before the jurist
hand-off.**

---

## 6. Items raised during triage (not from the client)

These surfaced while checking the system against the client's questions. They are not
client requests; they are decisions for the developer.

### 6a. Approved bookings get no reminder until T-14 — BLOCKS-LAUNCH, framed

Per FR-63, the only nudge to a guest who has not completed the form fires **14 days before
check-in**, exactly once. A booking approved six months out gets no contact for five and a
half months, then a single nudge. The completion link itself expires long before that (§2.6)
— which FR-63 handles by regenerating it, but only at T-14.

**Verified (code + FR-53 read 2026-08-06) — the gap is worse than "no reminder".** A
completion token expires at whichever comes first: **30 days after issue**, or 14 days before
check-in, with a 48-hour floor (FR-53). So a booking approved six months out plays out as:
approval email with a working link → **day 30, the link dies silently, with no warning to the
guest** → months of nothing, during which clicking the original link hits an expired page →
T-14, a fresh link and a nudge. The guest is left holding a link that stops working and is
never told.

### 6a — Framing (decided 2026-08-06)

**The change.** One nudge a few days **before the completion token expires**, sent only if
the booking is still `Approved`.

**Why that trigger rather than "X days after approval":** it fires exactly when something is
about to break, instead of at an arbitrary interval. It also mirrors an established pattern
in this system — FR-66 already warns 30 days before a record is auto-deleted.

**Deliberately not doing: auto-regeneration on expiry.** The time limit is a security
property (NFR-03: tokens are single-use and time-limited); silently renewing it indefinitely
would defeat it. The guest is warned; if they still do not act the link expires correctly and
the existing T-14 nudge (FR-63) catches them.

**Cost:** low. `DailyJobRunner` already runs daily and `ReminderEmailSentAt` is an
established fire-once pattern — this needs one additional timestamp field and one mail
template.

**Check while implementing:** what the expired-link page actually says. If it is a bare error
rather than "this link has expired — contact us and we will send a new one", that is a poor
moment for a guest to land on.

### 6b. Manually created bookings are silent — BLOCKS-LAUNCH, framed

`CreateStayRequestAsync(createdByAdmin: true)` creates the booking as `Pending` and sends
**no emails** (deliberate, FR-47). The guest hears nothing until the admin approves, which
issues the magic link. That is coherent — but it means a typo in the guest's email address
produces a booking that sits in `Approved` forever, with the only signal being the T-14
admin alert from FR-63.

Traced through: manual creation is silent → the admin approves → the completion link goes to
whatever address was typed. If it is wrong the guest receives nothing, the token expires after
30 days (FR-53), and the admin only finds out at the T-14 alert. They *do* find out — just
very late.

### 6b — Framing (decided 2026-08-06)

**The change: no new mechanism — one line of text.** Approving a booking already passes
through `ConfirmDialog` (built in Phase 11). Include the guest's email address in that
dialog's text — *"Skicka slutförandelänk till `anna@example.com`?"* — so the admin reads the
address at the exact moment it matters.

**Why this and nothing more:** no new background job, no new field, no delivery-tracking
infrastructure. It catches the typo at the only point where a human is already stopping to
confirm, and it costs a string interpolation. Undeliverable-bounce detection was considered
and rejected as disproportionate for the frequency of manual bookings.

### 6d. Payment tracking checkboxes removed — BLOCKS-LAUNCH, framed

**Origin:** the demo itself. The four payment checkboxes were presented to the client on
2026-08-05 and the client did not claim them — the client-developer's read is that they were
never asked for. Decision (user, 2026-08-06): **remove them completely.**

**Current state (verified 2026-08-06):** four booleans on `Booking` —
`DepositInvoiced`, `DepositPaid`, `FinalInvoiced`, `FinalPaid` — backed by **FR-42** and the
§2.5 flow-diagram node *"Admin tracks deposit — 20% invoiced · paid"*. They touch the entity,
one service method (`SetPaymentFlagsAsync`), one `AuditAction` verb, `DemoDataSeeder`, and
`BookingDetail.razor`. **Not** on the dashboard, **not** in any email, **not** in the
downloadable lists. A contained removal.

**The distinction that matters: the deposit *policy* stays.** It appears in the approval email
(20% deposit + cancellation terms), in the cancellation email (refunded vs. forfeited, driven
by `CancellationInitiator` — FR-77/79), in §5.6, and in the booking terms awaiting jurist
review. None of that changes. **Only the admin's in-system tracking of invoicing and receipt
is removed.**

**Why this makes the system more coherent, not less.** §5.4 already states that *invoicing is
performed manually outside the system*, and C8 forbids storing any monetary total. The money
domain was therefore already entirely external — except for four checkboxes that formed an
island of payment state connected to nothing: not derivable, not validatable, driving no
behaviour. Removing them puts the whole financial domain cleanly outside the system rather
than 95% outside.

**The real loss, stated plainly:** the admin will have no in-system view of who has not yet
paid. That must live wherever invoicing already lives. Given invoicing is manual, that is
where it belonged — but it is the kind of absence someone notices six months later, so it
should be confirmed with the client rather than assumed.

**Where it lands:** FR-42 **tombstoned** (numbering stays stable, per the FR-12/41/43/46
pattern); four field rows removed from §8.1; the general-inquiry null-list mention of "the
deposit/payment flags" removed; the §2.5 flow-diagram node removed; and **FR-95 amended** —
it explicitly lists *"deposit and final-payment status changes (FR-42)"* among the audited
actions, and that clause comes out.

**`docs/legal/` is unaffected** — the deposit policy is untouched, so this removal is legally
inert and does not disturb the jurist review. Worth noting given how tight that timing is.

### 6c. Stale SRS version footer — BLOCKS-LAUNCH, housekeeping

`requirements.md` still ends with *"End of document — Version 0.48 Working Draft"*, but SRS
versioning was retired on 2026-07-18 and the document is now explicitly unversioned. The
footer should be corrected while the document is being edited for this feedback.

---

## 7. Design corrections (ours, not the client's)

These did not come from the client. They surfaced while framing the feedback, in answer to
the question *"if we had known all of this from the start, would we have designed anything
differently?"* — asked deliberately (user, 2026-08-06) to avoid taking shortcuts merely
because the project is otherwise near complete. They are recorded separately from §1–6 so
that what the client asked for is never confused with what we chose to fix.

**Two of the four findings need no separate work** — the feedback already corrects them:

- **`DietaryRequirements` mixed medical constraints with preferences.** A modelling error
  present from day one, independent of the feedback: a nut allergy and vegetarianism are
  different kinds of fact, with different consequences (safety vs. preference) and different
  GDPR weight (art. 9 health data vs. an ordinary choice). **Corrected by 1a Q5.**
- **Role-based permissions were deferred although the requirement was already visible.** §1's
  stakeholder table named the external coordinator and kitchen manager with *"access: none —
  sent manually"* from the start. **Corrected by 2b.**

The remaining two require explicit design work beyond what the feedback strictly demands.

### 7.1 `MealsByDayCalculator` welds business policy to computation

**The defect.** The meals-by-day rule (§4.1) is implemented as a **pure derived function of
two dates**: check-in 14:00 / check-out 12:00 ⇒ arrival day is dinner only, departure day is
breakfast only. A *business policy* was encoded as a *computation*, so the policy cannot vary
per booking. This is precisely why item 3 is disruptive rather than additive.

**The shortcut to avoid.** Bolting three edge-day booleans onto `Booking` while
`MealsByDayCalculator` keeps deriving the middle days leaves **two sources of truth** for
"which meals occur on day X of this stay" — one derived, one stored, silently disagreeing the
first time someone edits a date.

**The correct shape.** One resolved concept: *the set of meal occasions for this booking*.
The calculator supplies **defaults** from the standard schedule; the booking may **override**
the edge days; every consumer (completion form, `ListShaper`, prep counts, reminder email)
reads the single resolved answer and never re-derives it. Policy becomes data with a
computed default, instead of a rule baked into a function.

### 7.2 `AvailabilityCalculator` has no first-class "unavailable reason"

**The defect.** `DateAvailabilityInput` flattens three unrelated concepts — booked guest
count, capacity, and a single `WholePropertyBlock` boolean — and derives the public state
mostly by comparing guests against beds. There is exactly **one** way to force a date
unavailable irrespective of headcount.

**Why it matters now.** 4a requires two turnover days to be **fully** unavailable. Extending
the date range does not achieve that: a small group leaving a large property yields
`PartiallyBooked`. (This corrected an error in the original 4a framing — see that item.)

**The shortcut to avoid.** Writing a synthetic `BlockedPeriod` row, or inflating the guest
count to fake a full property. Both put fictional data in the database to drive a display
rule.

**The correct shape.** Model *why* a date is unavailable as a first-class set of reasons —
whole-property block, turnover — evaluated alongside the capacity comparison rather than
smuggled through it. Turnover then becomes one more reason, and any future reason is additive
rather than another special case.

### 7.3 What is deliberately **not** being changed

Recorded so a future reader knows these were considered and upheld: the tech stack; the
booking state machine (§2.5); the snapshot pattern (NFR-15/16/33/34); the audit trail; the
public/admin host separation (FR-70); and the no-stored-monetary-totals rule (C8). Nothing in
the feedback strains any of them.

> **Why this section exists.** The database may be freely rebuilt until the 10d cutover, and
> after it never again. That makes this the cheapest moment in the system's life to correct
> modelling debt — and the last one. The freedom to drop the database is not a licence to
> think less carefully about the schema; it is the reason to think harder now.

---

## 8. Conflict audit (2026-08-06)

A pass tracing every locked decision through the implementation, looking for contradictions
with what is already built — run **before** any document was edited, at the user's request.
Four findings would have shipped as silent defects.

### 8.1 Blocking conflicts and their resolutions

**C1. Removing `Participant.RoomId` silently voids the room hard-delete guard (FR-20).**
`RoomService.CountReferencesAsync` is one line — `db.Participants.CountAsync(p => p.RoomId ==
roomId)` — and is the *only* reference check on a room. After 2a it returns 0 forever, so
`CanHardDelete(0)` is always true and **every room becomes hard-deletable**. The guard does
not fail loudly; it silently always passes. FR-20 (*"shall not hard-delete a room referenced
in an existing booking"*) also becomes unenforceable by construction, since after 2a no
booking references a room. *(`BuildingService` has the same shape but survives by accident —
it also counts rooms in the building, and a lodging building always has rooms. Protection by
coincidence, not design; FR-16 must still be repointed at the new booking↔building
assignment.)*

> **Resolution (user, 2026-08-06): option B — repoint FR-20 at blocked-period integrity.**
> `BlockedPeriod.BuildingId` / `RoomId` are **logical references, not foreign keys**
> (`ApplicationDbContext:194`), so deleting a room today already orphans any room-scoped
> block pointing at it — a real hole that exists right now and is guarded by nothing. FR-20
> is rewritten to block hard-deleting a room (or building) that a `BlockedPeriod` still
> references. Chosen over simply tombstoning FR-20 because it costs the same, keeps the
> requirement doing useful work, and closes a genuine pre-existing defect.

**C2. NFR-16 would be orphaned.** *"Room assignment records shall store a snapshot of the room
name and building at submission time."* After 2a there are no room-assignment records.
**Resolution:** NFR-16 is **rewritten** (not deleted) to cover the building-assignment
snapshot decided in 2a/D3 — otherwise the codebase carries an NFR nothing implements.

**C3. A requirement nobody stated: blocked buildings must not be assignable.** §3.3 and FR-21
both tie block semantics to room selection on the completion form (FR-56) — *"excluded from
room selection and the admin assignment view"*. Both clauses die with FR-56. Blocks still
reduce calendar capacity (FR-10), which survives, **but nothing then prevents an admin
assigning a building that is blocked for those dates.** Today that protection exists only as
a side effect of room selection; after 2a it evaporates. A functional regression covered by
no feedback item.

> **Resolution (user, 2026-08-06): confirmed — blocked buildings must not be assignable.** A
> **new FR** is added for this, and §3.3 / FR-21 are rewritten from room-selection terms into
> building-assignment terms.

**C4. Turnover days would not render as unavailable.** See §7.2 — extending the date range
alone yields `PartiallyBooked`, not `FullyBooked`.

### 8.2 Implementation notes (non-blocking, but they change the effort)

- **`MealPlan.Mixed` has 13 call sites**, including two user-facing dropdowns
  (`BookInquiry.razor:126`, `ManualBooking.razor:55`). The day-guest applicability rule
  `MealPlan is FullBoardAll or Mixed` is **triplicated** — `BookingService:240`,
  `InquiryNormalizer:14`, `CompleteBooking.razor:531`. Consolidate to one rule while editing,
  rather than editing three copies.
- **Role infrastructure exists but nothing is seeded.** Correcting an earlier claim in this
  document that "the mechanism is already there": `AddIdentity` registers `RoleManager` and
  `AspNetRoles` exists, but **no role is ever created and no user assigned one** —
  `AdminSeeder` creates the first admin with no role. 2b additionally needs role seeding at
  startup, `Administratör` backfilled onto seeded admins, and `AdminInviteService` extended
  to carry a role.
- **`MealsByDayCalculator` has 11 call sites across 6 files**, including `ListShaper`,
  `DemoDataSeeder` and three separate places in `CompleteBooking.razor`. §7.1 is a genuine
  refactor, not a tidy-up.
- **Two calendars, two code paths.** `AdminCalendar.razor` does **not** use
  `OccupancyProvider` — it queries `BookingService.GetBookingsAsync` /
  `GetBlocksInRangeAsync` directly. The 4a turnover rule must be applied to both, or the
  admin will see availability the public cannot book.
- **`CheckOut` feeds GDPR retention arithmetic** (`BookingService:71`, `:525` →
  `RetentionSchedule`). This confirms item 3's decision to add separate `TimeOnly` fields
  rather than converting the dates to `DateTime` — conversion would have rippled into
  retention computation.
- **`DietaryRequirement` touches 12 files**, including `BookingDisplay`, `ListShaper`,
  `ParticipantEdit` and both UIs. Wider than "split an enum".

### 8.3 Azure infrastructure impact — none

**No Bicep edits, no new resources, no cost change.** `main.bicep`, `alerts.bicep`,
`ssl.bicep` and all parameter files are untouched. Blob Storage is unaffected because 1b (the
only upload surface) is deferred — so no CORS or malware-scanning question arises. Redis is
unaffected: no new public write endpoints, so no new rate-limit surface. SMTP gains one mail
type (6a), negligible. App Service and PostgreSQL tiers are unchanged, and data volume
actually **decreases** as per-participant meal rows collapse to per-booking rows. Monthly cost
stays ~$45.

**The only Azure-adjacent work is operational:** the baseline regeneration requires dropping
and recreating **both** staging and production databases per `infra/ops/db-access-runbook.md`
— never migrating in place (see the 2026-07-14 boot-crash landmine noted at the top).

> **5a was the one risk here, and it is now closed.** Had the review feature resolved to
> *public* reviews it would have carried real infrastructure implications — a new public write
> surface (Redis rate-limit keys), possibly Blob storage for images, and a spam/moderation
> story. Postponing it as a future feature (2026-08-06) removes the only item in this set that
> could have changed the infrastructure, and the "no Bicep change" conclusion above is
> therefore firm rather than provisional.

### 8.4 Documentation surface map

| Document | Touched by | Scale |
|---|---|---|
| `requirements.md` | §1 stakeholders (2b) · §2.5 flow (6d) · §2.6 (6a) · §2.8 (3) · §3.3 (C3) · §4.1/4.2/4.6 (1a) · §5.5 (2a) · §8.1/8.5/8.6/8.10/8.11/8.12 (1a, 2a, 6d) · §9 emails (3, 6a) · §10 deferred list (2b) · FR-16/20/21/42/56/57/64/93/95 · NFR-16 | Largest |
| `erd.md` | New booking↔building entity; `MealPreference` → `BookingMealSelection` re-parented; `Participant` shrinks | Redraw |
| `architecture.md` | Two admin roles; no new external services | Light |
| `implementation-plan.md` | New work section; **migration-exception expiry moved 10c → 10d**; phase → requirements map | Moderate |
| `docs/ux/design.md` | Completion-form restructure; admin building assignment; `Samordning` pages | Moderate |
| `docs/legal/Integritetspolicy.md` | **2b only** (art. 9 data, new processor role). 6d is legally inert | ⚠️ Jurist gate |
| `docs/legal/Boknings-och-ordningsvillkor.md` | Possibly item 3 — check-in/check-out times are contractual | ⚠️ Verify |
| `.claude/CLAUDE.md` | Migration-exception expiry; entity list | Small |
| `requirements-changelog.md` | **Frozen — never touch** | None |

---

## Working order

1. **Capture** — this document. ✅
2. **Triage** — all items launch-blocking except 1b; 5a later postponed as a future feature. ✅
3. **Frame** every item into precise language, recording the reasoning, not just the decision. ✅
4. **Conflict audit** against the implementation before touching any document (§8). ✅
5. **Update the documents** — `requirements.md` (new FR numbers only; never reuse the
   tombstoned FR-12/41/42/43/46/56), `erd.md`, `architecture.md`, `implementation-plan.md`
   (including the phase → requirements map), `.claude/CLAUDE.md`, `docs/ux/design.md`, and
   `docs/legal/` where the data collected changes. ✅ — verified by two passes: reference
   integrity (FR/NFR/§ cross-references, entity parity between the SRS and the ERD, stale
   terminology, table column counts) and a **render pass** on all five Mermaid diagrams.
6. **Then** write code. ← next

> **The critical-path note, updated.** The original concern was that 1b (guest-name uploads)
> and 2b (a second role reading art. 9 dietary data) both change what personal data the system
> holds, and that a change landing *after* the jurist review starts would buy a second round
> and a later launch. **1b is deferred and 5a is postponed, so only 2b remains** — and it is
> decided, documented, and already reflected in `Integritetspolicy.md`. The privacy policy can
> now go to the jurist without waiting on anything in this document.
>
> Worth telling the client and the jurist: the system after this work holds **less** personal
> data than the version demonstrated — no per-person meal rows, no room assignments, dietary
> preferences as counts rather than named flags, and no payment tracking at all.
