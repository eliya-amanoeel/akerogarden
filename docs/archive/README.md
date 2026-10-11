# Archive — closed history

Read-only. **Nothing in this folder is a source of truth about how the system behaves today**, and
nothing here is waiting on anyone.

For current truth read [`docs/product/requirements.md`](../product/requirements.md). For what is
still to be done, ask the maintainer — it is not tracked in here.

---

## The rule for what belongs here

A document moves into `archive/` when **both** are true:

1. **Nothing is waiting on it.** No open item, no parked finding with a re-open trigger, no question
   awaiting an answer.
2. **Nobody has to act on it.** It is consulted to understand *why* something is the way it is, never
   to find out what to do next.

**If either fails, it stays where it is.** The point of this folder is to make "closed" legible, not
to make maintenance optional — a stale document is worse than no document, and moving one here does
not make it less wrong, only less visible. That failure has already happened once in this repo: the
implementation plan was found asserting three things its own neighbouring rows contradicted.

Documents that were deliberately **not** archived, and why:

| Kept in place | Why |
|---|---|
| `docs/review/post-launch-security-review.md` | Still the tracker for two parked findings with explicit re-open triggers — #16 "re-open if it rises or reaches a user", #13 "close it opportunistically" — and for the deferred `AddDbContextFactory` fix, whose trigger conditions are written into finding #2 |
| SRS §12 (Open Questions) | Where any question waiting on the client is recorded — none since 2026-10-09. The client forms themselves (`klientfragor-*.html`) were working copies, never tracked, and were deleted on 2026-10-10; their questions and answers live in the decision records and the SRS |

---

## What is in here

| Document | What it was, and when it closed |
|---|---|
| [`implementation-plan.md`](implementation-plan.md) | The phased build plan, phases 0–11. Every phase is closed (Phase 11 on 2026-09-09) and each is one summary table row. Its header explains why it was collapsed rather than deleted. Carries the **phase → requirement map**, the one piece of provenance the SRS deliberately does not hold |
| [`requirements-changelog.md`](requirements-changelog.md) | Frozen SRS history up to the retirement of document versioning (2026-07-18). **Never append to it** — requirement changes are now edited in place in the SRS, with the rationale in the commit body |
| [`architect-review.md`](architect-review.md) | Pre-development architectural review, June 2026. All 20 Round 2 items resolved and folded into the SRS. Kept for the pre-build design debate: which alternatives were considered and rejected. Its `v0.x` references point at SRS versions that no longer exist |
| [`Boknings-och-ordningsvillkor.md`](Boknings-och-ordningsvillkor.md) | Our own draft booking terms (summer 2026), replaced on 2026-08-23 by the client's document, which is what is published (`docs/legal/Bokningsvillkor.md`, named `klientsvar.md` until 2026-10-10). Archived 2026-10-08 |
| [`known-conflicts-at-launch.md`](known-conflicts-at-launch.md) | What was knowingly carried across the 2026-09-07 launch line in the legal texts — the jurist review that never happened, the client's replacement terms, the email/terms cancellation conflict, VAT pricing, check-in times — and how each was settled. Every item resolved by 2026-10-09 (the deposit work and the client's answers); archived the same day |
| [`klientfragor-juridiska-underlag.md`](klientfragor-juridiska-underlag.md) | The first, prose version of the client questions (mid-August 2026), written for a jurist review that never happened. Replaced by the plain-language forms; its last open part — the points routed to the jurist — was decided on 2026-10-07. Archived the same day; outcomes live in `known-conflicts-at-launch.md`, archived beside it |
| [`demo-feedback-2026-08-05.md`](demo-feedback-2026-08-05.md) | Decision record for the client demo of 2026-08-05: every change request, the decision on each and its reasoning, two design corrections of our own (§7) and a conflict audit (§8). Kept in `docs/review/` until 2026-10-11 as "consulted, not merely remembered" — which is what this folder is for. Its two deferred items (1b name upload, 5a reviews) live in SRS §10. Archived 2026-10-11 |
| [`deposit-flow-2026-10-07.md`](deposit-flow-2026-10-07.md) | Decision record for the deposit before confirmation (FR-115/116, live 2026-10-09) and the estimated price (D22–D27, live 2026-10-11): design, rejected alternatives, every decision and whose it was. Nothing open; archived 2026-10-11, the day its last decision went live |
| [`security-review.md`](security-review.md) | The Phase 7 security gate deliverable, 2026-07-08. No vulnerabilities found; its three items deferred to live Azure verification were all closed during the 2026-09-08/09 post-launch review |

Nothing here is retro-edited. Paths inside a body are as they were on the day — files since renamed or
moved (`docs/legal/klientsvar.md` → `Bokningsvillkor.md`, `docs/legal/` and `docs/review/` → here) are not
chased through the text. Where a document's premises later expired, that is recorded in a header
note rather than by rewriting the body — a point-in-time record loses its value the moment it is
quietly brought up to date.
