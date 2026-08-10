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

## 3. Kaldeiska Kursgård should be added on the start page (Public)

**Found:** 2026-08-07 · **Severity:** cosmetic

**To fix:** add that text somewhere we agree upon

---

## 4. Logo

**Found:** 2026-08-07 · **Severity:** cosmetic

**To fix:** Update the logo.