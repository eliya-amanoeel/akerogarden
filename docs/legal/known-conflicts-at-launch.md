# Known legal-text conflicts carried into launch

**Status: accepted, not resolved. Decision taken 2026-09-07 — launch proceeds regardless.**

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
eight questions we sent (`docs/legal/klientfragor-juridiska-underlag.md`, and the plain-language
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
