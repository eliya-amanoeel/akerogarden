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

**The shapes the client's photos have to fit** — because a layout that decides this implicitly decides it badly. `/kontakt`'s photo is **4:5 portrait** on desktop (`aspect-ratio`, top-aligned) and a wide band below 1024px, so its subject wants to sit centred and survive both crops. **The client approved this crop on 2026-08-19**, so it is an agreed constraint on the launch photography rather than something the layout is imposing on them. It previously took its height from whatever stood in the neighbouring column, which meant the find-here map landing there in 2026-08 quietly turned the requirement into a ~1:2.2 portrait — a shape almost no photograph of a rural property has. **If a photo frame's height comes from a sibling rather than from a ratio, that is a bug: it hands the client a crop nobody chose.**

---

## Architecture

.NET 10 Blazor — **Static SSR** for all public pages, Interactive Server for admin. Styles live in `wwwroot/css/site.css`; no CSS framework, no component library — plain hand-written CSS.

**Mobile-first**, breakpoints at `480px` (tablet) and `1024px` (desktop). One documented exception: the `/kontakt` find-here map switches label sets at `530px`, because that is where the *drawing* runs out of room rather than where the page reflows — see the Find-here map component. A few components add their own local breakpoint where the content demands it (e.g. `640px` for the calendar's docked selection bar, `720px` for the two-column month grid, `768px` for the three-up card rows) — these are component-level, not new global tiers.

**Mobile fold discipline.** The hero image yields height to the call to action, not the other way round: the booking funnel is the primary KPI, so `Skicka förfrågan` must be reachable without scrolling on a small phone. Measured at 375px the CTA sat at 640px with a 15rem hero image — below the fold once browser chrome is counted — so the mobile image is capped at `11rem` and grows back from 480px up. Re-measure before increasing it.

Public pages are Static SSR (no SignalR circuit), so any interactivity is plain progressive-enhancement JS/CSS — **not** Blazor interactivity. The **mobile nav is a CSS-only checkbox toggle** (no JS required), so it works under Static SSR. The header is a **solid sticky bar** (see Shell) — the earlier transparent-over-hero + scroll-JS + logo-swap approach is retired in direction A.

---

## Fonts

Both typefaces are **self-hosted** — no Google Fonts CDN, no external font requests. Loading Google Fonts sends visitor IPs to Google without a legal basis (Austrian DPA ruling, Jan 2022; enforced in Germany too). Fonts were the first case of it, but **NFR-35 now covers every third-party resource** — no CDN, analytics, embedded map, video or social widget, and no third-party iframe, script, stylesheet or image. That is why no design problem on this site is ever solved by reaching for an embed, and why no consent banner is needed.

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
--color-muted:          #646159   /* hints, captions, labels */
--color-bg:             #faf8f4   /* page background (warm off-white) */
--color-bg-warm:        #f0ebe1   /* section tint */
--color-footer-bg:      #1a1714   /* dark footer */
--color-line:           rgba(30,28,26,.12)  /* hairline borders */

/* Semantic (alerts, validation, status pills) */
--color-ok:   #3f6a0f   --color-ok-bg:   rgba(113,176,2,.12)
--color-warn: #7a4c00   --color-warn-bg: rgba(214,158,46,.16)
--color-err:  #8f2915   --color-err-bg:  rgba(179,52,26,.09)
--color-info: #6f4a1f   --color-info-bg: rgba(134,86,40,.10)

/* Status families with no semantic hue of their own. Both are purpose-picked, not borrowed:
   see the badge note below for why --color-neutral cannot just be --color-muted. */
--color-neutral: #5d5b56   --color-neutral-bg: rgba(30,28,26,.07)   /* Avslutad · Besvarad */
--color-active:  #2c4708   --color-active-bg:  rgba(113,176,2,.22)  /* På plats */

/* Hairline per family, each at its own alpha — three different hues, not one hue repeated */
--color-err-line:  rgba(179,52,26,.32)
--color-warn-line: rgba(214,158,46,.4)
--color-info-line: rgba(134,86,40,.3)
```

**The green rule (accessibility-critical).** `#71b002` is a light lime — it **only passes WCAG AA as a fill with dark text** (`#71b002` + `#1e1c1a` = 6.5:1). As white-on-green it fails (2.65:1) and **must not be used that way**. As **text/links**, use the deepened `--color-primary-ink` `#3f6a0f` — it clears AA on **both** the off-white `#faf8f4` *and* the warm `#f0ebe1` section tint (an earlier `#4c7a12` passed on off-white but missed 4.5:1 on the warm tint — caught by the axe E2E test). Darkening the *fill* toward white-text-legibility instead turns it olive — rejected; keep the bright fill + dark text.

**The warm-tint rule (same trap, second occurrence).** `--color-warn` was deepened from `#8a5a00` to `#7a4c00` on 2026-08-07 for the identical reason. Because `--color-warn-bg` is **translucent**, a warn element's contrast is decided by the surface beneath it: the old pair measured 5.2:1 over white but **4.49:1** over the warm `#f0ebe1` tint — failing AA by a hundredth, which is where the `Väntar` status badge sits on `/bokningar` and `/kalender`. Caught by the axe E2E test, as the green was. `.alert--warn` had already been given `#7a4c00` as a local override, so the system was carrying the fix and the bug side by side; the token now holds it and the override is gone.

**The warm-tint rule, third occurrence — and the last one.** `--color-err` was deepened from `#b3341a` to `#8f2915` on 2026-08-10, for the third time the identical trap: measured over the warm tint it gave **4.53** — three hundredths of headroom, which is a coin on its edge, not a pass — and that is where the `Avbokad` / `Nekad` badges sit on `/bokningar`. Unlike the green and the amber, this one was **not** caught by the axe test, because 4.53 passes; it was caught by measuring all four pairs deliberately. The deepened value is not a new colour: `.alert--error` already carried it.

> **The general rule these three share (this is what NFR-38 requires in practice):** any semantic colour paired with a **translucent** `-bg` must clear 4.5:1 against **both** the off-white `#faf8f4` *and* the warm `#f0ebe1` tint, because the same component appears on both. Checking only against white is how the first two shipped, and a bare pass is how the third nearly did. **A margin under ~0.5 is a finding, not a pass** — treat it as failing and deepen. Measured after the 2026-08-10 fix: `err` ~7.3, `info` 5.79, `warn` 5.55, `ok` 4.91.
>
> **The corollary: a translucent `-bg` belongs to inline components only.** On anything
`position: fixed` the surface underneath is not a known ground — it is whatever the page happens to
be scrolled to — so the contrast cannot be reasoned about at all, and page content shows straight
through the panel. Give floating elements an opaque background: either an existing solid token, or
the intended tint **flattened** onto its base. The error toast shipped translucent on 2026-08-10 for
exactly this reason (`#ebdbcf` is its `--color-err-bg` flattened onto `--color-bg-warm`). Verified
2026-08-10: every other fixed/sticky element is opaque; the only translucent ones are
`.modal-overlay` and `.admin-scrim`, where that is the point.

**A colour chosen as a fill is not automatically usable as text — the fourth occurrence of the trap, and the only one caught before it shipped.** The 2026-08 identity arrived with a teal, `#3a9698`. Inside the mark that was fine (logotypes are exempt from 1.4.3/1.4.11). Set as **text** it measured **3.30:1** on `#faf8f4` and **2.95:1** on the warm tint. Same trap as green, warn and err above, from a new direction: not a translucent `-bg` this time, but a value picked for a *fill* and then reused for *type*. **The teal is no longer in the system** — the mark was recoloured on 2026-08-11 (see Logo Variants) — but the rule it produced stands, and it is the reason to check any brand colour before letting it carry a word.

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
| Prose sub-head (`.prose h3`) | Playfair **700** | 17px — the prose body size, separating by family and weight only |
| Eyebrow / label | Roboto **700** | 12px, `letter-spacing .18em`, uppercase, colour `--color-primary-ink` |
| Lead / ingress | Roboto 400 | ~18px |
| Body | Roboto 400 | 16px, measure ≤ ~60ch |
| Small / caption | Roboto 400 | ~13.5px, colour `--color-muted` |
| Button | Roboto 700 | ~15px |

### Print — one minimal `@media print` block

The site shipped no print styles until 2026-09-08, so printing any page carried the header, the
footer, the docked mobile CTA and the "Vill du boka?" card. The block at the foot of `site.css`
hides that chrome, overrides the colour **tokens** rather than individual rules (so every component
follows without being listed), holds atomic blocks together across page breaks — the drawn map, a
`dt`/`dd` pair, a booking summary — and sets a 15mm `@page` margin.

Two things to know before extending it:

- **Measure it, don't read it.** The rules were verified by emulating print media in Chromium and
  printing each page to PDF, before and after. Honest result: the chrome disappears and the ground
  goes white on every page, but the **page-count saving is modest** — `/` 6 → 5, `/om-oss` 3 → 2,
  `/integritetspolicy` 7 → 6, and `/kontakt`, `/bokningsvillkor` and `/mat-och-priser` unchanged.
  Anyone expecting a dramatic reduction should measure rather than assume.
- **Media emulation is asynchronous.** Switching the emulated media triggers a style recalc that is
  *not* complete when the emulation call returns, so a single immediate `getComputedStyle` read
  observes the previous media's values. Use a polling assertion (`ToHaveCSSAsync`), never a one-shot
  `EvaluateAsync` — this cost a false failure while writing `PrintStylesheetTests`, and it is the
  same class of trap as the injected-stylesheet oddity recorded under Text scaling.

This is deliberately **not** the "document treatment" of `/kontakt`, `/integritetspolicy` and
`/bokningsvillkor` — the three pages people actually print. That is worth doing only if someone asks.

### No inline `style` attributes — the CSP blocks them

Styling lives in `site.css`, never in a `style="…"` attribute on an element. This is not a taste
rule: since the Content-Security-Policy shipped (NFR-39), `style-src 'self'` admits no inline
styling, so an attribute that reaches the browser is **dropped** — the element simply renders
unstyled, with nothing in the page to say why. Nineteen such attributes were converted when the
policy landed.

Two consequences worth knowing before reaching for one:

- **Put the rule beside the component it modifies**, as a modifier (`.chip--trailing`,
  `.alert--stacked`, `.cta-note--spaced`, `.utility__actions--start`), not in a utilities block at
  the end of the file. The August 2026 design-system audit was about rules drifting away from what
  they style; a `.mt-sm` bucket is that drift by construction. Where the value is genuinely about
  the content rather than the layout, name it for the content — `.freetext` (`white-space: pre-wrap`)
  holds guest-authored text whose line breaks are meaning, not formatting.
- **Script may still write to the style object.** `el.style.display = "none"` is CSSOM, not inline
  styling, and CSP does not govern it — `inquiry-form.js` and `completion-form.js` both rely on
  this. But a class beats a cleared inline style, so the moment an element's *initial* state is
  server-rendered as a class, the script toggling it must toggle the class too. That is exactly
  what happened to the completion form's meal cells (`.meal-cell--hidden`): the markup half and the
  script half had to move together, and moving only one would have left cells stuck hidden.

A unit test (`NoInlineStyleAttributeTests`) scans every `.razor` file for the attribute, because the
end-to-end CSP tests only see the pages they visit — and an inline style is most likely to appear on
an admin detail view or an error page, which is precisely what those tests do not open.

### Text scaling — the reader's setting is an input

`html { font-size: 100% }`, never an absolute size. An absolute root overrides the reader's own
browser text-size setting with no way for them to opt out (WCAG 2.1 SC 1.4.4 "Resize text", AA —
inside NFR-38's scope). `100%` resolves to whatever they chose, which is 16px for almost everyone,
so a default visitor sees no difference. The site was pinned to `16px` until 2026-08-19.

Everything **sized by, or sized to fit, type** must then be in `rem` so it follows. Genuinely
physical values may stay in `px`: hairline borders, radii, shadow offsets, and artwork such as
`.site-header__logo .logo` (55px — an image, not text). The map labels on `/kontakt` are the
worked precedent for the first category.

Three rules came out of measuring the site at 100 / 125 / 150 % text:

- **Never pin the height of a box whose contents are type.** `.site-header .container` was
  `height: 72px`, so its own CTA button spilled out of the bar at a 24px root. It is now
  `min-height: var(--header-min-h)` — the constant is a floor, and content decides the rest.
- **A `1fr` grid track cannot shrink below its longest unbreakable word, and Swedish compounds
  are long.** "Integritetspolicy" is 100px at a default root and 150px at a 24px one;
  "Helpension" in `.trust-strip__num` reaches 171px. Two such tracks plus a gap do not fit a
  360px phone, and the page grows a horizontal scrollbar. Where a two-column block must survive
  large text, give the track an explicit minimum —
  `repeat(auto-fit, minmax(min(100%, 9rem), 1fr))` — so it drops to one column instead of
  overflowing, and pin the column count again from `480px` up so `auto-fit` cannot pack in a
  third. Applied to `.site-footer__grid`, `.facts` and `.trust-strip__grid`.
- **Draw grid separators with a `1px` gap over the container's background, not with
  `:nth-child` borders.** Border rules have to hard-code the column count, so they cannot
  survive a block collapsing to one column. The gap draws the right hairlines at one, two or
  four columns without knowing which. `.trust-strip__grid` and `.facts` both use it.

**Breakpoints: `em` when the question is "does this fit", `px` when it is not.** `em` in a media
query resolves against the browser's *initial* font size — the reader's own setting — and is
deliberately **not** affected by `html { font-size }`, which is what makes it stable. So every
breakpoint that **adds a column** is in `em`, because that content grows with the setting while a
`px` breakpoint does not; breakpoints that only change **padding** stay in `px`.

At a default 16px root each `em` value resolves to exactly the pixel width it replaced —
`40em`=640, `45em`=720, `48em`=768, `64em`=1024, `30em`=480 — so **nothing moves for a default
visitor**. At larger settings the layout steps down a column instead of overflowing.

Converted on 2026-08-19: the header's inline-nav/hamburger switch, `.hero__grid`,
`.trust-strip__grid`, `.usecards`, `.teaser`, `.steps`, `.site-footer__grid` (both the 2- and
4-column rules), `.dash-grid`, `.about-grid`, `.fac-grid`, `.lodging-list`, `.menu-cols`,
`.contact-grid`, `.cal-months`, `.form-row`, `.day__meals`, `.facts`.

Two findings worth keeping, because both cost a re-measure to discover:

- **A `px` pin can re-introduce the overflow the collapse was meant to prevent.** The `480px`
  pins added to stop `auto-fit` packing in a third column forced two columns back on at a width
  where auto-fit had correctly collapsed to one — every page ran ~97px past a 480px screen at a
  32px root until those pins became `30em` too.
- **`max-width` breakpoints follow the same rule.** Converted: the admin sidebar collapse
  (`860px` → `53.75em`), the table→cards swap (`640px` → `40em`), the Byggnader toolbar columns
  (`640px` → `40em`), `.prow__age` (`560px` → `35em`) and the calendar selection bar docking to
  the bottom (`640px` → `40em`). Two were deliberately **left in `px`**, because they are not
  "does this fit" questions: `.mobile-cta` appearing at `640px` (a *is this a phone* judgement —
  though note the docked CTA becomes more valuable at a large text size, since the header
  collapses to a hamburger, so this is worth revisiting as a product decision), and the
  `530px` find-here-map type sizes, which are art-direction values measured against the drawing's
  scale factor and should not be disturbed casually.
- **The admin sidebar width was a fixed `236px`.** It is sized to hold its own labels, so it is
  text-derived and is now `14.75rem` — the identical width at a default root. Its content track is
  `minmax(0, 1fr)` so a wide table cannot push the grid past the viewport.

**Long Swedish tokens need `overflow-wrap`, and the widest ones are not headings.** A word with
no break opportunity cannot be wrapped by any amount of track sizing, so these carry
`overflow-wrap` explicitly: headings (`h1`–`h4`), `.prose`, `.admin-topbar h1`,
`.lodging-item__tag` ("Tillgänglighetsanpassad", 23 characters — the widest single token on the
public site), and the admin card fields `.data-card__title` / `__sub` / `dt` / `dd`, which hold
email addresses and values like "Konferensbyggnad". Use `anywhere` rather than `break-word` for
anything that may be an email or a single compound; `break-word` is enough for running prose.
Pair it with `min-width: 0` when the element is a flex item, and `max-width: 100%` when it is a
pill or button, or it will still push past its parent.

**`break-word` does not shrink a grid or flex track; `anywhere` does.** This is the subtle one,
and it cost two test cycles. `overflow-wrap: break-word` breaks a word that overflows *its line
box*, but it does **not** reduce the element's min-content size — so a `1fr` track, whose auto
minimum is min-content, still grows to fit the whole unbroken word and the heading rule never gets
a chance to act. Two ways out, and single-column text tracks in this file use the first:

- give the track room to shrink — `minmax(0, 1fr)` rather than a bare `1fr`; the word then breaks
  inside the narrower line box (used on every single-column track that holds text);
- or use `overflow-wrap: anywhere`, which *does* reduce min-content (used on the pills, card
  fields and checkbox labels, where there is no track to size).

**A `clamp()` whose minimum is in `rem` collapses to that minimum at a large text size.**
`.teaser h2` is `clamp(1.75rem, 3.4vw, 2.375rem)`. At a 32px root the minimum is 56px while
`3.4vw` on a 360px phone is 12px — so the viewport-relative middle term can never win, and the
heading renders at 56px on a phone. That is not wrong in itself (the reader asked for large text),
but it means fluid type is only fluid in the range where the `vw` term is actually in play, and
every `clamp()` minimum should be read as "the size at this reader's setting", not "the small
size".

**Two measurement traps, both of which produced a false result before being caught:**

- **Measure with the HTTP cache disabled.** The `@Assets` fingerprint is stable between app
  restarts, so a browser with a persistent profile will happily serve a previous `site.css` and a
  measurement silently reports the state before your edit. A fix that "changed nothing" was
  actually working.
- **`html { scroll-behavior: smooth }` makes a programmatic scroll asynchronous.** Setting
  `scrollTop` and reading a rect on the next line reads the *pre-scroll* position, which looks
  exactly like a sticky element failing to stick. The E2E suite avoids this by running its context
  `ReducedMotion.Reduce` (which forces `scroll-behavior: auto`); ad-hoc probes must set
  `behavior: 'instant'` themselves.

**Sticky bars publish their measured height.** Anything that sticks *below* a bar needs that
bar's rendered height, and CSS gives one element no way to read another's. So
`js/sticky-offsets.js` measures each bar with a `ResizeObserver` and writes it into a custom
property; `site.css` declares a `rem` fallback for each, so a page is correct before the script
runs and if it never runs.

| Property | Bar | Consumed by |
|---|---|---|
| `--header-h` | public `.site-header` | `.cal-selection` |
| `--admin-topbar-h` | admin `.admin-topbar` | `.detail-rail` |
| `--header-min-h` | — | the header's own `min-height`; **never** script-written |

Two separate bar properties because they are two different elements on two different hosts —
they were a single `--header-h: 72px` until 2026-08-19, which had `.detail-rail` positioning
itself below the *admin* topbar using a number measured off the *public* header. And
`--header-min-h` is deliberately not `--header-h`: feeding a measured height back into the
`min-height` of the element it was measured from is a ratchet, and the bar could never shrink
again. Where the offset can be expressed structurally, prefer that over any property at all —
the mobile nav panel uses `top: 100%` against its positioned ancestor and needs no constant.

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

Each component supports the relevant states: **default / hover / focus / active / disabled / loading / empty / error**. Focus is always a visible `2px` outline in `--color-primary-ink`, offset 3px (keyboard accessibility, NFR-38).

- **Buttons** — `.btn` primary (bright-green fill + ink text; hover → `--color-primary-hover`; loading spinner; disabled 40% opacity, `not-allowed`); `.btn-outline` (ink border, inverts on hover); `.btn-ghost` (text button in `--color-primary-ink`). The hero/CTA secondary adds an underline treatment (scoped, so admin/calendar ghost buttons stay plain).
- **Links** — `--color-primary-ink`. **Inline links in running text are underlined** (WCAG 1.4.1 — colour alone isn't a sufficient distinction; axe "link-in-text-block"); nav/footer/buttons/cards are set apart by layout, not underlined. Two things have since been folded into that one shared selector rather than styled locally, for the same reason each time: **checkbox labels count as running text** (added 2026-08-17 — the allergy-consent label (FR-110) is a full sentence with the privacy-policy link inside it, and axe flagged it; the acknowledgement label on `/boka` had passed only because it is almost entirely link text with barely any prose around it — the same latent failure, one longer sentence away), and **the map caption's OpenStreetMap attribution**, a link inside a sentence. Both went into the one definition instead of onto the single element that failed, which is the point. Reset via **`:where(a)`** (zero specificity) so component classes always win — this prevents the base link colour from silently overriding `.btn` text (a real bug caught in slice 2).
- **Form controls** — label (700, ink) + input + hint/`--color-muted`; text input / textarea / select / checkbox / radio. Focus = green ring (`box-shadow 0 0 0 3px rgba(76,122,18,.18)` + `--color-primary-ink` border). Error = `--color-err` border + ring + a message that **explains the rule** ("Ange högst 79 gäster…"), not just "ogiltigt". Disabled = tinted, `not-allowed`.
  - **Validation is server-side, styled — the single source of truth.** The Static-SSR forms post to the server, which returns our styled messages (`ValidationMessage` on the inquiry form; the `.alert--error` summary banner on the completion form). Avoid native constraints that short-circuit the post and suppress those messages: no `required` attribute and no `type="email"` — use `type="text" inputmode="email"` to keep the mobile e-mail keyboard while our `[EmailAddress]` message still renders (slice 7).
- **Cards** — image card (photo + title + body) and flat card (green top-rule, no image). Border `--color-line`, `--radius-card`.
- **Tables** — uppercase header labels, hairline row borders, right-aligned numeric columns with `font-variant-numeric: tabular-nums`; horizontal scroll wrapper on narrow screens.
- **Alerts** — `.alert` in `ok / warn / err / info`, tinted background + matching border, bold lead line. The leading `.alert__ic` glyph is a **round filled badge** (italic "i" for info), so it reads as an indicator, not stray punctuation — a bare bold "i" was mistaken for an upside-down "!" (slice 7).
- **Status pills (badges)** — `.badge` + a state modifier, a coloured dot + label mapped from `BookingDisplay.StatusBadgeClass`: Väntar (`--pending`/warn) · Godkänd (`--approved`/ok) · Uppgifter klara (`--complete`/info) · På plats (`--active`) · Avslutad & Besvarad (`--done`/`--neutral`) · Avslagen & Avbokad (`--denied`/`--cancelled`/err). **Primarily admin**, but the token set is shared.
  - **"AA on white" is not the test, and saying so here is what hid a failure for months.** This line used to claim "all combinations AA on white". Every badge tint is **translucent**, and these badges live in `.admin-table` rows that turn `--color-bg-warm` on hover — so the true ground is the badge tint composited over a row that itself changes. Measured that way, `--neutral` was **3.96:1** on a hovered row while reading as a comfortable pass against white. Current worst-case, hovered: `--active` 7.39 · `--denied` 6.24 · `--complete` 5.78 · `--pending` 5.56 · `--neutral` 5.00 · `--approved` 4.91. **Re-measure against the hovered row, not white, whenever a badge or a grey changes.**
- **Modal** — dark scrim, `--radius-modal`, footer with primary + cancel; destructive confirmations use `--color-err` as the fill (white text, 6.3:1). Used sparingly, mostly in admin.
- **Legal document page** (`.legal-block`, on `/integritetspolicy` and on the booking-terms page when it lands) — numbered `h2.subhead` sections, each a block wrapper rather than one `.prose` around the whole page: the prose column is capped at 65ch for readability while the `.data-table` blocks (sub-processors, retention, cookies) run full width, so they cannot live inside it. `.prose h3` (a lighter Playfair sub-head beneath the numbered one) and `.prose ul/li` were added here — the reset strips list markers and `.prose` had styled paragraphs only, so this is the first public page with real document structure. `p.legal-note` is an aside qualifying the table above it, set apart by a hairline left rule and `--color-muted` at the house small/caption size.

  - **Why `.prose h3` is 17px and not the scale's H3.** The scale had no step below H3 (~21px), but `.subhead` — the numbered section head this sits *beneath* — is 20px, so a scale H3 here would outrank its own parent. It takes the prose body size and separates by family and weight instead. **The step is now in the scale above**, which is the point: the first version of this rule invented 17px as a loose number, along with a fourth muted-note size (0.95rem, four hundredths off the three that already existed), a list-item margin 0.05rem off `.alert__list`'s, and a block rhythm 4px off `.price-block`'s. All four were near-duplicates of house values — the drift the duplication rule exists to catch, arriving by exactly the route it describes: a new page needing something *almost* like what the system already had. Corrected 2026-08-17; if a size here needs changing again, change the scale, don't add a neighbour to it. `.legal-draft` is the `.alert--warn` banner marking text that is published but **not yet jurist-reviewed**; it is removed at sign-off, not restyled.

- **Find-here map** (`.findmap-figure` + `.findmap`, on `/kontakt`) — a hand-built map of Åkerögården's location, drawn from OpenStreetMap geometry (E 18, väg AB 1031 Åkerövägen, the coastline) and shipped as **inline SVG** wrapped in a link to the same Google Maps URL the address already uses. Attribution sits in the `figcaption`, as ODbL requires; it is text, not part of the drawing, so it can be selected and its link followed.

  - **It is an embed-shaped feature that must never become an embed.** NFR-35 forbids it outright — nothing loads from a third-party host while a page renders — and the reason is that a provider iframe pulls third-party script, tiles and cookies for *every* visitor regardless of intent. It would also make §7 of the privacy policy ("vi anlitar inga tredjepartstjänster som placerar kakor") untrue, which in turn would require a consent banner the site deliberately does not have. A link only reaches Google when the visitor clicks. `Contact_page_loads_nothing_from_a_third_party` asserts this at the network level rather than by reading the markup, so the guarantee survives whoever next edits the page.

  - **Inline, not `<img>` — the deliberate opposite of the logo rule.** The lockups are `<img>` and baked to outlines precisely *because* `<img>` renders SVG sandboxed. Here that sandbox is the thing to avoid: the map has to inherit the `--color-*` tokens and the self-hosted Roboto/Playfair from `site.css`, and its labels have to grow at the mobile breakpoint. Both rules come from the same fact about `<img>`; they point opposite ways because the two graphics want opposite things.

  - **It introduces no colour.** Land is `--color-bg-warm` and water `--color-bg` — the site's two existing grounds — which is why a map of an archipelago needs no blue. Roads are `--color-secondary`, the marker `--color-primary-ink` on a `--color-ok-bg` halo, labels `--color-muted` (5.20:1 on the warm ground) and `--color-text`. `.findmap__halo` paints a `--color-bg-warm` text outline under every label, so a label that crosses a road or the water is still measured against the ground it was checked on.

  - **A label's size is its viewBox unit times the map's scale factor — check the *narrowest* place the map is rendered, not the widest.** The labels first shipped at 11u/20u, sized against the ~592px the map gets at full container width. At the `1024px` breakpoint, where `.contact-grid` turns two-column and the map halves to 495px, that rendered them at **9.7px** — the smallest text on the site, one pixel of viewport away from 19px. The base is now `0.8125rem`/`1.4375rem`, which lands the whole desktop range at 11.5–13.7px against the house 12px label and 13.5px caption, and the place name at 20–24px against the ~21px H3. The numbers are not scale members and cannot be; they are the scale *divided by* the scale factor, which is why they look arbitrary and must not be "tidied".

  - **They are in `rem`, and stacked labels are offset by an em-based `tspan dy`, both for the same reason: the reader's text size is an input.** In absolute `px` the map was the one thing on the page that ignored a raised browser font size — and it went further and *shrank*, because `.container`'s padding is in `rem`, so the column narrows, the scale factor drops, and a fixed unit renders smaller (measured at a 24px root: caption 13.6→20.4px, labels 13.7→13.0px). Sizing in `rem` fixes the growth but not the spacing: two labels stacked at hard-coded `y` values grow into each other, which is why the second line is a `tspan` with `dy="1.5em"` — an offset that scales with the type it separates. Guarded by `Contact_map_labels_are_sized_in_rem_so_they_follow_the_reader` — which asserts the *unit*, not the behaviour, because *injecting* a raised root font size (a stylesheet, or an inline style on `<html>`) silently does not take effect against this app and the cause was never found — **there is now a mechanism that does work**: CDP `Page.setFontSizes`, which emulates the browser's own font-size setting rather than injecting CSS, and is what `TextScalingTests` uses. This guard could be upgraded to assert the behaviour — and by `Contact_map_labels_do_not_collide_at_their_largest`, which runs the overlap check at the narrow breakpoint where the type is largest relative to the drawing.

  - **In forced colors (Windows High Contrast) the map keeps its own palette, and that is now stated rather than accidental.** `fill` and `stroke` are not forced-color properties, so the drawing survives untouched while the page around it inverts — verified under CDP `forced-colors: active` emulation, not assumed. `forced-color-adjust: none` on `.findmap` records the reason it *should* survive: on a map the colours carry meaning, land is not water and a road is not a shoreline, so collapsing them to a two-colour palette would destroy the information. The same test caught a real defect beside it — `.contact-photo` had no `border`, and since its `linear-gradient` is suppressed in forced colors the placeholder became an invisible hole with its caption floating loose. **Any element whose only visible boundary is a background needs a border to survive forced colors.**

  - **Below 530px the map sheds labels rather than shrinking them — and 530 is deliberately not the house 480px breakpoint.** Font sizes are viewBox units, so they scale down with the drawing — about 10px on a narrow screen. Raising them alone makes the labels collide, so the secondary ones (`.findmap__label--aside`: the region name, the eastward direction, the road's name under its number, the scale bar) are hidden and what remains is what a guest navigates by: E 18, the direction to Norrtälje, the exit, väg 1031, and the property. The address is in the `<dl>` directly beneath either way.

    The house breakpoints mark where the **page** changes column count; this one marks where the **map** runs out of room for ten labels, which is a fact about the drawing, not the layout. Measured on the running site: the desktop sizes only reach 11px at about a 528px viewport, so between 481 and 528 the map showed all ten labels at 10.0–10.9px beside 16px body copy — the only fine print on the page, and the five extra labels it bought were exactly the supporting ones this block exists to drop. Seen side by side, the 500px rendering was worse than the 360px one despite having more room. The two numbers coinciding at 480 was a guess, not a decision. This is the one component permitted its own breakpoint, and it is permitted because the number is *derived* — if the map's geometry or its label set changes, re-measure and move it.

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

**Colours: gold `#b5822a` + green `#3f6a0f`.** The green is `--color-primary-ink` — the same green already carrying every link and eyebrow on the site — and the tagline uses it too. On the `#26221d` sidebar the gold falls to ~3.5:1 and reads as mud, so `lockup-dark` re-tones rather than recolours: warm off-white building `#f0ebe1`, lifted green `#8fbf4a` (7.31:1).

> **The client's artwork was a suggestion, not a locked identity**, so the mark is coloured for this page rather than reproduced. It arrived as gold `#b5822a` + teal `#3a9698`, and went through one wrong turn worth recording: the gold was first replaced with `--color-secondary` `#865628` "to match the palette" while the teal was kept because "a logo is exempt from the palette". Both arguments cannot hold at once, and the substitution also cost the mark the thing that made the original work — the client's two colours sat at *equal luminance* (1.03×), so building and hand read as one object with hue doing all the separating.
>
> The green replaced the teal because **teal was the only cool hue in the system**. Every other colour here sits between 29° and 88°, and the neutrals are warm-biased on purpose; `#3a9698` is at 181°. On the warm grounds that was fine, but against `--color-primary` `#71b002` (82°) it was not — two greens 99° apart is the awkward interval, too far to harmonise and too close to read as contrast, and they met in the 72px header where the logo sits beside the CTA. Since the booking funnel is the primary KPI and the header must have **one** obvious next step, a mark that visually argues with the button costs something real.
>
> Equal luminance was deliberately **not** carried over: gold→green is only 46° apart, and at that distance matching the values makes the two forms merge into one silhouette (verified by rendering a value-matched variant in greyscale). Equal value only works when hue does the separating, which is what the 143° gold→teal gap allowed. The current 1.87× split is what keeps the hand distinct at 16px.

**The 52-unit box is load-bearing.** The horizontal lockup renders at a CSS `height: 55px`, so every unit of padding inside the viewBox shrinks the ink. At the old 80-unit box the tagline came out at 4.5px — invisible. At 52 it lands at 7.9px and the mark *grows* from 36px to 42px in the same header. Horizontal padding is trimmed just as hard (4 units of edge): measured at 1024px, where the public header swaps its hamburger for the inline nav, the row has only ~66px to spare, and an earlier, roomier lockup overflowed both the 236px admin sidebar and the 320px admin top bar. **Grow either dimension and you take it out of the navigation.**

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
