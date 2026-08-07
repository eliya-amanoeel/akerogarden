# Open items

Short by design. Things that are **decided but not done**, or **found but not yet a problem** — the
ones that would otherwise live only in a commit message, a code comment, or a 150 KB plan section
where nobody rereads them.

Not a backlog for features. Features go in `implementation-plan.md`. This is for the handful of
things a solo maintainer would otherwise lose track of.

**Delete an entry when it is done.** An open-items file that only grows is one nobody trusts.

---

## 1. `--color-err` passes WCAG AA by 0.03

**Found:** 2026-08-07 · **Severity:** not currently failing · **Effort:** ~15 min

The semantic colours pair a text colour with a **translucent** background, so their real contrast
depends on the surface beneath them. Measured against both surfaces the app actually uses:

| token | on page `#faf8f4` | on warm tint `#f0ebe1` | headroom on the tint |
|---|---|---|---|
| `--color-err` | 5.05 | **4.53** | **0.03** |
| `--color-ok` | 5.44 | **4.91** | 0.41 |
| `--color-warn` | 6.13 | 5.55 | comfortable |
| `--color-info` | 6.44 | 5.79 | comfortable |

AA needs **4.5**. `err` clears it by three hundredths — that is a coin on its edge, not a pass.
Anything that darkens what sits behind it tips it under: a nested tinted container, a hairline
border, or someone adjusting the `.09` alpha.

**Why it is not fixed already:** nothing fails today, and deepening a design token on a hunch rather
than a measurement is how the palette drifts. But this exact failure has already shipped **twice** —
the green in Phase 6, and `--color-warn` on 2026-08-07 (4.49 on the tint, failing on the `Väntar`
badge). Both were caught by the axe E2E test in production-shaped code, not in review.

**To fix:** deepen `--color-err` (`#b3341a` → roughly `#a32e17`) until it clears ~5.0 on the tint,
then re-run the axe E2E tests to confirm on the rendered page — the arithmetic above is a
*prediction*, and reading colours off CSS has given the wrong answer here before. Same for `ok` if
you want margin there too.

**Related:** the warm-tint rule in [`../ux/design.md`](../ux/design.md); the re-measure item in the
demo-feedback exit criteria in [`../product/implementation-plan.md`](../product/implementation-plan.md).

---

## 2. Meal grid layout on the completion form

**Raised:** 2026-08-07 (smoke test) · **Status:** parked by decision · **Effort:** unknown

A 3-day stay renders six dish dropdowns as day-cards with long Swedish date headings
(`torsdag 10 augusti`), with "Lunch" and "Middag" repeating inside each. A 5-day stay is five
near-identical blocks. It reads as several separate forms rather than one menu for the stay.

Proposal not yet accepted: **one row per day, lunch and dinner side by side**, so the whole stay is
visible at once and an unfilled meal is obvious — stacking to the current shape on mobile.

Parked deliberately during slice 3; the specific complaint ("we could work a bit more on the meal
grid") was never narrowed down to wording, density, or the hidden edge-day dropdowns.

---

## 3. `operational-tasks.md` is referenced but does not exist

**Found:** 2026-08-07 · **Severity:** cosmetic

`implementation-plan.md` (three places), `requirements-changelog.md` and
`review/architect-review.md` all point at `operational-tasks.md`. There is no such file and there
never was. The items those references describe — the staging cold-start lag watch item, the
go-live content blockers — are either resolved or tracked in their phase.

**To fix:** either drop the references, or move the surviving items here and delete the phrase.
