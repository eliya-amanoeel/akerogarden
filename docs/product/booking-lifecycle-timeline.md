# Booking lifecycle and timeline

What happens to a booking from inquiry to deletion, and — the part that is hard to hold in your
head — **when** each automated email fires and **how long each completion link lives**.

The rules themselves live in `requirements.md` (§2.5 transitions, §2.6 magic link, §2.7 retention,
§9 email templates). This document only draws them. If the two disagree, the SRS wins.

---

## 1. Status transitions and the emails they trigger

```mermaid
stateDiagram-v2
    direction TB

    [*] --> Pending: guest submits an inquiry
    [*] --> Pending: admin records it (FR-47/112)

    Pending --> Approved: admin approves directly (stay request)
    Pending --> AwaitingDeposit: admin asks for the deposit (FR-115)
    Pending --> Denied: admin denies (stay request)
    Pending --> Closed: admin answers (general inquiry)

    AwaitingDeposit --> Approved: admin confirms — deposit received
    AwaitingDeposit --> Cancelled: admin cancels (withdrawn, or never paid)

    Approved --> DetailsComplete: guest submits the completion form
    Approved --> Cancelled: admin cancels

    DetailsComplete --> Active: admin marks arrived
    DetailsComplete --> Cancelled: admin cancels

    Active --> Completed: admin marks the stay finished
    Active --> Cancelled: admin cancels

    Denied --> [*]
    Closed --> [*]
    Cancelled --> [*]
    Completed --> [*]

    note right of Pending
        Guest: acknowledgement
        Admin: new inquiry
        (neither is sent when an admin
        recorded the booking — FR-47)
    end note

    note right of AwaitingDeposit
        Guest: deposit request — where to pay,
        due in 7 days, not refundable once paid
        Dates held while waiting
        Due date passed: one reminder (FR-116),
        then overdue on the dashboard
    end note

    note right of Approved
        Guest: approval — with the link if
        check-in is 40 days away or sooner,
        otherwise "the link follows later"
        Then, while still Approved:
        the daily jobs below take over
    end note

    note right of DetailsComplete
        Guest: confirmation of what they entered
        Admin: details are in
    end note

    note right of Completed
        Guest: thank-you
        Retention clock starts
    end note
```

`Denied`, `Closed`, `Cancelled` and `Completed` are terminal — nothing transitions out of them.
`DetailsComplete` never returns to the completion form; corrections after submission are made by the
admin in the panel and change no status (C10).

### An admin-recorded booking enters at the same place, quietly

A booking an admin takes by telephone starts in `Pending` like any other and follows the identical
path — **it still waits for approval**, and approval still issues the completion link. What differs is
only what is sent at creation: **no guest acknowledgement and no admin notification** (FR-47). The
acknowledgement says *"we have received your booking request"*, which is untrue for a booking already
agreed on the phone, and undeliverable to a guest with no email address at all. The approval mail —
or, if the admin asks for a deposit, the deposit request (FR-115) — is then the single, accurate first
message the guest receives, which is also why the terms reach them there rather than earlier. That gap was closed by decision on 2026-09-09 with a process control:
`/bokningar/ny` tells the admin to send a guest who has an email to the web form instead (FR-111).

Where the guest has no email, the admin's own address stands in as the booking's contact email so the
approval mail and completion link reach whoever will fill the form in, and the booking records which
admin is acting and that the terms were read out verbally (FR-112, `OnBehalfOfAdminId` /
`TermsAttestedAt`). Everything below applies unchanged to those bookings — the dates, the link
lifetimes and the nightly decisions do not know or care how the booking was created.

---

## 2. What the nightly run decides

One run, 03:00 Europe/Stockholm, four jobs in the order they are registered
(`AkeroServiceCollectionExtensions.AddAkeroScheduledJobs`). The order matters only in the rare case
noted below. *(Until 2026-10 this section drew retention last; the code has always run it first.)*

```mermaid
flowchart TD
    N(["Nightly run — 03:00 Europe/Stockholm"]) --> J0

    J0["**1. GDPR retention** (FR-66/69)"] --> Q7{"Past DeleteAfter?"}
    Q7 -->|"yes"| A4["Hard-delete the booking<br/>and everything hanging off it"] --> J1
    Q7 -->|"no"| Q8{"Within 30 days of it,<br/>not yet warned?"}
    Q8 -->|"yes"| A5["Admin: record due for deletion"] --> J1
    Q8 -->|"no"| J1

    J1["**2. Completion-link issuance** (FR-108)"] --> Q1{"Still Approved,<br/>check-in within 40 days,<br/>and no usable link?"}
    Q1 -->|"no"| J2
    Q1 -->|"yes"| A1["Issue a link<br/>Email: 'you can now fill in your details'"] --> J2

    J2["**3. The two 14-day emails** (FR-63/64)"] --> Q2{"Check-in within 14 days?"}
    Q2 -->|"no"| J3
    Q2 -->|"yes"| Q3{"Status?"}

    Q3 -->|"DetailsComplete / Active"| Q4{"Already reminded?"}
    Q4 -->|"yes"| J3
    Q4 -->|"no"| A2["Pre-arrival reminder<br/>address + the booking's own times<br/>sets ReminderEmailSentAt — only if sent"] --> J3

    Q3 -->|"still Approved"| Q5{"Already nudged?"}
    Q5 -->|"yes"| J3
    Q5 -->|"no"| Q6{"Link sent more<br/>than 3 days ago?"}
    Q6 -->|"no — defer to a later run"| J3
    Q6 -->|"yes (or never sent)"| A3["Reissue the link<br/>Guest: completion nudge<br/>Admin: guest has not completed<br/>sets CompletionNudgeSentAt — only if the guest's email went out"] --> J3

    J3["**4. Deposit reminder** (FR-116)"] --> Q9{"Still AwaitingDeposit,<br/>due date passed,<br/>not yet reminded?"}
    Q9 -->|"no"| E(["done"])
    Q9 -->|"yes"| A6["Guest: deposit not received<br/>sets DepositReminderSentAt — only if sent<br/>Never cancels: the admin decides"] --> E
```

**Two flags, never one.** The pre-arrival reminder and the completion nudge are guarded separately.
A guest nudged at 14 days who then submits at 12 still receives the pre-arrival email at 10. While
one flag guarded both, that guest received neither the address nor their stated times.

**A failed email changes nothing, so the next night retries it** (NFR-13, FR-63/108). A flag is set
only on a confirmed send. Issuance and the nudge replace the guest's link before sending; if the
send fails they put the previous link back, so the guest keeps what they had and the booking is due
again tomorrow. A failed nudge also holds the admin alert, which would name a link that no longer
exists. Only the admin alert on its own is never retried — that would nudge the guest twice.

**Why issuance runs first.** The two are normally 26 days apart. If issuance has been down and a
link-less booking reaches 14 days, this order hands it the link first — the nudge then sees a link
sent seconds ago and defers on its grace period, instead of telling the same guest in the same
minute both "here is your link" and "you have not filled in your details".

---

## 3. Every issuance outcome, on one check-in date

All five bookings below arrive **2026-12-10** and leave **2026-12-13**. The only thing that differs
is when the admin approved. Three dates are fixed by that check-in:

| Landmark | Date | What happens there |
|---|---|---|
| 40 days before | **2026-10-31** | the issuance job's window opens |
| 14 days before | **2026-11-26** | pre-arrival reminder *or* completion nudge |
| 3 days before | **2026-12-07** | the deadline any link issued 14 days or fewer out runs to — a late approval *or* the nudge |

```mermaid
gantt
    title Five approvals against one check-in — 2026-12-10
    dateFormat YYYY-MM-DD
    axisFormat %d %b
    todayMarker off

    section Landmarks
    40 days before          :milestone, 2026-10-31, 0d
    14 days before          :milestone, 2026-11-26, 0d
    Check-in                :milestone, 2026-12-10, 0d

    section S1 — approved 192 days out
    Approved — no link      :milestone, 2026-06-01, 0d
    Link issued + emailed   :milestone, 2026-10-31, 0d
    Link valid              :s1a, 2026-10-31, 26d
    Guest submits           :milestone, 2026-11-05, 0d
    Pre-arrival reminder    :milestone, 2026-11-26, 0d

    section S2 — same but link sent early
    Approved — no link      :milestone, 2026-06-01, 0d
    Admin sends early       :milestone, 2026-06-15, 0d
    Early link valid        :s2a, 2026-06-15, 30d
    Dead link — no one needs it yet :s2b, 2026-07-15, 108d
    Replacement issued      :milestone, 2026-10-31, 0d
    Link valid              :s2c, 2026-10-31, 26d

    section S3 — approved 30 days out
    Approved — link in the email :milestone, 2026-11-10, 0d
    Link valid              :s3a, 2026-11-10, 16d
    Nudge + admin alert     :milestone, 2026-11-26, 0d
    Nudge link valid        :s3b, 2026-11-26, 11d
    Guest submits           :milestone, 2026-11-30, 0d
    Pre-arrival reminder    :milestone, 2026-12-01, 0d

    section S4 — approved 6 days out
    Approved — short notice :milestone, 2026-12-04, 0d
    Link valid              :s4a, 2026-12-04, 3d
    Nudge after its grace   :milestone, 2026-12-08, 0d

    section S5 — approved 1 day out
    Approved — 48h floor    :milestone, 2026-12-09, 0d
    Link valid              :s5a, 2026-12-09, 2d
```

### The same thing, exactly

| | Approved | Link issued at approval? | Link expires | Why that date | 14-day email |
|---|---|---|---|---|---|
| **S1** | 2026-06-01 (192 d) | **No** — beyond 40 days | 2026-11-26 | issued 10-31 at the 40-day mark; the 14-day cut-off binds before the 30-day ceiling | pre-arrival (submitted 11-05) |
| **S2** | 2026-06-01 (192 d) | **No**, but the admin sent one on 06-15 | early link 2026-07-15, replacement 2026-11-26 | the early link hits the 30-day ceiling; the job replaces it at the 40-day mark | pre-arrival |
| **S3** | 2026-11-10 (30 d) | **Yes** | 2026-11-26, then **2026-12-07** | 14-day cut-off; the nudge then reissues, and that link is a late issue → 3 days before check-in | nudge — guest had not submitted |
| **S4** | 2026-12-04 (6 d) | **Yes** | 2026-12-07 | late issue → 3 days before check-in | nudge on 12-08, held 3 days after the link |
| **S5** | 2026-12-09 (1 d) | **Yes** | 2026-12-11 | 3-days-before is already past → 48-hour floor | **none** — the grace outlasts check-in, and nobody nudges a guest who has arrived |

**S2 is the case that reopens the gap if the issuance job is written the obvious way.** Keyed on
"has a link ever been sent", the job would skip S2 for ever and the guest would hold a dead URL from
15 July to the nudge on 26 November — 134 days. Keyed on "is there a usable link", it replaces it on
31 October. See §2.6 and FR-108.

**S3 is the case the flag split fixes.** The nudge fires on 11-26 and the guest submits on 11-30. The
pre-arrival email with the address and their stated times must still go out on 12-01. Under one
shared flag it never did.

**S3 also shows the late-issue floor doing its second job.** The nudge reissues the link at 03:00 on
11-26 — 14 days out, so that reissue is itself a late issue and the new link runs to 12-07, eleven
days. Compared against the 14:00 check-in *instant* rather than in whole days, 03:00 read as eleven
hours early, took the ordinary cut-off, expired that same afternoon and landed on the 48-hour floor:
the guest being chased for being late got two days, while S4 — approved eight days later — got
eleven. Corrected 2026-08-10; see FR-53.

---

## 4. Terminal branches and how long the record lives

```mermaid
flowchart LR
    P["Pending"] -->|"never answered"| R1["delete 12 months<br/>after submission"]
    AD["AwaitingDeposit"] -->|"never paid, never cancelled"| R1
    AD -->|"cancelled"| R4
    P -->|"denied"| R2["delete 12 months<br/>after denial"]
    P -->|"closed (general inquiry)"| R3["delete 12 months<br/>after submission"]
    A["Approved / DetailsComplete / Active"] -->|"cancelled"| R4["delete 12 months<br/>after cancellation"]
    A -->|"stay completed"| R5["delete 2 years<br/>after check-out"]

    R1 --> W["Admin warned 30 days<br/>before deletion"]
    R2 --> W
    R3 --> W
    R4 --> W
    R5 --> W
    W --> D["Hard delete — booking,<br/>participants, meals, buildings,<br/>cleaning and facility rows,<br/>and its audit entries (FR-98)"]
```

`AwaitingDeposit` keeps the 12-months-from-submission date it had as `Pending` (FR-91), so a booking
nobody pays and nobody cancels cannot keep guest data indefinitely. Live in-progress states
(`Approved`, `DetailsComplete`, `Active`) carry **no** deletion date; the
clock only starts when the booking reaches a terminal state.

Worked example on the same booking: cancelled by the guest on **2026-11-15**. If a deposit was paid,
the cancellation email states it is not refunded, and that any further fee follows the terms in force
when the booking was confirmed (FR-79) — the email never restates the scale itself. The record is deleted
**2027-11-15**, and the admin is warned on **2027-10-16**.
