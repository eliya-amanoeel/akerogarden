# Åkerögården — Booking System

Booking and administration system for **Åkerögården**, a rural retreat in Norrtälje, Sweden. It handles
guest inquiries, the booking lifecycle, participant and meal details, and the admin workflows around
them. It replaces a decommissioned Payload CMS.

All guest- and admin-facing UI is **Swedish**. The code, comments, and documentation are English.

**🚀 Live since 2026-09-07.** `akerogarden.se` serves real guests, so anything here describes a running
production system, not a project in build. **All phases are closed** — Phase 11 (post-production
hardening and handover) closed 2026-09-09. What remains is operational, not engineering.

| | |
|---|---|
| **Public site** | `akerogarden.se` — Blazor Static SSR |
| **Admin panel** | `admin.akerogarden.se` — Blazor Interactive Server, login-gated |
| **Staging** | `staging.akerogarden.se` + `admin.staging.akerogarden.se` |
| **Source of truth** | [`docs/product/requirements.md`](docs/product/requirements.md) |

## Tech stack

.NET 10 LTS Blazor · EF Core + PostgreSQL (Azure Flexible Server, VNet-only) · ASP.NET Core Identity ·
Azure App Service (Linux B1) · Azure Managed Redis · Azure Blob Storage · MailKit over One.com SMTP ·
Bicep for infrastructure · Azure DevOps Pipelines for CI/CD.

The stack is **locked** — see [`docs/product/requirements.md`](docs/product/requirements.md) §1.5 for the
decision record behind each choice.

## Project layout

```
src/
  AkeroGarden.Web.Core      Shared Razor class library — entities, services, EF Core, components
  AkeroGarden.Web.Public    Public site. Static SSR only; loads no Identity or admin code
  AkeroGarden.Web.Admin     Admin panel. Interactive Server + Identity + the daily background job
tests/
  AkeroGarden.Unit          Business logic
  AkeroGarden.Integration   API/data layer against real ephemeral containers — the DB is not mocked
  AkeroGarden.E2E           Playwright browser journeys, incl. axe-core accessibility checks
infra/
  azure/                    Bicep templates + per-environment parameter files
  ops/                      Runbooks — DNS cutover, restore drill, DB access, seeding, operations
  pipelines/                Azure Pipelines YAML
docs/
  product/                  SRS, ERD, architecture, lifecycle diagrams — current truth
  ux/                       Design system, logo, imagery
  legal/                    Privacy policy + booking terms — published and live; no jurist review (cancelled, see known-conflicts-at-launch.md)
  review/                   Review artefacts that still carry open items
  archive/                  Closed history — read-only, nothing waiting on it (see docs/archive/README.md)
  handover/                 Admin manual (source of truth is the .html)
```

The public/admin split is **structural, not just routing** — the internet-facing public process loads no
Identity, SignalR, or admin assemblies, so an admin route on the public host is a genuine 404. The two
run as separate App Service sites on one shared B1 plan.

## Local development

Requires the .NET 10 SDK (pinned in [`global.json`](global.json)) and Docker.

```bash
docker compose up -d      # PostgreSQL, Redis, Azurite (Blob), Mailpit (SMTP)
dotnet restore
```

| Service | Port | Notes |
|---|---|---|
| PostgreSQL | 5432 | credentials in [`.env.example`](.env.example) |
| Redis | 6379 | rate limiting |
| Azurite | 10000–10002 | Blob storage emulator |
| Mailpit | 1025 (SMTP) / **8025 (web UI)** | catches all outbound mail — open the UI to read it |

Configuration comes from `dotnet user-secrets`, not `.env` files or `appsettings.json` — **no secrets are
ever committed**. Set the connection string per project, e.g.:

```bash
dotnet user-secrets set "ConnectionStrings:Postgres" "..." --project src/AkeroGarden.Web.Admin
```

Run either surface:

```bash
dotnet run --project src/AkeroGarden.Web.Admin
dotnet run --project src/AkeroGarden.Web.Public
```

For seeding local reference data, see [`infra/ops/seeding.md`](infra/ops/seeding.md).

### Seeing a scheduled email locally

The pre-arrival reminder, completion nudge, completion-link issuance and pre-deletion warning are all sent by
the daily background job, which fires once a day at `Jobs:DailyRunHourLocal` — **03:00 Europe/Stockholm
by default, and always in Stockholm time regardless of the machine's zone**. Waiting for that is not a
workflow, so:

```bash
DemoSeed__Reset=true Jobs__RunOnStartup=true dotnet run --project src/AkeroGarden.Web.Admin
```

`Jobs:RunOnStartup` runs the jobs once immediately, then hands back to the normal schedule. It is off
by default and must never be set in production. `DemoSeed:Reset` wipes app data (the admin user
survives) and reseeds — including one `DetailsComplete` booking with check-in exactly 14 days out and
deliberately non-standard times, which is the booking the reminder actually picks up.

Mail lands in Mailpit at <http://localhost:8025>.

## Tests

Tests are written **with** each feature, not after. All three tiers run in CI and any failure fails the
build.

```bash
dotnet test                                        # everything
dotnet test tests/AkeroGarden.Unit                 # one tier
```

E2E needs Playwright browsers installed once (`pwsh tests/AkeroGarden.E2E/bin/.../playwright.ps1 install`
after a first build). There is deliberately **no pixel/visual-regression diffing** — it is flaky across
macOS↔CI-Linux font rendering, so the suite asserts behaviour and structure instead.

A test must honestly verify what it claims. Never route a test around a known bug to get it green — fix
the bug, or assert the current wrong behaviour so it stays visible. See `.claude/CLAUDE.md` for the full
test-integrity rule.

## Infrastructure and deployment

Every Azure resource is defined in Bicep under [`infra/azure/`](infra/azure/). Production and staging
deploy from the same `main.bicep` with different parameter files. **Nothing is provisioned by clicking
in the portal** — if it is not in code, it does not exist.

Deploy flow is `dev → CI → staging → production`. CI builds once and deploys that same artifact; the
production pipeline is manual and gated behind an environment approval. See
[`infra/azure/README.md`](infra/azure/README.md) and [`infra/pipelines/pipeline-flow.md`](infra/pipelines/pipeline-flow.md).

## Constraints worth knowing before you change anything

These are enforced, not stylistic. The full list lives in `.claude/CLAUDE.md` and the SRS.

- **Swedish-only UI**, from day one. No English strings, no i18n framework.
- **No personal data outside the EU** — all Azure resources in North Europe or Sweden Central.
- **No PII in URLs**; **no Swedish personnummer** collected or stored, ever.
- **Nothing loads from a third-party host** — no font CDN, analytics, tag manager, embedded map, video or social widget. Fonts are self-hosted. Widened from fonts-only in 2026-08 and **enforced since 2026-09-08 by a `default-src 'self'` Content-Security-Policy on both hosts** (NFR-39), so this is no longer a convention you could breach by accident — the browser blocks it. The same policy forbids inline styling, so `style="…"` attributes in `.razor` markup are dropped; use a modifier class in `site.css`.
- **No monetary totals stored, and no payment state at all** — prices are reference data. Invoicing happens entirely outside the system, so there is nothing here to track it with (FR-42 retired 2026-08).
- **No hard-deletes of referenced reference data** — deactivate instead.
- **The booking state machine is one-way** — invalid transitions are blocked in code, not just hidden in the UI.
- **Code style is enforced at build time** — `.editorconfig` + `Directory.Build.props`; formatting
  violations fail the build.

## Documentation

| Document | What it is |
|---|---|
| [`docs/product/requirements.md`](docs/product/requirements.md) | The SRS — single source of truth. Unversioned living document |
| [`docs/product/erd.md`](docs/product/erd.md) | Entity-relationship diagram (15 entities) |
| [`docs/product/architecture.md`](docs/product/architecture.md) | App-level architecture — surfaces, hosting, external services |
| [`docs/archive/implementation-plan.md`](docs/archive/implementation-plan.md) | Closed history: what was built, in what order, satisfying which requirements. **All phases 0–11 are done** and each is one table row. Not a source of truth — the SRS is |
| [`docs/ux/design.md`](docs/ux/design.md) | Design system — tokens, type scale, components |
| [`infra/ops/`](infra/ops/) | Operational runbooks |

If this README and the SRS ever disagree, **the SRS wins** — raise the conflict rather than silently
picking one.
