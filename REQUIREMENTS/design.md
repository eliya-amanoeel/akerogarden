# Åkerögården — Design System

Visual design reference for the public-facing website: tokens, layout rules, key CSS classes, and logo usage. Split out of README.md so design/UI details don't mix with architecture and operations documentation.

---

## Architecture

All styles live in `src/app/(frontend)/styles.css`. No CSS framework, no component library.

Mobile-first with two breakpoints: `480px` (tablet) and `1024px` (desktop).

---

## Design Tokens

```css
/* Colours */
--color-primary:        #71b002    /* brand green — buttons, accents */
--color-primary-hover:  #5e9600
--color-secondary:      #865628    /* warm brown */
--color-text:           #4a4845    /* body copy */
--color-heading:        #1e1c1a    /* headings */
--color-bg:             #faf8f4    /* page background (warm off-white) */
--color-bg-warm:        #f0ebe1    /* section background tint */
--color-footer-bg:      #1a1714    /* dark footer */

/* Typography */
--font-body:    var(--font-roboto, 'Roboto'), sans-serif
--font-heading: var(--font-playfair, 'Playfair Display'), Georgia, serif

/* Layout */
--max-width:  1200px
--header-h:   72px
```

---

## Key CSS Classes

| Class | Purpose |
|---|---|
| `.container` | Max-width wrapper with responsive padding |
| `.section` | Vertical section spacing (48px → 72px) |
| `.btn` | Primary button (brand green) |
| `.btn-outline` | Outlined button variant |
| `.btn-ghost` | Ghost/text button variant |
| `.hero` | Full-height hero with background image and overlay |
| `.form-row` | Two-column form layout, collapses on mobile |
| `.form-group` | Label + input pair |
| `.site-header` | Fixed header — transparent over hero, solid on scroll |
| `.site-footer` | Footer grid |

---

## Logo Variants

SVG assets live in `public/logo/`. The `Logo.tsx` component takes a `variant` prop (`'on-dark' | 'on-light'`); `Header.tsx` switches between variants based on scroll position (transparent over the hero vs. solid on scroll).

| File | Description | Used when |
|---|---|---|
| `lockup-dark.svg` | White strokes | Transparent/hero header |
| `lockup-light.svg` | Dark strokes | Scrolled header |
| `lockup-transparent.svg` | Dark strokes, transparent background | — |
| `mark-dark.svg` | Icon only, white strokes | — |
| `mark-light.svg` | Icon only, dark strokes | — |

---

## Open Visual/UX Items

Carried over from README.md's pre-launch checklist and known issues — these are tracked operationally there, but are design-related in nature:
