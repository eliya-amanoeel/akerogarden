# Åkerögården — Design System

Visual design reference for the public-facing website: tokens, layout rules, key CSS classes, and logo usage. Split out of README.md so design/UI details don't mix with architecture and operations documentation.

---

## Architecture

The application is .NET 10 Blazor (Static SSR for all public-facing pages — see requirements.md §1.5). Styles live in `wwwroot/css/site.css`. No CSS framework, no component library — plain hand-written CSS.

Mobile-first with two breakpoints: `480px` (tablet) and `1024px` (desktop).

Public pages render as static server HTML (no SignalR circuit), so any client-side behaviour — such as the scroll-aware header below — is implemented with a small amount of plain JavaScript/CSS rather than Blazor interactivity.

---

## Fonts

Both typefaces are **self-hosted** — no Google Fonts CDN, no external font requests. Reason: loading Google Fonts sends user IPs to Google servers without a legal basis (Austrian DPA ruling, January 2022; German courts have followed). For a GDPR-conscious Swedish business this is a real liability.

Font files live in `wwwroot/fonts/`. Declare them with `@font-face` in `site.css`. No consent banner needed for self-hosted fonts.

- **Roboto** — body text. Download from [fonts.google.com/specimen/Roboto](https://fonts.google.com/specimen/Roboto) and save the `.woff2` files to `wwwroot/fonts/roboto/`.
- **Playfair Display** — headings. Download from [fonts.google.com/specimen/Playfair+Display](https://fonts.google.com/specimen/Playfair+Display) and save to `wwwroot/fonts/playfair-display/`.

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
--font-body:    'Roboto', sans-serif
--font-heading: 'Playfair Display', Georgia, serif

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

SVG assets live in `wwwroot/logo/`. A `Logo.razor` component takes a `Variant` parameter (`OnDark | OnLight`); the site header (`Header.razor`) switches between variants based on scroll position (transparent over the hero vs. solid on scroll). As noted under Architecture, the scroll detection is a small JavaScript/CSS concern, since public pages are Static SSR and have no Blazor interactivity.

| File | Description | Used when |
|---|---|---|
| `lockup-dark.svg` | White strokes | Transparent/hero header |
| `lockup-light.svg` | Dark strokes | Scrolled header |
| `lockup-transparent.svg` | Dark strokes, transparent background | — |
| `mark-dark.svg` | Icon only, white strokes | — |
| `mark-light.svg` | Icon only, dark strokes | — |

---