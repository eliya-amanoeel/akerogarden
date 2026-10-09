# Deposit before confirmation — decision record, 2026-10-07

The client asked, on 2026-10-07, that a booking be confirmed only once the 20 % deposit has arrived.
This document records the design agreed for that, every decision taken and whose it was, what still
waits on the client, the gaps found while checking it, and everything the change touches.

> **Status (2026-10-09): built on `feat/deposit-before-confirmation`, not yet deployed.** The SRS
> (FR-115/116), ERD, lifecycle diagram and admin manual now carry it and are the authority; this file
> is the record of *why*. Two questions about the client's terms remain (§5) and block nothing. Same role as `demo-feedback-2026-08-05.md` had for that work.

---

## 1. The requirement, and what it is not

**A stay request is confirmed only when its deposit has been received** — at the admin's discretion,
per booking. The money moves entirely outside the system: the guest pays Åkerögården by bank transfer
or Swish, and an admin marks it as received.

**The system does not take payments, and does not learn what anything costs.** No amount is stored or
computed (C8 stands). What changes is that the booking lifecycle gains a step that *waits* for money.

**The documents already said this; the system did not.** SRS §5.6 lists the deposit as *"due to
confirm booking"*, and the client's own terms (§6) say Åkerögården *"kan begära förskottsbetalning
… för att bekräfta en bokning"*. Until now `Approved` was treated as confirmed — dates held, links
issued — whether or not anything had been paid. This change brings the system in line with its own
documents rather than adding a new rule.

**It is not FR-42 coming back.** The four payment checkboxes removed on 2026-08-06 were dropped
because they *drove nothing* (demo-feedback §6d). That record also predicted this request: *"the
admin will have no in-system view of who has not yet paid … the kind of absence someone notices six
months later"*. The new status drives behaviour — it holds dates, withholds the completion link, and
gates confirmation — which is exactly the distinction FR-42's removal drew.

---

## 2. The design

### Lifecycle

```
Pending ──► AwaitingDeposit ──► Approved ──► DetailsComplete ──► Active ──► Completed
   │             │  holds the dates
   │             └──► Cancelled   (unpaid and released, or withdrawn)
   └──────────────────► Approved   (direct confirmation — unchanged, still allowed)
```

- **New status `AwaitingDeposit`**, Swedish label **"Väntar på handpenning"**. Persisted as a string
  (`HasConversion<string>()`, max 30, no check constraint), so the value itself needs no schema change.
- **New transitions:** `Pending → AwaitingDeposit` (admin, stay requests only),
  `AwaitingDeposit → Approved` (admin), `AwaitingDeposit → Cancelled` (admin).
  **`Pending → Approved` stays.** The terms say *kan*, so the admin chooses per booking.
- **`Approved` keeps its meaning — confirmed.** Everything after it is untouched: completion link
  issuance (FR-51/108), the nudge and pre-arrival reminder (FR-63/64), lists, the coordinator role.

### What the waiting step does

- **Holds the dates.** `AwaitingDeposit` joins `OccupancyProvider.OccupyingStatuses`. That single
  array feeds the public calendar, the public form's refusal (FR-113), the admin calendar's turnover
  days (FR-37) and — through `BlockedPeriodService.LiveStatuses` — the block refusal (FR-102).
- **Takes over today's approval preconditions**, because it is now the moment dates are held: the
  availability check (FR-113), building assignment (FR-103), the advisory warnings (FR-75/101,
  capacity), and the dialog stating the address the email goes to (FR-38) — for a manually created
  booking this is now the first email the guest receives.
- **Confirming re-runs the availability check** (FR-113 says "and again at approval"). Cheap, and it
  covers a whole-property block that slipped in between.
- **A deadline.** On entering the status the booking gets a due date: today + N days (N from the
  client, §5), **capped so it never falls after arrival**. Stored, because nothing else can supply it:
  `UpdatedAt` moves on every edit, and business logic must not read the audit log.
- **One automatic reminder** when the deadline passes — the client's terms §6 require a reminder
  before an unpaid booking may be treated as cancelled. Marked sent only on a confirmed send, retried
  nightly otherwise (the PR 95 rule).
- **After that, the admin decides.** The booking shows as overdue, in red, at the top of the dashboard
  card. No automatic release, no admin email.

### Emails

| Email | When | Content |
|---|---|---|
| **Deposit request** (new) | `Pending → AwaitingDeposit` | Reference; the dates are reserved until the due date; the deposit is 20 %, the amount comes in a separate email from Åkerögården; where to pay (payee, bankgiro, Swish, clearing + account — D17), with the reference as payment message; the due date; **the deposit is not refunded on cancellation once paid** (D14 — must be shown before the guest pays); **link to the terms** (§2 — paying is the guest's acceptance) |
| **Deposit reminder** (new) | Deadline passed, once | Not yet received; pay as soon as possible or the booking may be cancelled |
| **Approval — deposit path** | `AwaitingDeposit → Approved` | As today, but worded as a confirmation. *(Planned 2026-10-07 to say the deposit had arrived; changed while building on 2026-10-09 — the admin may confirm without payment, so that sentence could be false.)* |
| **Approval — direct path** | `Pending → Approved` | As today, but **no sentence saying a deposit confirms the booking** |
| **Cancellation — guest** | Admin cancels, guest-initiated | Where a deposit was paid: *"Handpenningen återbetalas inte."* Then: *"Eventuell ytterligare avbokningsavgift följer de villkor som gällde när er bokning bekräftades. Vi återkommer med besked."* (client's §32) |
| **Cancellation — property** | Admin cancels, property-initiated | Full refund of **anything paid** — mentions a deposit only if one was requested |
| **Cancellation — unpaid release** (new variant) | `AwaitingDeposit → Cancelled`, deposit **not** received | The booking was never confirmed, nothing is owed (terms §2) |

**Cancelling a booking still in `AwaitingDeposit` asks the admin "Har handpenningen kommit in?"**
Paying is the binding moment (D15), so money that has arrived but not yet been marked makes the deposit
non-refundable — the system cannot know that, so the answer picks the email: *yes* → the guest
cancellation wording, *no* → the unpaid release.

Across all approval variants: **no cancellation rule restated** (point to the terms instead of the old
two-month rule at `BookingEmailTemplates.cs:287`), and **no balance due date** — *"resterande belopp
faktureras"*, or *"beloppet faktureras"* on the direct path. Both approval variants keep the existing
link / no-link split (FR-52), so the approval template has four variants.

**The due-date column doubles as the "a deposit was requested" marker** that the property-initiated
cancellation needs; it stays set after the booking leaves `AwaitingDeposit`.

### Admin panel

- **Booking detail, `Pending`:** two actions — *Begär handpenning* and *Godkänn direkt* — under the
  existing refusal/warning boxes.
- **Booking detail, `AwaitingDeposit`:** the due date, overdue state, **"Bekräfta bokningen"** (neutral
  wording — the admin may also confirm without payment, e.g. after deciding to invoice instead), and
  cancel with the deposit question above.
- **Dashboard:** new card *Väntar på handpenning*, sorted by due date, overdue in red. This is the
  only safety net for an unpaid booking nobody acts on.
- **Status badge:** every semantic hue is already allocated (`design.md`). A new state needs its own
  token, and contrast measured against the **hovered** admin row, not white.
- Booking list filter and admin calendar pick the status up from the enum by themselves.

---

## 3. Rejected alternatives

| Alternative | Why not |
|---|---|
| Take payments in the app | Needs a computed total (C8), a payment provider as a new data processor, refunds in-app, a privacy-policy change — and a payment integration for a solo maintainer, on a low-traffic site |
| Bank details in the inquiry acknowledgement, guest pays straight away | The amount is unknown before review; guests pay for requests nobody has read (denials become refunds, two guests can pay for one week); `Pending` holds no dates; admin-created bookings (FR-47) get no acknowledgement at all; on-behalf bookings (FR-112) go to the admin's address |
| A deadline on `Approved` instead of a new status | Revives FR-42 as a payment flag, and unpaid guests would still be told *"godkänd"*, get completion links and nudges |
| Don't hold the dates while waiting | Another group can be approved over a week whose group is already paying |
| Release unpaid bookings automatically | More to build, and a transfer takes 1–2 days — the system would release dates while the money is in flight |
| The system calculates the amount, or the admin types it in | Calculating is a pricing engine; typing it in means storing an amount. Both amend C8 for no gain: organisations need an invoice to pay anyway |
| Bank details editable in the admin panel | Anyone with an admin login could change where guests send money |

---

## 4. Decisions

All taken 2026-10-07 by the maintainer unless marked.

| # | Decision |
|---|---|
| D1 | Deposit optional **per booking** — the admin chooses. Covers phone and short-notice bookings |
| D2 | **20 %**, as the client stated verbally. Not raised again in the form |
| D3 | The amount stays outside the system |
| D4 | The waiting status **holds the dates** |
| D5 | After the reminder **the admin decides**; safety net is **red on the dashboard only** |
| D6 | Deadline **capped** so it never falls after arrival |
| D7 | Emails name **no balance due date** |
| D8 | Cancellation emails point to the terms in force at confirmation (**§32 wording**). Reverses the 2026-09-07 decision to keep the emails unchanged — that was a holding position until question 6 was answered. Resolves known-conflicts §3 |
| D9 | "rekommenderas" → **"gäller"** in the client's §7. Decided 2026-10-07 to change it right away; **changed 2026-10-08: it waits with everything else** (D19) |
| D10 | Prices on `/mat-och-priser` shown **including VAT** — fixed quietly, not raised with the client (known-conflicts §5). *Built (commit `feb5c30`, recoverable from it), then held back from the 2026-10-09 deploy: its inputs are unconfirmed (see the smoke-test note in §5); the maintainer takes it to the client* |
| D11 | Check-in/check-out 14:00/12:00 **closed**: the approval email already states the booking's own times, which is what the client's §10 asks for |
| D12 | Allergy data stays on **consent**; retention periods **stand** (the system holds no accounting records). The maintainer acts as part of the client's organisation, so **no processing agreement** is needed |

Taken 2026-10-08, after the client's answers (§5):

| # | Decision |
|---|---|
| D13 | Amount told to the group **by a separate email** from Åkerögården (client) |
| D14 | **The deposit is never refunded**, however early the group cancels (client). This overturns our 2026-10-07 reading that §7's *"ingen avbokningsavgift"* more than 8 weeks out implied a full refund; §7 must be rewritten to say so |
| D15 | **Paying the deposit is the binding moment** — non-refundable once paid, not once the admin confirms. Hence the cancel question in §2 |
| D16 | **7 days** to pay (client), capped per D6 |
| D17 | Payment details: payee **"Kaldeiska katolska Mar Narsay Missionen i Södertälje"** (the privacy policy's name, not the variant in the client's answer); bankgiro **5713-3399**; Swish **123 583 24 23**; clearing **5364**, account **108 72 28** (SEB — the maintainer confirmed the split of the client's `53641087228`). Kept as a **code default** in `BookingEmailOptions`, like `PropertyAddress`: changed only through a reviewed PR, never from the admin panel |
| D18 | Retention while waiting: **as `Pending`** — 12 months from submission |
| D19 | The terms-page changes — §7 rewrite, "gäller", kitchen and common-room rules (client), and **damage claimed within one week of departure** (set by the maintainer; the client left that question unanswered) — are published **together with everything else**, not before. The client is told afterwards, naming the one-week deadline explicitly |
| D20 | A failed deposit-request email is accepted: admin-triggered emails ignore failed sends (out of scope since 2026-10-05) and the nightly reminder at the deadline is the safety net |
| D21 | Bookings already confirmed before this ships get **no special handling** — they are few and will be treated fairly by hand |

---

## 5. Waiting on the client

Form: `docs/legal/klientfragor-2026-10-07.html` (replaces the 2026-08-23 form). **Answered
2026-10-08** (`Akerogarden_Svar_Betalning_och_Villkor.pdf`, kept outside the repo):

| Form question | Answer |
|---|---|
| 1 — how the group learns the amount | A separate email (D13) |
| 2 — where to pay | Bankgiro, Swish and account; reference number as payment message, OCR-only not ticked (D17) |
| 3 — days to pay | 7 (D16) |
| 4 — cancellation 8 to 4 weeks out | **Not answered.** Instead: the deposit is never refunded (D14) |
| 5 — kitchen and common rooms | No kitchen at night; cleaning is a paid add-on, otherwise the group cleans the kitchen; common rooms close 23:00 |
| 6 — damage claim deadline | **Not answered** (restated §19). Set to one week by the maintainer (D19) |

**Still with the client** — recorded in SRS §12, neither blocks the build:

1. **Cancellation 8 to 4 weeks before arrival.** The deposit is now the minimum anyone pays; the
   question is only whether more is owed.
2. **Bed linen and towels** — terms §12 says what is included *"framgår av bokningen"*, and nothing in
   the system or on the site says either way.

### Raised in the local smoke test, 2026-10-09 — open, to discuss

1. **The amount in the deposit email (maintainer's proposal).** The deposit request carries every
   payment detail except the amount, so the client must send a separate email just for the number —
   hard to justify for the guest or the client. Putting it in means the admin types the amount when
   asking for the deposit, which **stores an amount and so reopens C8** (§5.6, CLAUDE.md). Not
   decided; the design today keeps it out (D3, D13).
2. **"Pris och betalningsvillkor får ni från oss separat"** in the direct-path approval email. Same
   root as 1: the system knows no prices, so the confirmation cannot state them as the client's §2
   says it should. Whatever is decided for 1 decides this sentence.
3. **VAT-inclusive prices (D10).** The arithmetic is right (550 × 1.12 = 616), but its inputs are
   unconfirmed: the 12 %/25 % rates were seeded as placeholders "to be confirmed with the accountant",
   and production's prices are identical to the seed; that the stored amounts *exclude* VAT is an SRS
   assumption with no source; and a church mission may not charge VAT at all, or not on room hire.
   Showing computed consumer prices on unconfirmed rates is worse than today's "exklusive moms". A
   question for the client.

---

## 6. Everything it touches

**Domain** — `BookingStatus`, `BookingStateMachine`, `OccupancyProvider.OccupyingStatuses`,
`RetentionCalculator` (FR-91), `CancellationPolicy` (the binary refund/forfeit outcome goes; initiator
plus "was a deposit requested" decide the wording), `BookingService` (new request-deposit step carrying
today's approval preconditions; `ApproveAsync` accepts `AwaitingDeposit`; `GetApprovalWarningsAsync`),
`Booking` (due date + reminder-sent columns), `AuditAction` (new verbs, stored as strings),
`BookingDisplay`, the nightly reminder (new `IScheduledJob`, so job history picks it up).

**Migration** — two nullable columns. Not the first since launch — `AddOnBehalfOfBooking` (2026-09-09) and `AddJobRuns` (2026-10-02) already ran against production — and as additive as they were; the
pipeline already deploys the migrating admin site first, and the public role is covered by
`ALTER DEFAULT PRIVILEGES`.

**Emails** — `BookingEmailTemplates`, `BookingEmailService`, `BookingEmailOptions` (payment details).

**Admin** — `BookingDetail.razor`, `Dashboard.razor`, `site.css` (badge token).

**Public** — `Home.razor` ("Så går det till"), `PrivacyPolicy.razor`: the §5 sentence naming the live
states, **plus a payment-data category and a seven-year accounting retention row** (deposits paid by
Swish or transfer give the client the payer's name and number; the policy names bookkeeping as a
purpose but has neither). `MatOchPriser.razor` (D10 — its code comment cites C8 for not showing an
incl-VAT price; C8 forbids stored booking totals, not a per-item display price).

**Legal text** — `BookingTerms.razor` and `docs/legal/klientsvar.md`: §7 rewritten for D14 and D9,
the kitchen and common-room rules in §12/§15, the one-week damage deadline in §19, and the *"Senast
uppdaterad"* date (D19). `docs/legal/Integritetspolicy.md` in step with the policy page,
`known-conflicts-at-launch.md` (mark §3–§6 done once built).

**SRS** — §2.1, §2.3, §2.4, §2.5, §2.7, §5.6 note, §8.1, §9; FR-38, 63, 75, 76, 77, 79, 91, 95, 102,
103, 113; a new FR for the waiting step and one for the deposit emails.

**Rules** — the CLAUDE.md critical rule *"no payment state at all"* and the matching `erd.md` note must
be amended openly: a status named after a payment is payment state. Amounts stay out.

**Docs** — `erd.md`, `booking-lifecycle-timeline.md` (§1, §2, §4), `design.md` (badge list and
contrast figures), `docs/handover/admin-manual.html` (approving, statuses, emails, cancelling — and the
dashboard as the only safety net), `infra/ops/seeding.md`, the `Booking.cs` doc comment.

**Tests** — status lists are written out by hand (`InlineData`), so **nothing covers the new status
automatically**: state machine, occupancy (unit and integration), retention calculator, reminder
schedule, FR-102 block integration, approval warnings for the new step, every email variant,
cancellation policy, audit trail, demo seeder, the nightly reminder (isolated database, per the PR 95
lesson), and E2E for both paths with axe at 360/768/1280; the Swedish-only rendered-text test gains
the dashboard card. The roughly 55 existing approval call sites keep their meaning, since direct
approval remains.

**Demo data** — `DemoDataSeeder` gets a waiting booking and an overdue one.

---

## 7. Order of work

The maintainer gave the go on 2026-10-08; the work is on `feat/deposit-before-confirmation`.

1. **Before the production deploy (not before the work starts):** a passing restore drill (NFR-29).
   It has never been run against a database holding real guests, and it is the only proof a bad
   migration can be undone. **The 2026-10-08 attempt failed on Azure capacity and the drill is
   postponed** — attempts and the risk they exposed are in `infra/ops/restore-drill-runbook.md`. If it
   still cannot pass when the work is ready, deploying without it is a decision to take explicitly,
   not by default. **Taken 2026-10-09 by the maintainer: deploy to production without a passing
   drill; the drill is postponed with no date set.** The migration is additive (two nullable
   columns), as the two post-launch migrations before it were; the drill remains owed under NFR-29.
2. **SRS and docs first, then code, on one branch** — the terms-page changes (D19) and the VAT fix
   (D10) ride along. The client's answers to the build parameters are all in.
3. **Tell the client what changed** once it is live, with the two open questions (§5) in the same
   message if still unanswered.
4. **Full completion gate** before merge — this touches the booking state machine.
5. **Staging, then production** — never deployed across 03:00. On the day it goes live, bump *"Senast
   uppdaterad"* on `/bokningsvillkor` **and** `/integritetspolicy` (both texts changed with it — page and source
   `.md` alike), then tell the client what changed —
   naming the one-week damage deadline explicitly, since it is ours (D19). **After** the production
   deploy, apply `infra/azure/alerts.bicep`: its nightly-jobs threshold rose from 3 to 4, and applied
   before the fourth job exists it fires every night.
