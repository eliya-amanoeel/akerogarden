# Åkerögården — Design System

Visual + experiential design reference for the site: art direction, tokens, type scale, components (with states), the public shell, and logo usage. This is the **single source of truth for design**, evolved in Phase 6 (UI/UX). If a component in code drifts from this doc, reconcile them — don't let them diverge silently.

> **Status:** Current — direction **A — "Editorial lugn"**, built and shipped in Phase 6 (merged 2026-07-07). Phases 0–5 had shipped a deliberately functional-but-unstyled UI ("functional first, polish last"); this system replaced it across both surfaces. The component classes documented here are in the codebase — if code and doc drift, reconcile them.

---

## Art direction — "Editorial lugn"

Åkerögården is a rural retreat & conference property near Norrtälje, running since 1978, hosting a **mix** of groups (families, companies, courses, community/church) from a handful of people up to 79 overnight guests. The public site's single job is to **convert visitors into inquiries** (the booking funnel is the primary KPI); the admin panel's job is efficient daily operation.

The chosen look is **calm, timeless, editorial**: generous whitespace, large light serif headings, a warm off-white ground, and **green used sparingly** as an accent — photography carries the emotion. The *page* stays welcoming and neutral — not corporate, not institutional — so any kind of group can picture themselves there.

> **The neutrality no longer extends to the logo, and that was the client's call.** This section previously read "not 'churchy'". In 2026-08 the client supplied a new identity — a cross-topped church cradled in a hand, with *Kaldéiska Kursgård* under the wordmark — which is the plain opposite of that instruction. It was raised explicitly before the work started: the booking funnel is the primary KPI, the copy is deliberately written for families, companies and courses as well as community groups, and a confessional mark narrows who feels invited. The client owns the property and the brand, chose it knowingly, and that settles it. What follows from the decision is that **the surrounding page must carry the neutrality the mark no longer does** — the copy stays plainly secular, the imagery stays the landscape and the buildings, and no other confessional signal is added anywhere. Do not "harmonise" the rest of the site toward the logo.

### Design principles (the north star)

1. **Lugn, inte lyx** — calm and spacious; earn trust through restraint, not decoration.
2. **Gruppen först** — every decision serves someone responsible for a whole group: capacity, clarity, reassurance.
3. **Ärlig och konkret** — real numbers, real photos, transparent prices. No hype, no stock-photo gloss.
4. **En tydlig väg framåt** — every public page funnels to one obvious next step; never a dead end.
5. **Tillgänglig på riktigt** — older eyes, phones, rural connections: generous type, big tap targets, WCAG AA, fast pages, keyboard-friendly.

**Admin adds a sixth working rule:** *density & speed over beauty* — the admin surface reuses these tokens but optimises for scannable lists, tight forms, and fast workflows (productivity design, not persuasion).

### Voice & tone

Warm, plain Swedish addressing the reader as **du**. Confident but never salesy (Swedish audiences distrust hype). Short sentences; concrete over abstract. Reassure at decision points — the model is inquiry → approval, so *"En förfrågan är inte bindande"* recurs. Copy speaks to **small groups too** ("från några familjer till 79 gäster"), never only the big number.

**Primary call-to-action sitewide: "Skicka förfrågan".** Secondary: "Se tillgänglighet".

### Imagery strategy

Real photography is the biggest trust lever, so the design is **built for real photos** but ships with **tasteful placeholders** (soft landscape gradients, labelled *"Platshållare — foto vid lansering"*), swapped for real Åkerögården photography at go-live — the client's responsibility, not tracked as an engineering task. Never stock-photo gloss.

---

## Architecture

.NET 10 Blazor — **Static SSR** for all public pages, Interactive Server for admin. Styles live in `wwwroot/css/site.css`; no CSS framework, no component library — plain hand-written CSS.

**Mobile-first**, breakpoints at `480px` (tablet) and `1024px` (desktop). A few components add their own local breakpoint where the content demands it (e.g. `640px` for the calendar's docked selection bar, `720px` for the two-column month grid, `768px` for the three-up card rows) — these are component-level, not new global tiers.

**Mobile fold discipline.** The hero image yields height to the call to action, not the other way round: the booking funnel is the primary KPI, so `Skicka förfrågan` must be reachable without scrolling on a small phone. Measured at 375px the CTA sat at 640px with a 15rem hero image — below the fold once browser chrome is counted — so the mobile image is capped at `11rem` and grows back from 480px up. Re-measure before increasing it.

Public pages are Static SSR (no SignalR circuit), so any interactivity is plain progressive-enhancement JS/CSS — **not** Blazor interactivity. The **mobile nav is a CSS-only checkbox toggle** (no JS required), so it works under Static SSR. The header is a **solid sticky bar** (see Shell) — the earlier transparent-over-hero + scroll-JS + logo-swap approach is retired in direction A.

---

## Fonts

Both typefaces are **self-hosted** — no Google Fonts CDN, no external font requests (NFR-35). Loading Google Fonts sends visitor IPs to Google without a legal basis (Austrian DPA ruling, Jan 2022; enforced in Germany too). No consent banner is needed for self-hosted fonts.

Files live in `wwwroot/fonts/`, declared with `@font-face` in `site.css`.

- **Roboto** — body & UI. Weights: **400, 700 only**.
- **Playfair Display** — headings. Weights: **400, 700 only**.

> **Only 400 and 700 exist** — do not design for 500/600 (the browser would synthesize or fall back). The editorial look deliberately uses the **light 400** for large display headings and 700 for emphasis/labels/buttons.

---

## Design Tokens

Defined as CSS custom properties on `:root` in `site.css`. Style components through the tokens.

### Colour

```css
/* Brand */
--color-primary:        #71b002   /* bright green — FILLS / accent rules / marks (pair with dark text) */
--color-primary-hover:  #5e9600   /* primary fill hover */
--color-primary-ink:    #3f6a0f   /* green as TEXT / links — AA on both light grounds */
--color-secondary:      #865628   /* warm brown — secondary accent */

/* Neutrals (warm-biased, not raw grey) */
--color-heading:        #1e1c1a   /* headings + primary-button text */
--color-text:           #4a4845   /* body copy */
--color-muted:          #6f6a60   /* hints, captions, labels */
--color-bg:             #faf8f4   /* page background (warm off-white) */
--color-bg-warm:        #f0ebe1   /* section tint */
--color-footer-bg:      #1a1714   /* dark footer */
--color-line:           rgba(30,28,26,.12)  /* hairline borders */

/* Semantic (alerts, validation, status pills) */
--color-ok:   #3f6a0f   --color-ok-bg:   rgba(113,176,2,.12)
--color-warn: #7a4c00   --color-warn-bg: rgba(214,158,46,.16)
--color-err:  #8f2915   --color-err-bg:  rgba(179,52,26,.09)
--color-info: #6f4a1f   --color-info-bg: rgba(134,86,40,.10)

/* Hairline per family, each at its own alpha — three different hues, not one hue repeated */
--color-err-line:  rgba(179,52,26,.32)
--color-warn-line: rgba(214,158,46,.4)
--color-info-line: rgba(134,86,40,.3)
```

**The green rule (accessibility-critical).** `#71b002` is a light lime — it **only passes WCAG AA as a fill with dark text** (`#71b002` + `#1e1c1a` = 6.5:1). As white-on-green it fails (2.65:1) and **must not be used that way**. As **text/links**, use the deepened `--color-primary-ink` `#3f6a0f` — it clears AA on **both** the off-white `#faf8f4` *and* the warm `#f0ebe1` section tint (an earlier `#4c7a12` passed on off-white but missed 4.5:1 on the warm tint — caught by the axe E2E test). Darkening the *fill* toward white-text-legibility instead turns it olive — rejected; keep the bright fill + dark text.

**The warm-tint rule (same trap, second occurrence).** `--color-warn` was deepened from `#8a5a00` to `#7a4c00` on 2026-08-07 for the identical reason. Because `--color-warn-bg` is **translucent**, a warn element's contrast is decided by the surface beneath it: the old pair measured 5.2:1 over white but **4.49:1** over the warm `#f0ebe1` tint — failing AA by a hundredth, which is where the `Väntar` status badge sits on `/bokningar` and `/kalender`. Caught by the axe E2E test, as the green was. `.alert--warn` had already been given `#7a4c00` as a local override, so the system was carrying the fix and the bug side by side; the token now holds it and the override is gone.

**The warm-tint rule, third occurrence — and the last one.** `--color-err` was deepened from `#b3341a` to `#8f2915` on 2026-08-10, for the third time the identical trap: measured over the warm tint it gave **4.53** — three hundredths of headroom, which is a coin on its edge, not a pass — and that is where the `Avbokad` / `Nekad` badges sit on `/bokningar`. Unlike the green and the amber, this one was **not** caught by the axe test, because 4.53 passes; it was caught by measuring all four pairs deliberately. The deepened value is not a new colour: `.alert--error` already carried it.

> **The general rule these three share:** any semantic colour paired with a **translucent** `-bg` must clear 4.5:1 against **both** the off-white `#faf8f4` *and* the warm `#f0ebe1` tint, because the same component appears on both. Checking only against white is how the first two shipped, and a bare pass is how the third nearly did. **A margin under ~0.5 is a finding, not a pass** — treat it as failing and deepen. Measured after the 2026-08-10 fix: `err` ~7.3, `info` 5.79, `warn` 5.55, `ok` 4.91.
>
> **The corollary: a translucent `-bg` belongs to inline components only.** On anything
`position: fixed` the surface underneath is not a known ground — it is whatever the page happens to
be scrolled to — so the contrast cannot be reasoned about at all, and page content shows straight
through the panel. Give floating elements an opaque background: either an existing solid token, or
the intended tint **flattened** onto its base. The error toast shipped translucent on 2026-08-10 for
exactly this reason (`#ebdbcf` is its `--color-err-bg` flattened onto `--color-bg-warm`). Verified
2026-08-10: every other fixed/sticky element is opaque; the only translucent ones are
`.modal-overlay` and `.admin-scrim`, where that is the point.

**The brand teal is a logo colour, never a text colour — the fourth occurrence, and the first one caught before it shipped.** The 2026-08 identity introduced a teal, `#3a9698`, which is fine inside the mark (logotypes are exempt from 1.4.3/1.4.11). Set as **text** it measures **3.30:1** on `#faf8f4` and **2.95:1** on the warm tint — the same trap that caught green, warn and err above, arriving from a new direction: not a translucent `-bg` this time, but a colour picked for a *fill* and then reused for *type*. The lockup's `Kaldéiska Kursgård` tagline therefore uses a deepened **`#1e7070`** (5.49:1 / 4.90:1), exactly the `--color-primary` → `--color-primary-ink` split this palette already runs. On dark grounds use `#5cb9bb` (6.86:1 on the sidebar, 7.74:1 on the footer).

> **There is deliberately no `--color-teal` token.** All three values live inside the logo SVGs, and no CSS rule consumes teal — a token whose only user is a generated asset is a token nobody maintains. If a component ever needs teal in CSS, promote `#1e7070` then, per the duplication rule below, and not before.

**The duplication rule (when a hardcoded value must become a token).** A colour a component hardcodes locally is acceptable while it has exactly **one** user. The moment a second rule needs the same value, it becomes a token — one definition, every rule pointing at it. This is not tidiness; it is how the warn bug above survived: `.alert--warn` already carried the corrected `#7a4c00` as a local override while the token still held the failing `#8a5a00`, so the system shipped the fix and the bug side by side, and neither one knew about the other. It nearly repeated on 2026-08-10 — `#8f2915` (error text on `--color-err-bg`) was hardcoded in two `.alert--error` rules and a third was about to copy it, along with a fourth ad-hoc alpha of the same red. It became `--color-err-text` and `--color-err-line` instead.

**And then the promotion itself became the duplicate.** `--color-err-text` `#8f2915` sat alongside `--color-err` `#b3341a` — two dark reds a shade apart, one for text on a tint and one for text everywhere else, which is a distinction no one reading a rule can hold. Once the warm-tint measurement showed `--color-err` should have been the darker value all along, the two collapsed into one and `--color-err-text` was deleted (2026-08-10). **A token that exists only to be the correct version of another token is a bug in the palette, not a feature of it** — fix the original. `-bg` and `-line` legitimately stay derived from the brighter red: they are tints and hairlines, not text.

> **Before adding a variant, grep `site.css` for the literal you are about to type.** If it is already there, promote it rather than repeating it.

**The four alert variants are the worked example — all now token-only (2026-08-10).** Clearing the last three instances is what showed how this drift actually accumulates: it is not one careless rule, it is a *fix landing on the token and missing a consumer*. `.alert--warn .alert__ic` was still painting itself `#8a5a00` — the pre-2026-08-07 warn value, the one deepened *because it failed contrast* — three days after the token was corrected and the doc above recorded the override as "gone". It was found by reading the four variants side by side, not by grepping for a known-bad value, because nobody knew that one was there.

> **So the rule has a second half: when you correct a token, grep the old value before you finish.** The component that copied it will not announce itself, the build will not complain, and the tests will not either — `#8a5a00` on white still passes AA, so it would have sat there indefinitely looking deliberate. `.alert--info` was the same story with `#5a3d1f`, and `.btn-outline.btn-danger` carried a fourth alpha of the error red.

Verified contrast: link `#3f6a0f` 5.6:1 on `#faf8f4` / 5.1:1 on `#f0ebe1` · body `#4a4845` 11.9:1 · muted `#646159` **5.83:1 on `#faf8f4` / 5.20:1 on `#f0ebe1`** · error `#8f2915` ~7.9:1 on `#faf8f4` / ~7.3:1 on `#f0ebe1` · brown-on-white 6.2:1.

**The fourth occurrence, and the one that shows the rule was still too weak (2026-08-11).** `--color-muted` was `#6f6a60`, recorded on the line above as a bare "5.0:1" with **no ground named** — the only entry there missing its grounds, which is exactly why nobody could check it. Measured properly it was 5.07:1 on `--color-bg` but **4.52:1** on the warm tint. That alone was just another thin pass. Chasing it down surfaced something worse.

> **A translucent tint over a surface that itself changes is two variables, not one.** `.badge--neutral` sets `color: var(--color-muted)` on `rgba(30,28,26,.07)`. That tint is translucent, so the real ground is the tint composited over the **row** — and `.admin-table tbody tr:hover` swaps that row to `--color-bg-warm`. Against the resulting `#e1ddd3` the pair measured **3.96:1: a live AA failure**, on hover, on the `Avslutad` and `Besvarad` badges on `/bokningar`. It had been invisible because the badges were only ever checked against white.
>
> So the rule stated for the three semantic families is not enough on its own. **Check a translucent `-bg` against every surface its container can take, including hover and selected states — not just the page grounds.** The badge's own tint is one layer; the row underneath is another.

Two fixes, deliberately separate. `--color-muted` is deepened to `#646159` (5.20 / 5.83 / 6.18), which is safe in one direction only because all 53 of its consumers sit on white, `--color-bg` or `--color-bg-warm` — the dark footer and admin sidebar use their own greys, so nothing gets worse. It stays 1.47:1 from `--color-text`, enough that the hierarchy still reads. And the neutral badge gets **its own** `--color-neutral` `#5d5b56` (5.00 hovered, 5.90 at rest), because the deepened token alone would only have reached 4.56 — passing by 0.06, the coin-on-its-edge this rule exists to reject. It was the only badge borrowing a general-purpose token for a job that token was never picked for.

**The status families are now all tokens, and that is the point (2026-08-11).** The first attempt at the fix above put `#5d5b56` straight onto `.badge--neutral` as a literal, on the grounds that the duplication rule permits a single-user hardcode. That reading was too literal. These are *semantic status colours* whose values come from contrast measurements, so they have to be greppable when the greys are next touched — a hardcoded one is precisely how `.alert--warn .alert__ic` kept painting itself `#8a5a00` for three days after the token was corrected. `.badge--active` was tokenised at the same time for the same reason; its `#2c4708` is genuinely **not** a near-duplicate of `--color-ok`, because its tint is `0.22` where the ok tint is `0.12` and `--color-ok` over it measures only 4.54 on a hovered row. A different colour for a measurably different ground is the rule working, not the rule being broken.

### Typography

| Role | Family / weight | Size (desktop) |
|---|---|---|
| Display (hero) | Playfair **400** | `clamp(36px, 5vw, 56px)`, line-height 1.05, `-0.015em` |
| H1 | Playfair 400 | ~38px |
| H2 (section) | Playfair 400 | `clamp(28px, 3.6vw, 40px)` |
| H3 | Playfair **700** | ~21px |
| Eyebrow / label | Roboto **700** | 12px, `letter-spacing .18em`, uppercase, colour `--color-primary-ink` |
| Lead / ingress | Roboto 400 | ~18px |
| Body | Roboto 400 | 16px, measure ≤ ~60ch |
| Small / caption | Roboto 400 | ~13.5px, colour `--color-muted` |
| Button | Roboto 700 | ~15px |

### Shape, spacing & motion

```css
--radius-sm:    2px    /* buttons, inputs — editorial = sharp, not rounded */
--radius-card:  3px
--radius-modal: 6px
--transition:   160ms ease
```

- **Spacing:** 4-based rhythm — 4 · 8 · 12 · 16 · 24 · 32 · 48 · 64 · 96.
- **Elevation:** minimal — sections separate by whitespace, hairline `--color-line` borders, and thin green rules, **not** drop shadows. Shadows are reserved for overlays (modal/dropdown).
- **Motion:** subtle colour/border transitions (`--transition`). All motion is wrapped by `@media (prefers-reduced-motion: reduce)`.

---

## Components (states)

Each component supports the relevant states: **default / hover / focus / active / disabled / loading / empty / error**. Focus is always a visible `2px` outline in `--color-primary-ink`, offset 3px (keyboard accessibility, NFR-21).

- **Buttons** — `.btn` primary (bright-green fill + ink text; hover → `--color-primary-hover`; loading spinner; disabled 40% opacity, `not-allowed`); `.btn-outline` (ink border, inverts on hover); `.btn-ghost` (text button in `--color-primary-ink`). The hero/CTA secondary adds an underline treatment (scoped, so admin/calendar ghost buttons stay plain).
- **Links** — `--color-primary-ink`. **Inline links in running text are underlined** (WCAG 1.4.1 — colour alone isn't a sufficient distinction; axe "link-in-text-block"); nav/footer/buttons/cards are set apart by layout, not underlined. Reset via **`:where(a)`** (zero specificity) so component classes always win — this prevents the base link colour from silently overriding `.btn` text (a real bug caught in slice 2).
- **Form controls** — label (700, ink) + input + hint/`--color-muted`; text input / textarea / select / checkbox / radio. Focus = green ring (`box-shadow 0 0 0 3px rgba(76,122,18,.18)` + `--color-primary-ink` border). Error = `--color-err` border + ring + a message that **explains the rule** ("Ange högst 79 gäster…"), not just "ogiltigt". Disabled = tinted, `not-allowed`.
  - **Validation is server-side, styled — the single source of truth.** The Static-SSR forms post to the server, which returns our styled messages (`ValidationMessage` on the inquiry form; the `.alert--error` summary banner on the completion form). Avoid native constraints that short-circuit the post and suppress those messages: no `required` attribute and no `type="email"` — use `type="text" inputmode="email"` to keep the mobile e-mail keyboard while our `[EmailAddress]` message still renders (slice 7).
- **Cards** — image card (photo + title + body) and flat card (green top-rule, no image). Border `--color-line`, `--radius-card`.
- **Tables** — uppercase header labels, hairline row borders, right-aligned numeric columns with `font-variant-numeric: tabular-nums`; horizontal scroll wrapper on narrow screens.
- **Alerts** — `.alert` in `ok / warn / err / info`, tinted background + matching border, bold lead line. The leading `.alert__ic` glyph is a **round filled badge** (italic "i" for info), so it reads as an indicator, not stray punctuation — a bare bold "i" was mistaken for an upside-down "!" (slice 7).
- **Status pills (badges)** — `.badge` + a state modifier, a coloured dot + label mapped from `BookingDisplay.StatusBadgeClass`: Väntar (`--pending`/warn) · Godkänd (`--approved`/ok) · Uppgifter klara (`--complete`/info) · På plats (`--active`) · Avslutad & Besvarad (`--done`/`--neutral`) · Avslagen & Avbokad (`--denied`/`--cancelled`/err). **Primarily admin**, but the token set is shared.
  - **"AA on white" is not the test, and saying so here is what hid a failure for months.** This line used to claim "all combinations AA on white". Every badge tint is **translucent**, and these badges live in `.admin-table` rows that turn `--color-bg-warm` on hover — so the true ground is the badge tint composited over a row that itself changes. Measured that way, `--neutral` was **3.96:1** on a hovered row while reading as a comfortable pass against white. Current worst-case, hovered: `--active` 7.39 · `--denied` 6.24 · `--complete` 5.78 · `--pending` 5.56 · `--neutral` 5.00 · `--approved` 4.91. **Re-measure against the hovered row, not white, whenever a badge or a grey changes.**
- **Modal** — dark scrim, `--radius-modal`, footer with primary + cancel; destructive confirmations use `--color-err` as the fill (white text, 6.3:1). Used sparingly, mostly in admin.

---

## The public shell

- **Header** — solid sticky bar on `--color-bg`, hairline bottom border. Left: logo lockup (dark strokes). Right (desktop): quiet inline nav + outlined **Skicka förfrågan** CTA. Mobile: **CSS-only hamburger** opening a dropdown panel (no JS). Height `--header-h` (72px). **Current-page state** is a thin green underline on desktop and a **green inset bar + faint tint** in the mobile panel (mirroring the admin sidebar) — colour alone is not a sufficient state indicator (WCAG 1.4.1), and the desktop underline lives inside the 1024px query so mobile needs its own cue.
- **Docked mobile CTA** (`.mobile-cta`) — a full-width **Skicka förfrågan** bar fixed to the bottom edge below `640px`. On a phone the header CTA is inside the collapsed hamburger panel and the hero CTA measures below the fold, so the first screen otherwise offers no route into the funnel at all — with the hero reading as a finished screen, so nothing prompts a scroll. Suppressed on `/boka`, `/kalender` (which docks its own selection bar and would collide), `/slutfor/*` (an approved booking, not a new inquiry) and the error page. Respects `env(safe-area-inset-bottom)`; the footer reserves matching bottom padding so the bar never covers it.
- **Footer** — dark (`--color-footer-bg`), four-column grid: brand + contact · Utforska · Boka · Om sidan (incl. **Integritetspolicy**, required by NFR-20). Bottom bar with copyright + "Retreat & konferens sedan 1978". Collapses to one/two columns on mobile.

---

## Logo Variants

The mark is the client's **church-in-a-hand** identity, delivered 2026-08 and redrawn as SVG. It replaced the abstract Å-figure that shipped in Phase 6. SVG assets live in `wwwroot/logo/`; `Logo.razor` takes a `Variant` parameter (`OnDark | OnLight | Stacked`).

> **The lockups are generated, and the wordmarks inside them are baked outlines — roughly 19 KB of bezier path data each.** Colours, spacing and the mark itself can be edited by hand; **the words cannot**. Changing so much as one character of *Åkerögården* or *Kaldéiska Kursgård* means regenerating, not editing.
>
> The generator was deleted once the logo settled, rather than keeping a Python script (`pip install fonttools brotli`) in a .NET repo that nothing builds or runs. It is in git history and comes back in one command:
>
> ```
> git show c44f392:docs/ux/logo-generator.py > docs/ux/logo-generator.py
> ```
>
> Everything it encodes — the 52-unit box, the 4-unit edges, the roof band, the hand's web notch, and the measurement behind each — is written out below, so the script is a convenience, not the source of truth. **Recover it before touching the text; do not hand-edit the paths and do not redraw the mark from scratch.**

The client's artwork is a single **square, stacked** composition. That cannot go into a 72px header — at `height: 55px` the wordmark renders about 3px tall and dies. So one mark ships as **three lockups**; this is completing the identity, not deviating from it.

| File | Composition | Used by |
|---|---|---|
| `lockup-transparent.svg` | Horizontal, 192×52 — mark left, two-line wordmark right | Public header (`OnLight`) |
| `lockup-dark.svg` | Same, re-toned for a dark ground | Admin sidebar (`OnDark`) |
| `lockup-stacked.svg` | Centred, 250×156 — the client's own composition | Login / glömt- / återställ-lösenord (`Stacked`) |
| `mark-light.svg` / `mark-dark.svg` | Icon only | Tight spots |
| `mark-favicon.svg` → `favicon.png` | Icon, simplified | Browser tab |

**Both wordmarks are baked to outlines, not `<text>`.** The lockups load through `<img>`, which renders SVG sandboxed — no page CSS, no webfonts, no external anything. A `font-family` in there resolves against whatever the viewer's OS happens to have, so the pre-2026-08 lockup asked for Cormorant Garamond and silently got Georgia on macOS and something generic on Linux. Outlines render identically everywhere and add no font dependency. **Do not "fix" this back to a `<text>` element.**

**Colours.** The mark's gold is `--color-secondary` `#865628`; the client's own ochre was an off-palette `#b5822a`. The teal `#3a9698` is the client's and is kept as the mark's signature — a logo is exempt from contrast minimums (WCAG 1.4.3 and 1.4.11 both carve out logotypes explicitly). The **tagline does not use it**: see the teal rule under Colour. On the `#26221d` sidebar the gold falls to ~3.5:1 and reads as mud, so `lockup-dark` re-tones rather than recolours — warm off-white building, lifted teal `#5cb9bb`.

**The 52-unit box is load-bearing.** The horizontal lockup renders at a CSS `height: 55px`, so every unit of padding inside the viewBox shrinks the ink. At the old 80-unit box the tagline came out at 4.5px — invisible. At 52 it lands at 6.9px and the mark *grows* from 36px to 42px in the same header. Horizontal padding is trimmed just as hard (4 units of edge): measured at 1024px, where the public header swaps its hamburger for the inline nav, the row has only ~66px to spare, and an earlier, roomier lockup overflowed both the 236px admin sidebar and the 320px admin top bar. **Grow either dimension and you take it out of the navigation.**

> `.login-card__logo .logo` carries its own `height: 104px` rather than the 55px every other surface uses, because it is the only place rendering the *stacked* lockup — a different aspect entirely. Height is the wrong dimension to share between two lockups of different shape.
>
> The footer uses a Playfair text wordmark rather than the SVG, so it does not carry the operator name. `lockup-light.svg` (which baked in a white `<rect>`) was retired in the 2026-08 rework — it had no code path, and the transparent variant is what the header always used.

---

## Admin (productivity design)

The admin surface (redesigned in slice 6) **reuses these tokens** for coherence but is deliberately utilitarian: dense tables, tight forms, clear status pills, keyboard-friendly — **no hero treatment, no persuasion aesthetic**. "Shared skin, different discipline."

- **Shell** (`AdminLayout`) — a fixed 236px **dark sidebar** (`--admin-nav-bg` #26221d) with the `OnDark` logo lockup, **grouped nav** (main · Referensdata · System) with per-page **active state** (green inset bar + tint, `aria-current="page"`), a live **count badge** on *Bokningar* (pending inquiries), and the signed-in email + logout pinned to the bottom. The main column has a **sticky topbar** (page title / breadcrumb + a right-aligned primary action) fed per-page via a Blazor `SectionOutlet`; pages inject it through the `<AdminHeader>` component. The breadcrumb link (`.crumb a`) is **underlined** — it sits inline in crumb text ("Bokningar / …"), so colour alone isn't a sufficient distinction (axe "link-in-text-block", caught in slice 7). **Under 860px the sidebar collapses behind a CSS-only hamburger** (`.admin-nav__checkbox` + `.admin-nav__toggle` label — the layout is static SSR, so a checkbox toggle is used, not Blazor `@onclick`; the `#admin-nav-links` container is `data-enhance-nav="false"` so tapping a link does a full navigation that resets the checkbox → the menu closes on navigate). The booking-detail collapses to one column. *(Phase 8, NFR-17: admin usable on a phone.)*
- **Dashboard** (`/`) — an **operational** landing, not a welcome: three flat cards — *Väntar på svar* (pending queue) · *Väntar på slutförande* (approved-not-completed, with link-expiry urgency) · *Kommande ankomster* (nearest arrivals). Every row links straight into the booking. The two top cards sit in `.dash-grid` — **one column on mobile, two from 720px up** (Phase 8; the earlier hard-coded two-column inline grid squeezed them on a phone).
- **Admin data table** (`.admin-table` in `.admin-table-wrap`) — denser than the public `.data-table`: uppercase hairline header, hover row tint, `.rowlink` name + `.sub` contact line, right-aligned tabular numbers, status **badge** column, right-aligned `.rowactions`.
- **Booking detail** — two columns: main content (contact / stay / participants / message) + a **sticky action rail** (`.detail-rail`) of `.card`s holding status actions (`.action-panel` for deny/cancel reason inputs), building assignment, list downloads, and internal notes. `.kv` definition grid for read-only facts. *(The payment checkboxes and their `.paycheck` class were removed in the 2026-08 demo-feedback work — FR-42 retired; building assignment (FR-103) takes their place in the rail, since it must be set before the approve action directly above it.)*
- **Reference-CRUD pattern** — list in an `.admin-table` (active/inactive as a badge) above an **add/edit panel** wrapped in `.card.admin-editcard`. Consistent across Byggnader/rum, Matalternativ, Faciliteter, Priser, Städzoner, Konton, Blockeringar.
- **Auth pages** — centred `.login-card` on the `--color-bg-warm` tint with the **`Stacked`** logo (the only surface that uses it — the card has the vertical room, and it is the one place the logo is the focal point rather than a corner mark); used by login / glömt-lösenord / återställ-lösenord.
- **Utility pages** (`.utility` inside `.section > .container`, on `AdminLoginLayout` so the nav is absent) — a large code, a heading, one explanatory line, one action button. Used by **404** (`/not-found`) and **403** (`/ingen-behorighet`). The 403's action link is **role-resolved, not hardcoded to `/`**: for `Samordnare` the dashboard is itself forbidden, so a fixed "Till översikten" button would bounce the user straight back to the page they are standing on.
- **The `Samordnare` surface** (FR-104) — the restricted role reuses the same shell and table styling as everything else; the whole distinction is **which pages exist for it**, not a different look.
  - **Nav** — the sidebar renders a single group with one entry (*Deltagar- och matlistor*). The groups *Referensdata* and *System*, and the pending-count badge, are absent. This is navigation only: every page carries its own role attribute, so a stray link would be a dead end, never a leak. The label is spelled out rather than shortened to *Listor*: it stands alone with no surrounding context, "listor" on its own could mean anything, and the people reading it are external and log in rarely. The two-line wrap costs nothing when the role has one nav entry. (The `<h2>Listor</h2>` card headings **inside** a booking keep the short form — there the context is unambiguous and the two items are labelled directly beneath.)
  - **Deltagar- och matlistor** (`/listor`) — an `.admin-table` of bookings that have downloadable lists, three columns: *Referens · Datum · Gäster*. Deliberately **no Sällskap column and no contact line** — the `.rowlink` + `.sub` pattern used on Bokningar carries the contact person's name and email, which this role may not see. Row links to the download page. Mirrored into `.data-cards` for mobile, like every other admin list.
  - **Deltagar- och matlistor / {referens}** (`/listor/{id}`) — the same `.crumb` breadcrumb + `.admin-detail-head` (h1 + `.chip`s) shape as the booking detail, then a single `.card` with the four download links in a `.kv` grid. Read-only: no action rail, no notes, no status actions. The not-found state renders the crumb plus one line and no `h1`, matching `BookingDetail`'s equivalent state. The route stays `/listor` — short, no guest data, and the same page/route split as `/konton` under "Administratörskonton".
  - Both are **Static SSR** — they list and link, so they need no circuit.
- **Sidebar DB access caveat** — the layout loads its badge count through a **dedicated DI scope** (its own `DbContext`), because the layout and the page both resolve the circuit-scoped `BookingService`; overlapping their `OnInitializedAsync` DB calls on one `DbContext` otherwise throws "a second operation was started…".
