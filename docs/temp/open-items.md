# Open items

Short by design. Things that are **decided but not done**, or **found but not yet a problem** — the
ones that would otherwise live only in a commit message, a code comment, or a 150 KB plan section
where nobody rereads them.

Not a backlog for features. Features go in `implementation-plan.md`. This is for the handful of
things a solo maintainer would otherwise lose track of.

**Delete an entry when it is done.** An open-items file that only grows is one nobody trusts.

---

## 1. Meal grid layout on the completion form

**Raised:** 2026-08-07 (smoke test) · **Status:** parked by decision · **Effort:** unknown

A 3-day stay renders six dish dropdowns as day-cards with long Swedish date headings
(`torsdag 10 augusti`), with "Lunch" and "Middag" repeating inside each. A 5-day stay is five
near-identical blocks. It reads as several separate forms rather than one menu for the stay.

Proposal not yet accepted: **one row per day, lunch and dinner side by side**, so the whole stay is
visible at once and an unfilled meal is obvious — stacking to the current shape on mobile.

Parked deliberately during slice 3; the specific complaint ("we could work a bit more on the meal
grid") was never narrowed down to wording, density, or the hidden edge-day dropdowns.

---

## 2. `operational-tasks.md` is referenced but does not exist

**Found:** 2026-08-07 · **Severity:** cosmetic

`implementation-plan.md` (three places), `requirements-changelog.md` and
`review/architect-review.md` all point at `operational-tasks.md`. There is no such file and there
never was. The items those references describe — the staging cold-start lag watch item, the
go-live content blockers — are either resolved or tracked in their phase.

**To fix:** either drop the references, or move the surviving items here and delete the phrase.

---

## 3. Kaldéiska Kursgård on the start page — partly done, one question left

**Found:** 2026-08-07 · **Partly done:** 2026-08-11 · **Severity:** cosmetic

*Kaldéiska Kursgård* is now set under *Åkerögården* **inside the logo lockup**, so it appears in the
public header on every page (start page included), in the admin sidebar, and on the auth cards. It is
deliberately nowhere else — it travels with the logo rather than being repeated as page content.

**Still open:** the original wording said *"on the start page"*, and it is not certain whether that
meant the header mark (now satisfied) or a visible mention in the start page's own **content** — a
line in the hero, the trust strip, or the closing band. Not closed, because closing it on the
generous reading would bury the request.

**To fix:** confirm with the client which was meant. If the header is enough, delete this entry.

---

## 4. Re-run the axe E2E pass after the 2026-08-11 colour changes

**Raised:** 2026-08-11 · **Status:** waiting on CI · **Severity:** verification only

`--color-muted` was deepened `#6f6a60` → `#646159`, and the neutral and active badges were
tokenised (`--color-neutral` `#5d5b56`, `--color-active` `#2c4708`) after `.badge--neutral` was
found **failing AA at 3.96:1** on a hovered `/bokningar` row — the badge tint is translucent, so its
real ground is that tint over a row that `:hover` swaps to `--color-bg-warm`.

Every pair was measured by hand and the reasoning is in `design.md`, but the axe E2E suite has not
been run locally against the new values (53 `--color-muted` consumers changed).

**To fix:** confirm the accessibility E2E pass is green in CI on PR #43. Delete this entry when it
is. Nothing else is expected to move — the changes are one-directional darker on light grounds only.