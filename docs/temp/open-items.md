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

## 4. `--color-muted` clears AA on the warm tint by two hundredths

**Found:** 2026-08-11 · **Status:** decision pending · **Severity:** not currently failing

`--color-muted` `#6f6a60` measures **5.07:1** on `--color-bg` but **4.52:1** on `--color-bg-warm`
`#f0ebe1`. That passes, but `design.md`'s own margin rule — written after the green, warn and error
tokens each shipped on the same knife edge — says a margin under ~0.5 is a finding, not a pass.

It had been recorded in `design.md` as a bare "5.0:1" with no ground named, which is why it went
unnoticed; that line is now corrected. Muted text sits on the warm tint in at least seven places:
`.chip`, `.pcard__head`, `.cal-selection`, `.bldg-tree__toolbar .cols`,
`.admin-table tbody tr:hover .sub`, and `.lodging-note__sub` / `__rooms`.

Not fixed on the spot on purpose: `--color-muted` is one of the most-used tokens in `site.css`, so
deepening it shifts every hint, caption, label, table header and empty state at once. That is a
decision, not a drive-by.

**To fix:** either deepen to `#676560` (4.90 / 5.49 / 5.82, keeps the warm-grey bias) and re-run the
axe E2E pass, or accept 4.52 explicitly and record *that* in `design.md` so the next person doesn't
re-open it.