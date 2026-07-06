# Åkerögården Booking System — Claude Instructions

## Project overview

A full-stack booking and admin system for Åkerögården, a rural retreat in Norrtälje, Sweden. Replaces a decommissioned Payload CMS. The old production database was dropped during decommission — all data must be rebuilt from scratch.

**Single source of truth:** `REQUIREMENTS/requirements.md` (v0.33). If anything here conflicts with the SRS, the SRS wins — raise it, don't silently diverge.

## Tech stack (locked — do not change without explicit discussion)

- **.NET 10 LTS Blazor** — Static SSR for all public pages, Interactive Server for the admin panel
- **EF Core + PostgreSQL** on Azure Flexible Server (VNet-only, no public internet access)
- **ASP.NET Core Identity** for admin authentication (email + password)
- **Azure App Service** Basic B1 (Linux)
- **Azure Managed Redis** — distributed rate limiting (successor to the retired Azure Cache for Redis)
- **Azure Blob Storage** — images and list downloads
- **MailKit** over One.com SMTP
- **GitHub Actions** CI/CD
- **Bicep** — Azure infrastructure-as-code. Both production and staging are provisioned from the same `.bicep` templates with different parameter files. Never click-through portal provisioning — if it's not in code, it doesn't exist.

## Architecture

- Public site: `akerogarden.se` — Static SSR Blazor pages
- Admin panel: `admin.akerogarden.se` — Interactive Server Blazor, login-gated
- Staging: `staging.akerogarden.se` — separate Azure resource group (NOT a deployment slot; B1 doesn't support slots)
- Deploy flow: dev → CI → staging → production

## Key documents

| File | Purpose |
|---|---|
| `REQUIREMENTS/requirements.md` | Main SRS — v0.33, single source of truth |
| `REQUIREMENTS/erd.md` | Mermaid entity-relationship diagram (12 app entities) |
| `REQUIREMENTS/implementation-plan.md` | 7-phase dependency-ordered build plan |
| `UX/design.md` | Visual design tokens, CSS classes, logo usage |
| `REQUIREMENTS/operational-tasks.md` | Pre-go-live blockers (privacy policy, images, VAT rates) |
| `REQUIREMENTS/architect-review.md` | Round 1 + Round 2 review findings — all resolved |

## Data model (EF Core entities)

Defined in `REQUIREMENTS/requirements.md §8` and visualised in `REQUIREMENTS/erd.md`.

Entities: `Booking`, `Building`, `Room`, `Participant`, `MealPreference`, `MealOption`, `Facility`, `PriceRule`, `BlockedPeriod`, `CleaningZone`, `BookingCleaningSelection`, `BookingFacilitySelection`.

ASP.NET Core Identity tables (`AspNetUsers`, etc.) are infrastructure — excluded from the application data model.

**Add EF Core migrations incrementally** — introduce each entity in the phase that first needs it (see implementation plan).

## Critical rules

- **Swedish only** — all public and admin UI text is Swedish from day one. No English strings, no i18n framework (NFR-32).
- **No monetary totals stored** — `AmountExVat` on `CleaningZone` and `PriceRule` is reference data only. Deposit/final payment tracking on `Booking` is boolean status. No computed totals anywhere (C8).
- **Snapshot pattern** — `MealPreference`, `BookingCleaningSelection`, `BookingFacilitySelection`, and `Participant.RoomAssignmentSnapshot` each keep a live FK **and** a snapshot of the agreed value taken at submission. The live FK powers queries; the snapshot preserves what was agreed if reference data is later edited (NFR-15/16/33/34).
- **No hard-deletes of referenced reference data** — deactivate (soft) instead; block hard-delete when a record is referenced by a booking (FR-16/20/26/30/88).
- **Booking state machine is one-way** — only the transitions defined in §2.5 are valid. Invalid transitions must be blocked in code, not just hidden in the UI.
- **No PII in URLs** — no guest names, emails, or IDs in query strings or route segments (NFR-05).
- **No personnummer** — never collect or store Swedish national ID numbers (NFR-23).
- **No personal data outside EU regions** — all Azure resources in North Europe or Sweden Central (NFR-06/24).
- **Self-hosted fonts only** — no Google Fonts CDN or any external font requests. Loading Google Fonts sends user IPs to Google without a legal basis (Austrian DPA ruling, Jan 2022; enforced in Germany too). Font files go in `wwwroot/fonts/` with `@font-face` declarations in `site.css` (NFR-35).

## Environments (§11.1)

| Environment | Where | Notes |
|---|---|---|
| Local dev | Mac + Docker | PostgreSQL, Redis, Azurite (Blob emulator), Mailpit (SMTP catcher); `dotnet user-secrets` for config |
| Automated test | Ephemeral GitHub Actions containers | Ephemeral DBs; CI fails on any test failure |
| Staging | Separate Azure resource group | `staging.akerogarden.se`; synthetic data only; test SMTP inbox; power down when idle |
| Production | Azure (North Europe or Sweden Central) | See requirements.md §11 |

## Testing

Write tests **with** each feature, not after (NFR-26–28). Three tiers:
- **Unit tests:** business logic (pricing tiers, retention dates, token expiry, meal/participant validation)
- **Integration tests:** every API endpoint, against real ephemeral containers in CI (no mocking the database)
- **End-to-end / UI tests:** Playwright for .NET (`Microsoft.Playwright`), introduced in Phase 6 (`tests/AkeroGarden.E2E`). Cover key browser journeys (esp. the booking funnel), accessibility (axe-core), and responsive reachability at 360/768/1280. **No pixel/visual-regression screenshot diffing** — flaky across macOS↔CI-Linux font rendering; assert behaviour and structure instead.

## Implementation phases

Follow `REQUIREMENTS/implementation-plan.md` in order. Do not start a phase until the prior phase's exit criteria are met.

0. Foundation — scaffold, infra, auth, design system, CI/CD, local dev
1. Reference data + admin CRUD — Building, Room, MealOption, Facility, PriceRule, CleaningZone
2. Public site — Static SSR pages, availability calendar
3. Inquiry & booking lifecycle — state machine, emails, rate limiting, blocks
4. Completion flow — magic-link guest form, snapshots, room assignment
5. Notifications, lists & retention — reminders, PDF/Excel downloads, GDPR auto-deletion
6. UI/UX — visual + experiential design pass (from scratch, both surfaces), Figma-first then Blazor (scope TBD)
7. Hardening & go-live — security pass, UAT on staging, production deploy

## Working style

- Discuss approach before implementing. Wait for explicit confirmation before making changes.
- If you spot a conflict between this file and the SRS, raise it — don't silently pick one.
- Tombstoned requirements (FR-12, FR-41, FR-43, FR-46) need no work — they exist only to keep numbering stable.
- **Challenge the user when their instinct is wrong or under-thought.** Don't be agreeable. If a decision has a real downside, say so directly and explain why before offering alternatives.
- Be extremely careful when updating any document under the REQUIREMENTS folder, all these are strongly integrated into eachother, when updating any of these files, all other files should be checked to be kept in sync with the change.

## Phase completion gate

**Before merging any phase branch to main**, run a structured evaluation pass against all deliverables for that phase. The pass must check:

1. **Phase exit criteria** (from `REQUIREMENTS/implementation-plan.md`) — list each criterion as pass / partial / fail with evidence.
2. **Critical rules** in this file — Swedish-only UI, no PII in URLs, no monetary totals stored, self-hosted fonts, no hard-deletes of referenced data, booking state machine transitions, no personnummer, EU regions only.
3. **NFR compliance** — specifically the NFRs listed in the phase's "Satisfies" row.
4. **FR compliance** — every FR listed in the phase's "Satisfies" row implemented and correctly.
5. **Security** — auth, cookie settings, antiforgery, no secrets in source, no sensitive data in URLs or logs.
6. **Tests** — unit and integration tests written with the feature (not after); CI passes.
7. **Infra** — Bicep covers every resource introduced in the phase; nothing provisioned by clicking.
8. **Design system** — Swedish text, correct CSS tokens, self-hosted fonts, no external requests.

Produce a structured report with three sections: ✅ CONFIRMED, ⚠️ GAPS / CONCERNS, ❌ VIOLATIONS. Be blunt — do not soften findings. Fix every finding before merging; do not carry gaps forward into the next phase.
