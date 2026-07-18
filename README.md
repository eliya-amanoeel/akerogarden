# Åkerögården — Booking System

Booking and administration system for **Åkerögården**, a rural retreat in Norrtälje, Sweden. It handles
guest inquiries, the booking lifecycle, participant and meal details, and the admin workflows around
them. It replaces a decommissioned Payload CMS.

All guest- and admin-facing UI is **Swedish**. The code, comments, and documentation are English.

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
  product/                  SRS, ERD, architecture, implementation plan
  ux/                       Design system, logo, imagery
  legal/                    Privacy policy + booking terms (drafts, pending jurist review)
  review/                   Architecture and security review artefacts
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
- **Self-hosted fonts only** — no Google Fonts or any external font request (GDPR).
- **No monetary totals stored** — prices are reference data; payment tracking is boolean status.
- **No hard-deletes of referenced reference data** — deactivate instead.
- **The booking state machine is one-way** — invalid transitions are blocked in code, not just hidden in the UI.
- **Code style is enforced at build time** — `.editorconfig` + `Directory.Build.props`; formatting
  violations fail the build.

## Documentation

| Document | What it is |
|---|---|
| [`docs/product/requirements.md`](docs/product/requirements.md) | The SRS — single source of truth. Unversioned living document |
| [`docs/product/erd.md`](docs/product/erd.md) | Entity-relationship diagram (14 entities) |
| [`docs/product/architecture.md`](docs/product/architecture.md) | App-level architecture — surfaces, hosting, external services |
| [`docs/product/implementation-plan.md`](docs/product/implementation-plan.md) | Phased build plan and its exit criteria |
| [`docs/ux/design.md`](docs/ux/design.md) | Design system — tokens, type scale, components |
| [`infra/ops/`](infra/ops/) | Operational runbooks |

If this README and the SRS ever disagree, **the SRS wins** — raise the conflict rather than silently
picking one.
