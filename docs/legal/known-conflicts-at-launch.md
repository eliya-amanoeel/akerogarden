# Known legal-text conflicts carried into launch

**Status: accepted, not resolved. Decision taken 2026-09-07 — launch proceeds regardless.**

> **Status note, 2026-09-11 — three statements below have since changed; the decisions have not.**
> (1) The privacy-policy stub described under *"The one thing that is not optional"* was removed the
> same day: PR 52 (`67ca581`) put the full policy live on 2026-09-07. (2) `implementation-plan.md`
> and `CLAUDE.md` were corrected on 2026-09-07 and no longer describe a jurist gate (§1). (3) The
> eight questions were **sent** to the client (confirmed 2026-09-10); the answers are still
> outstanding, so everything in §2–§6 stands, and §3 still waits on question 6. The body is left as
> written — this is a point-in-time record of what was knowingly carried across the launch line.
>
> **Status note, 2026-10-07 — the open items below were decided.** *(2026-10-09: built on
> `feat/deposit-before-confirmation`; they are live once that is deployed.)*
> The client asked for bookings to be confirmed only once the 20 % deposit has arrived. Working that
> through settled most of this file, so the August form was replaced by
> `docs/legal/klientfragor-2026-10-07.html` (six questions; question numbers in the body below still
> refer to the August form). Decided by the maintainer:
>
> - **§3 — resolved by design, no longer waits on question 6.** The approval and cancellation emails
>   stop restating a cancellation rule. A guest cancellation says any fee follows the terms in force
>   when the booking was confirmed, and that Åkerögården will come back with the amount — the client's
>   own §32. This reverses the 2026-09-07 decision to keep the emails unchanged, which was a holding
>   position until question 6 was answered.
> - **§4 — both defects handled.** The 8-to-4-week gap is asked again in the new form;
>   "rekommenderas" becomes "gäller" in §7. *(2026-10-08: the client left the gap unanswered — it
>   stays open in SRS §12 — and said the deposit is never refunded, so §7 is rewritten, not just
>   reworded. All terms changes are published together with the deposit work, not before; see
>   `docs/review/deposit-flow-2026-10-07.md` D14/D19.)*
> - **§5 — still open.** Planned as a quiet fix (VAT-inclusive prices), then held back on 2026-10-09:
>   the stored VAT rates are unconfirmed placeholders, so it goes to the client first (SRS §12, Q3).
> - **§6 — closed.** The client's §10 puts arrival and departure times in the confirmation, and the
>   approval email already states the booking's own times; 14:00/12:00 are editable defaults.
> - **The points routed to the jurist** (archived `docs/archive/klientfragor-juridiska-underlag.md`):
>   allergy data stays on explicit consent (art. 9.2 a), as built. Retention periods stand — the
>   booking system stores no amounts, invoices or receipts, so the bookkeeping act's seven years does
>   not reach it. Cookie and role descriptions were verified against the code. The client is told in
>   the new form that a record of processing (art. 30) is required, since allergy data removes the
>   small-organisation exemption.

This file exists so that nobody later finds these conflicts and assumes they were missed.
They were found, written down, and knowingly carried. It is a record, not a to-do list.

---

## 1. The jurist review is not happening

Every planning document up to 2026-09-07 described Phase 10d as *"gated on the jurist review of
the Swedish legal text"*. **That gate is gone.** No jurist review is expected, before or after
launch. `implementation-plan.md`, `CLAUDE.md` and the phase notes still say otherwise wherever
they have not been corrected — treat this file as the current truth on that point.

The consequence is that no external reviewer will catch the items below. They ship as they are.

## 2. The client replaced the terms document rather than answering the questions

On 2026-08-23 the client returned a complete, independently written *Boknings- och
vistelsevillkor* (33 sections, kept at `docs/legal/klientsvar.md`) instead of filling in the
eight questions we sent (`docs/archive/klientfragor-juridiska-underlag.md`, and the plain-language
rewrite that followed it).

The returned document is more thorough than our draft and is the client's own text, so it is
what gets published. It answers Q3, Q6 and Q7 outright — including the important one, **Q7: yes,
private individuals book as consumers** (§1 defines *beställare* as "juridisk **eller fysisk**
person"; §9 and §31 both presuppose it).

**Eight questions remain open at launch.** They are, in the plain-language numbering of
`klientfragor-2026-08-23.html`: when the balance is due · what happens if it is unpaid on arrival ·
kitchen and common-room access · how long after departure a damage may be charged · the share of
phone/email bookings · **what applies to a cancellation between 8 and 4 weeks** · whether the
cancellation rules bind at all · pets. They are now post-launch cleanup.

## 3. The cancellation clause in the emails contradicts the published terms

This is the substantive one.

`BookingEmailTemplates.cs:287` (approval email) and `:437–440` (cancellation email) state the
**20% deposit / two-month** rule. The client's §7 states a **percentage-of-value scale**. Both are
published: the emails say one thing, `/bokningsvillkor` says another.

| Guest cancels | Our email tells them | Client's §7 |
|---|---|---|
| 10 weeks out | Full deposit refunded | No fee — agree |
| 6 weeks out | Deposit forfeited | *no rule — the gap in §7* |
| 3 weeks out | Deposit forfeited (20% of total) | **75% of the full booking value** |
| No-show | *silent* | 100% |

At three weeks the guest is told in writing that they owe roughly a quarter of what the terms
impose. **This cuts against the property, not the guest:** against a consumer, the email actually
sent generally beats the terms page it linked to. FR-111 stores `TermsAcceptedAt` precisely as
evidence that the terms were incorporated into the contract — where the two disagree, that
evidence works against the property.

**Not in conflict:** `BookingEmailTemplates.cs:286` (*"handpenning på 20 %... resterande 80 %"*).
The client's §6 says Åkerögården *"kan begära förskottsbetalning eller bokningsavgift"* and leaves
the amount to the bokningsbekräftelse — naming 20% specifies what §6 leaves open. This line is
consistent with the adopted terms and needs no change.

### The SRS now diverges from the adopted contract

The email wording is not an oversight — **it is what the SRS mandates**: §5.6, FR-79, and the
approval-email row in §9 all specify 20% / two months. So the divergence is between
`requirements.md` and the client's adopted document, not between the code and the SRS. The code is
correct against its specification; the specification is now behind the contract.

Resolving it later means moving one rule across four places: `BookingEmailTemplates.cs:287`,
`CancellationPolicy.cs` (which is binary `FullRefund`/`Forfeited` on a two-month cutoff and would
need three tiers), SRS §5.6, and FR-79. **It cannot be completed until question 6 is answered** —
§7 has no rule between 8 and 4 weeks, so there is nothing for the code to implement there.

## 4. Two defects in the client's own document

Neither was corrected before launch.

- **The cancellation scale has a hole.** §7 covers "more than 8 weeks" and "4 to 2 weeks", with
  nothing in between. A cancellation six weeks out falls through it.
- **§7 calls its own terms a recommendation** — *"rekommenderas följande avbokningsvillkor"*.
  Terms that describe themselves as recommended are weak to unenforceable, most of all against a
  consumer, which §1/§9/§31 confirm the client has.

## 5. Consumer status has a knock-on nobody has actioned

Because private individuals book as consumers (§1, §9, §31), `/mat-och-priser` stating
**"Alla belopp anges exklusive moms"** is likely wrong under prisinformationslagen (2004:347),
which requires a price aimed at a consumer to be shown inclusive of VAT. Unchanged at launch.

## 6. Check-in / check-out times are an assumption with nothing behind them

The client's §10 defers the times to the bokningsbekräftelse and names none. The old terms said
**14:00 in / 12:00 out**, and the public booking form still assumes those. The question asking the
client to confirm them was deliberately removed from the questionnaire on 2026-08-23. Nothing
currently supports the times the site shows.

---

## The one thing that is not optional

Everything above is a commercial or contractual risk that the client owns. **The privacy policy is
different** — it is legally required, and the version on `main` is a 42-line stub that renders:

> *"Uppgifter om personuppgiftsansvarig fylls i före lansering."*
> *"De exakta lagringstiderna för olika typer av bokningar anges här före lansering."*

Missing controller identity (GDPR art. 13.1 a) and missing retention periods (art. 13.2 a) are
mandatory failures, on a site collecting names, emails, phone numbers and **allergy data**
(art. 9 sensitive). The page also states its own incompleteness in text a regulator would quote back.

**The fix needs nothing from the client.** The real policy — 398 lines, no `[ATT BEKRÄFTA]` left,
controller named, retention periods stated — already exists on
`feat/legal-pages-terms-and-allergy-consent`. Merging that branch is what removes this.

## Decisions standing as of 2026-09-07

- Launch proceeds without the eight answers and without a jurist review.
- The client's document is published as-is, defects and all.
- **The emails are kept unchanged** — including the two-month cancellation clause, with the
  contradiction in §3 above accepted.
- **The FR-109 acknowledgement checkbox stays as built.** No requirement is bypassed.
- The eight questions are sent as post-launch cleanup, not as a gate.

*Recorded by Claude on 2026-09-07 at the user's instruction. The consequences above were stated
before the decision and accepted.*
