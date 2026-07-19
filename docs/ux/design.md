# Åkerögården — Design System

Visual + experiential design reference for the site: art direction, tokens, type scale, components (with states), the public shell, and logo usage. This is the **single source of truth for design**, evolved in Phase 6 (UI/UX). If a component in code drifts from this doc, reconcile them — don't let them diverge silently.

> **Status:** Current — direction **A — "Editorial lugn"**, built and shipped in Phase 6 (merged 2026-07-07). Phases 0–5 had shipped a deliberately functional-but-unstyled UI ("functional first, polish last"); this system replaced it across both surfaces. The component classes documented here are in the codebase — if code and doc drift, reconcile them.

---

## Art direction — "Editorial lugn"

Åkerögården is a rural retreat & conference property near Norrtälje, running since 1978, hosting a **mix** of groups (families, companies, courses, community/church) from a handful of people up to 79 overnight guests. The public site's single job is to **convert visitors into inquiries** (the booking funnel is the primary KPI); the admin panel's job is efficient daily operation.

The chosen look is **calm, timeless, editorial**: generous whitespace, large light serif headings, a warm off-white ground, and **green used sparingly** as an accent — photography carries the emotion. It reads welcoming and neutral (not "churchy", not "corporate") so any kind of group can picture themselves there.

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
--color-warn: #8a5a00   --color-warn-bg: rgba(214,158,46,.16)
--color-err:  #b3341a   --color-err-bg:  rgba(179,52,26,.09)
--color-info: #6f4a1f   --color-info-bg: rgba(134,86,40,.10)
```

**The green rule (accessibility-critical).** `#71b002` is a light lime — it **only passes WCAG AA as a fill with dark text** (`#71b002` + `#1e1c1a` = 6.5:1). As white-on-green it fails (2.65:1) and **must not be used that way**. As **text/links**, use the deepened `--color-primary-ink` `#3f6a0f` — it clears AA on **both** the off-white `#faf8f4` *and* the warm `#f0ebe1` section tint (an earlier `#4c7a12` passed on off-white but missed 4.5:1 on the warm tint — caught by the axe E2E test). Darkening the *fill* toward white-text-legibility instead turns it olive — rejected; keep the bright fill + dark text.

Verified contrast: link `#3f6a0f` 5.6:1 on `#faf8f4` / 5.1:1 on `#f0ebe1` · body `#4a4845` 11.9:1 · muted `#6f6a60` 5.0:1 · error `#b3341a` 5.7:1 · brown-on-white 6.2:1.

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
- **Status pills (badges)** — `.badge` + a state modifier, a coloured dot + label mapped from `BookingDisplay.StatusBadgeClass` (all combinations AA on white): Väntar (`--pending`/warn) · Godkänd (`--approved`/ok) · Uppgifter klara (`--complete`/info) · På plats (`--active`/green) · Avslutad & Besvarad (`--done`/neutral) · Avslagen & Avbokad (`--denied`/`--cancelled`/err). **Primarily admin**, but the token set is shared.
- **Modal** — dark scrim, `--radius-modal`, footer with primary + cancel; destructive confirmations use `--color-err` as the fill (white text, 6.3:1). Used sparingly, mostly in admin.

---

## The public shell

- **Header** — solid sticky bar on `--color-bg`, hairline bottom border. Left: logo lockup (dark strokes). Right (desktop): quiet inline nav + outlined **Skicka förfrågan** CTA. Mobile: **CSS-only hamburger** opening a dropdown panel (no JS). Height `--header-h` (72px). **Current-page state** is a thin green underline on desktop and a **green inset bar + faint tint** in the mobile panel (mirroring the admin sidebar) — colour alone is not a sufficient state indicator (WCAG 1.4.1), and the desktop underline lives inside the 1024px query so mobile needs its own cue.
- **Docked mobile CTA** (`.mobile-cta`) — a full-width **Skicka förfrågan** bar fixed to the bottom edge below `640px`. On a phone the header CTA is inside the collapsed hamburger panel and the hero CTA measures below the fold, so the first screen otherwise offers no route into the funnel at all — with the hero reading as a finished screen, so nothing prompts a scroll. Suppressed on `/boka`, `/kalender` (which docks its own selection bar and would collide), `/slutfor/*` (an approved booking, not a new inquiry) and the error page. Respects `env(safe-area-inset-bottom)`; the footer reserves matching bottom padding so the bar never covers it.
- **Footer** — dark (`--color-footer-bg`), four-column grid: brand + contact · Utforska · Boka · Om sidan (incl. **Integritetspolicy**, required by NFR-20). Bottom bar with copyright + "Retreat & konferens sedan 1978". Collapses to one/two columns on mobile.

---

## Logo Variants

SVG assets live in `wwwroot/logo/`. `Logo.razor` takes a `Variant` parameter (`OnDark | OnLight`).

| File | Description | Used when |
|---|---|---|
| `lockup-light.svg` | Dark strokes | **Header** (solid light bar) |
| `lockup-dark.svg` | White strokes | On a dark ground (e.g. dark photo overlay) if needed |
| `lockup-transparent.svg` | Dark strokes, transparent bg | — |
| `mark-dark.svg` / `mark-light.svg` | Icon only | — |

> Direction A's solid header always uses the **dark-stroke** lockup (`OnLight`), so the previous scroll-based light/dark logo swap is no longer required. The footer uses a Playfair text wordmark rather than the SVG.

---

## Admin (productivity design)

The admin surface (redesigned in slice 6) **reuses these tokens** for coherence but is deliberately utilitarian: dense tables, tight forms, clear status pills, keyboard-friendly — **no hero treatment, no persuasion aesthetic**. "Shared skin, different discipline."

- **Shell** (`AdminLayout`) — a fixed 236px **dark sidebar** (`--admin-nav-bg` #26221d) with the `OnDark` logo lockup, **grouped nav** (main · Referensdata · System) with per-page **active state** (green inset bar + tint, `aria-current="page"`), a live **count badge** on *Bokningar* (pending inquiries), and the signed-in email + logout pinned to the bottom. The main column has a **sticky topbar** (page title / breadcrumb + a right-aligned primary action) fed per-page via a Blazor `SectionOutlet`; pages inject it through the `<AdminHeader>` component. The breadcrumb link (`.crumb a`) is **underlined** — it sits inline in crumb text ("Bokningar / …"), so colour alone isn't a sufficient distinction (axe "link-in-text-block", caught in slice 7). **Under 860px the sidebar collapses behind a CSS-only hamburger** (`.admin-nav__checkbox` + `.admin-nav__toggle` label — the layout is static SSR, so a checkbox toggle is used, not Blazor `@onclick`; the `#admin-nav-links` container is `data-enhance-nav="false"` so tapping a link does a full navigation that resets the checkbox → the menu closes on navigate). The booking-detail collapses to one column. *(Phase 8, NFR-17: admin usable on a phone.)*
- **Dashboard** (`/`) — an **operational** landing, not a welcome: three flat cards — *Väntar på svar* (pending queue) · *Väntar på slutförande* (approved-not-completed, with link-expiry urgency) · *Kommande ankomster* (nearest arrivals). Every row links straight into the booking. The two top cards sit in `.dash-grid` — **one column on mobile, two from 720px up** (Phase 8; the earlier hard-coded two-column inline grid squeezed them on a phone).
- **Admin data table** (`.admin-table` in `.admin-table-wrap`) — denser than the public `.data-table`: uppercase hairline header, hover row tint, `.rowlink` name + `.sub` contact line, right-aligned tabular numbers, status **badge** column, right-aligned `.rowactions`.
- **Booking detail** — two columns: main content (contact / stay / participants / message) + a **sticky action rail** (`.detail-rail`) of `.card`s holding status actions (`.action-panel` for deny/cancel reason inputs), list downloads, payment checkboxes (`.paycheck`), and internal notes. `.kv` definition grid for read-only facts.
- **Reference-CRUD pattern** — list in an `.admin-table` (active/inactive as a badge) above an **add/edit panel** wrapped in `.card.admin-editcard`. Consistent across Byggnader/rum, Matalternativ, Faciliteter, Priser, Städzoner, Konton, Blockeringar.
- **Auth pages** — centred `.login-card` on the `--color-bg-warm` tint with the `OnLight` logo; used by login / glömt-lösenord / återställ-lösenord.
- **Sidebar DB access caveat** — the layout loads its badge count through a **dedicated DI scope** (its own `DbContext`), because the layout and the page both resolve the circuit-scoped `BookingService`; overlapping their `OnInitializedAsync` DB calls on one `DbContext` otherwise throws "a second operation was started…".
