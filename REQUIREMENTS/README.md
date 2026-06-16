# Åkerögården

Website and booking system for Åkerögården — a rural retreat property in Sweden's Stockholm archipelago, operating since 1978. Built as a full-stack Next.js application with an embedded CMS, a guest booking form, and an admin approval workflow.

| | |
|---|---|
| **Production** | akerogarden.se |
| **Hosting** | Self-hosted Windows home server |
| **Admin panel** | `/admin` |

---

## Table of Contents

1. [Tech Stack](#tech-stack)
2. [Getting Started](#getting-started)
3. [Project Structure](#project-structure)
4. [How the Booking System Works](#how-the-booking-system-works)
5. [CMS Data Model](#cms-data-model)
6. [Design System](#design-system)
7. [Deployment](#deployment)
8. [Pre-Launch Checklist](#pre-launch-checklist)
9. [Known Issues](#known-issues)

---

## Tech Stack

| Layer | Technology | Notes |
|---|---|---|
| Framework | Next.js 16.2.6 | App Router |
| CMS | Payload CMS 3.85.0 | Embedded — runs at `/admin` inside the Next.js app |
| Database | PostgreSQL 17 | |
| Language | TypeScript 5 | Strict mode |
| UI | React 19 | Server components by default |
| Styling | Vanilla CSS | Mobile-first, single stylesheet |
| Fonts | Playfair Display, Roboto | Google Fonts via `next/font` |
| Email | Nodemailer | One.com SMTP (`send.one.com:465`) |
| Images | Sharp | Next.js image optimisation |
| Hosting | Windows home server | pm2 manages the Node process |
| CI/CD | GitHub Actions | Self-hosted runner on the Windows server |
| Package manager | pnpm | v9+ |

---

## Getting Started

### Prerequisites

- Node.js ≥ 20.9.0 (CI uses Node 24)
- PostgreSQL 17 running locally
  - macOS: `brew services start postgresql@17`
  - Create the database: `createdb akerogarden`
- pnpm: `npm install -g pnpm`

### Setup

```bash
pnpm install
cp .env.example .env      # fill in the values (see below)
pnpm dev                  # → http://localhost:3000
```

On first run, visit `http://localhost:3000/admin` to create the initial admin user — Payload walks you through it.

After any change to a collection schema, regenerate TypeScript types:

```bash
pnpm generate:types
```

### Environment Variables

Copy `.env.example` to `.env` and fill in the blanks:

```
DATABASE_URL=postgres://user@127.0.0.1:5432/akerogarden
PAYLOAD_SECRET=<any long random string — used for session encryption>

SMTP_HOST=send.one.com
SMTP_PORT=465
SMTP_USER=info@akerogarden.se
SMTP_PASS=<One.com email password>
EMAIL_FROM=info@akerogarden.se
ADMIN_EMAIL=info@akerogarden.se

NEXT_PUBLIC_SITE_URL=http://localhost:3000
```

`PAYLOAD_SECRET`, `DATABASE_URL`, and `NEXT_PUBLIC_SITE_URL` are validated at startup — the app refuses to start if any are missing.

Email (`SMTP_USER` + `SMTP_PASS`) is only active in `NODE_ENV=production`. In development, emails are silently skipped.

---

## Project Structure

```
src/
  app/
    (frontend)/               Public-facing website
      layout.tsx              Root layout: Header + Footer + Google Fonts
      styles.css              All CSS — design tokens, components, pages
      page.tsx                Homepage (hero + facilities teaser)
      about/page.tsx          Om oss — story, heritage, location
      facilities/page.tsx     Faciliteter — amenities grid
      booking/page.tsx        Bokningsförfrågan — booking request form
      contact/page.tsx        Kontakt — address and transit info
      [slug]/page.tsx         Dynamic CMS pages (Pages collection)

    (payload)/                Payload CMS routes
      admin/                  Admin UI at /admin
      api/                    Payload REST + GraphQL endpoints

  collections/
    Bookings.ts               Guest requests, inquiries, blocked periods + email hooks
    Facilities.ts             Amenities listed on the Faciliteter page
    Media.ts                  Image uploads
    Pages.ts                  CMS-managed content pages
    Users.ts                  Admin users (auth)

  globals/
    SiteSettings.ts           Singleton: hero content, contact info, social links

  components/
    Header.tsx                Fixed header — scroll-aware, logo variant switching
    Footer.tsx                Site footer — contact info, nav, social links
    Logo.tsx                  SVG logo — variant prop: 'on-dark' | 'on-light'
    BookingForm.tsx           Public booking form ('use client')
    FacilitiesGrid.tsx        Facilities display grid
    BookingCalendar.tsx       Calendar view of bookings (admin)
    BookingsListToggle.tsx    List/calendar toggle for the admin bookings view

  lib/
    email-templates.ts        HTML email templates (admin notification, guest approval/denial)
    facilities.ts             Shared facility options list (used by form + collection)

  payload.config.ts           Payload CMS config — DB, email, collections, globals
  payload-types.ts            Auto-generated TypeScript types — do not edit by hand
  middleware.ts               Rate limiting: 5 POST requests per IP per 10 minutes

public/
  logo/                       SVG logo variants
    lockup-dark.svg           White strokes — transparent/hero header
    lockup-light.svg          Dark strokes — scrolled header
    lockup-transparent.svg    Dark strokes on transparent background
    mark-dark.svg             Icon only, white strokes
    mark-light.svg            Icon only, dark strokes

old_source/                   Archived WordPress source — reference only, not part of the build
```

---

## How the Booking System Works

Manual-approval workflow — no payment or automatic confirmation. Every request is reviewed by the admin.

**Step 1 — Guest submits the form**
The public booking page (`/booking`) presents a two-track form. The guest first selects either a booking request (with dates, guest count, and optional org fields) or a general inquiry (message only), then fills in their details. The form `POST`s to `/api/bookings`.

**Step 2 — Rate limiting**
`middleware.ts` allows at most 5 POST requests per IP per 10 minutes. Excess requests return `429`.

**Step 3 — Server-side validation**
A `beforeValidate` hook in `Bookings.ts` enforces:
- Name and email present (all non-blocked-period types)
- Check-in and check-out dates present, check-out after check-in (stay requests)
- At least 1 adult (stay requests)
- Message ≤ 5000 characters

**Step 4 — Record created**
A new Booking is saved to PostgreSQL with `status: 'pending'`.

**Step 5 — Admin notification email**
An `afterChange` hook sends an HTML email to `ADMIN_EMAIL` with the guest's details and a direct link to the record in the admin panel.

**Step 6 — Admin reviews in `/admin`**
The admin reads the request, optionally adds internal `adminNotes`, and sets `status` to `approved` or `denied`. The calendar view (`/admin/bookings-calendar`) gives a monthly overview.

**Step 7 — Guest notification email**
When the status changes, the same hook sends the guest an approval or denial email. Email failures are logged and do not crash the save.

**Step 8 — Versioning**
Every change is stored as a version record (up to 20 per booking), providing a full audit trail.

**Booking types:**

| Type | Description |
|---|---|
| `stay_request` | Specific check-in/check-out dates, guest count, optional org fields |
| `general_inquiry` | No dates — just a message and contact details |
| `group_request` | Admin-created group bookings |
| `blocked_period` | Admin-only — marks dates as unavailable on the calendar |

---

## CMS Data Model

All data is stored in PostgreSQL and managed through Payload CMS at `/admin`.

### Collections

#### Bookings

Stores all guest requests, inquiries, and blocked periods in one place, giving the calendar a single source of truth for availability.

| Field | Type | Notes |
|---|---|---|
| `bookingType` | select | `stay_request`, `general_inquiry`, `group_request`, `blocked_period` |
| `guestName` | text | Required for all non-blocked-period types |
| `guestEmail` | email | Required for all non-blocked-period types |
| `guestPhone` | text | Optional |
| `checkIn` | date | Required for stay requests and blocked periods |
| `checkOut` | date | Required for stay requests and blocked periods; must be after `checkIn` |
| `adultsCount` | number | Required for stay requests (min 1) |
| `childrenCount` | number | Optional |
| `isOrganisation` | checkbox | Reveals org fields when checked |
| `orgName` | text | Organisation/association name |
| `orgNumber` | text | Swedish organisation number |
| `invoiceAddress` | textarea | Invoice address for organisations |
| `requestedFacilities` | select (multi) | Boende, Kapell, Konferensrum, etc. |
| `cleaningPreferences` | textarea | Per-building cleaning notes |
| `reason` | text | Blocked period reason — not shown to guests |
| `facilities` | relationship | Admin link to Facilities collection |
| `message` | textarea | Free-text message from the guest (max 5000 chars) |
| `status` | select | `pending`, `approved`, `denied` |
| `adminNotes` | textarea | Internal only — not visible to guests |

Access: public create, admin-only read/update/delete. Versioning enabled (20 versions per record).

#### Facilities

Amenities listed on the Faciliteter page.

| Field | Type | Notes |
|---|---|---|
| `title` | text | Required |
| `description` | textarea | |
| `image` | upload | Required |
| `order` | number | Lower = displayed first |

Access: public read, admin write.

#### Pages

CMS-managed content pages served at `/[slug]`.

| Field | Type | Notes |
|---|---|---|
| `title` | text | Required |
| `slug` | text | Unique URL path, e.g. `privacy-policy` → `/privacy-policy` |
| `heroImage` | upload | |
| `content` | richText (Lexical) | |
| `metaDescription` | textarea | Used for SEO and social sharing |

Access: public read, admin write.

#### Media

Image and file uploads. Requires `alt` text. Processed through Sharp.

Access: public read, admin upload.

#### Users

Admin panel users. Email + password authentication. No public access.

### Globals

#### SiteSettings

A singleton edited directly in admin (no list view). Organised into tabs:

| Tab | Fields |
|---|---|
| Homepage | `heroTitle`, `heroSubtitle`, `heroImage` |
| Om oss | `aboutImage`, `aboutHeroSubtitle`, `aboutStoryTitle`, `aboutStoryText` |
| Contact | `contactEmail`, `contactPhone`, `address` |
| Social | `facebookUrl`, `instagramUrl` |

Access: public read, admin write.

---

## Design System

All styles live in `src/app/(frontend)/styles.css`. No CSS framework, no component library.

### Architecture

Mobile-first with two breakpoints: `480px` (tablet) and `1024px` (desktop).

### Design Tokens

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

### Key CSS Classes

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

## Deployment

The production site is deployed manually via GitHub Actions.

### CI/CD Pipeline (`.github/workflows/ci.yml`)

Triggered from GitHub → Actions → **Build & Deploy** → **Run workflow**. Two jobs run in sequence:

1. **Lint** — runs ESLint on Ubuntu
2. **Deploy** — runs on the self-hosted Windows runner:
   1. Stop the app (`pm2 stop akerogarden`)
   2. Copy source files to `C:\inetpub\wwwroot\akerogarden\`
   3. Install dependencies (`pnpm install --frozen-lockfile`)
   4. Run database migrations (`pnpm payload migrate`)
   5. Build (`pnpm build`)
   6. Start the app (`pm2 start ecosystem.config.cjs --env production`)
   7. Health check — `http://localhost:3000` must return HTTP 200

### IIS One-Time Configuration

The IIS reverse proxy adds an `X-Forwarded-Host: localhost:3000` header which Next.js Server Actions reject. Fix — run once as Administrator:

```cmd
%windir%\system32\inetsrv\appcmd.exe set config -section:system.webServer/rewrite/allowedServerVariables /+"[name='HTTP_X_FORWARDED_HOST']" /commit:apphost
```

This allows the `web.config` rule to rewrite `X-Forwarded-Host` to the real host, making Server Actions work through the proxy.

### Self-Hosted Runner Setup (once)

1. GitHub repo → **Settings** → **Actions** → **Runners** → **New self-hosted runner**
2. Select **Windows** and follow the setup instructions
3. Install as a Windows service:

```cmd
cd C:\actions-runner
.\svc.sh install
.\svc.sh start
```

### pm2 Setup (once)

```powershell
npm install -g pm2 pm2-windows-startup
pm2-startup install

# After the first deploy completes:
pm2 save
```

Useful commands:

```powershell
pm2 status
pm2 logs akerogarden
```

---

## Pre-Launch Checklist

### Critical

- [ ] **PostgreSQL backups** — no backups exist; a drive failure loses all data
  - Set up daily `pg_dump` via Windows Task Scheduler
  - Store backups offsite (S3-compatible bucket or remote server)
  - Test the restore procedure before launch
- [ ] **SSL/TLS** — the site must serve over HTTPS
  - Install nginx as a reverse proxy
  - Obtain a Let's Encrypt certificate (Certbot)
  - Redirect HTTP → HTTPS
- [ ] **Cleaning** — should be choices as checkboxes per facility.
- [ ] **Playroom facility** — bilder kommer senare.
- [ ] **We have received your email** — 
- [ ] **Approve email with rules** — 
- [ ] **Deny email with reason** — 
- [ ] **Public Calender** —
- [ ] **Check in** — 14:00. (tidigare mot tillägg)
- [ ] **Check out** — 12:00. (senare mot tillägg)

### Before Go-Live

- [x] **GDPR / Dataskyddslagen compliance**
  - Integritetspolicy page at `/privacy-policy`, linked from footer and as a modal in the booking form ✅
  - Acknowledgment line on booking form (*"Genom att skicka godkänner du vår integritetspolicy."*) ✅
  - Data retention: 2 years for completed stays, 12 months for denied/pending ✅
  - Rights contact (`info@akerogarden.se`) stated in policy ✅
  - IMY (Integritetsskyddsmyndigheten) referenced in policy ✅
  - Personnummer deliberately not collected ✅
- [ ] **Review privacy policy content** — verify data controller details, retention periods, rights contact, and IMY reference before launch. Consider a final read-through by someone familiar with Swedish GDPR.
- [ ] **Rename privacy policy slug in admin** — go to `/admin` → Pages → change the `integritypolicy` slug to `privacy-policy` (code references are already updated)
- [ ] **Upload hero image** — `/admin` → Site Settings → Homepage. The hero currently shows a dark fallback.
- [ ] **Verify hero text contrast** — check headline and subtitle readability once the hero image is in place
- [ ] **DNS cutover** — point `akerogarden.se` to the home server IP at One.com
- [ ] **Firewall** — open ports 80 and 443 only; block direct access to PostgreSQL (5432)
- [ ] **Test email delivery** — submit a test booking in production and confirm both the admin notification and guest emails arrive
- [ ] **Verify GitHub Actions runner** is installed and running on the Windows server

### Post-Launch (Deferred)

- [ ] **SMTP secrets** — add `SMTP_USER` and `SMTP_PASS` under GitHub → Settings → Secrets → Actions. Without them, emails are logged to console instead of sent.
- [ ] **Swedish admin UI** — add `sv` to Payload's `supportedLanguages` via `@payloadcms/translations`. Swedish is available in the package but Turbopack cannot resolve the `@payloadcms/translations/all` subpath at dev-server time. Options: wait for a Turbopack fix, import directly from the dist path, or restrict to production builds only.
- [ ] **ISR on-demand revalidation** — pages currently use `revalidate = 3600` (1-hour cache). The next step is triggering `revalidatePath()` from a Payload `afterChange` hook so pages update immediately when content changes in admin.
- [ ] **Automatic booking record deletion** — scheduled job to delete/anonymise records older than the retention periods stated in the privacy policy (2 years for completed stays, 12 months for denied/pending).
- [ ] **Dedicated GDPR contact address** — create `dataskydd@akerogarden.se` and update the privacy policy and booking form to use it instead of `info@akerogarden.se`.
- [ ] **Image migration** — import photos from `old_source/wp-content/uploads/` via `/admin` → Media
- [ ] **Content review** — review and update Om oss and other CMS pages via `/admin`

---

## Known Issues

| Issue | Severity | Notes |
|---|---|---|
| No database backups | Critical | See pre-launch checklist |
| Hero image not uploaded | Medium | Dark fallback shows until image is added via CMS |
| Hero text contrast | Medium | Needs verification once the final hero image is in place |
| In-memory rate limiter | Low | Safe for single-process deployment (see `ecosystem.config.cjs`). If ever scaled to multiple instances, replace with a Redis-backed limiter (e.g. `rate-limiter-flexible`). |
| Hardcoded email footer | Low | `info@akerogarden.se` in `baseLayout` in `email-templates.ts` is display-only but requires a code change if the address ever changes. |
| No test suite | Low | Recommended starting point: a Vitest smoke test for `POST /api/bookings` covering valid and invalid payloads to pin server-side validation behaviour. |
