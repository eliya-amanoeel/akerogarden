# Åkerögården — App-Level Architecture

**Derived from:** requirements.md, §1.3 (Scope), §1.5 (Technology Decisions), §2.5 (Booking Status Transitions), §2.6 (Magic Link Rules), §6.4 (Admin Auth), §6.7/FR-63–66 (Automated Notifications), §6.9 (Audit Trail), §10 (Out of Scope), §11/§11.1 (Hosting & Environments).
**Status:** Current — the two-site topology described here is built (Phase 10b) and **live in production since 2026-09-07**, serving `akerogarden.se` (public) and `admin.akerogarden.se` (admin) over managed SSL in the client tenant. Staging has run the same topology since 2026-07-17. *(This line asserted the opposite — "production is not yet provisioned, nor cut over" — until the 10d cutover; production was in fact provisioned back on 2026-07-20.)*

This is a system/component view — how the public site, admin panel, background jobs, and external Azure services fit together. For the data model see [erd.md](erd.md); for infra-as-code see the `.bicep` templates under `infra/`.

---

## Tech stack (locked, §1.5)

| Layer | Technology |
|---|---|
| Web framework | .NET 10 LTS Blazor — Static SSR (public), Interactive Server (admin) |
| Data access | EF Core + PostgreSQL |
| Database hosting | Azure Database for PostgreSQL Flexible Server (Burstable B1ms), VNet-only |
| Auth | ASP.NET Core Identity (email + password, admin panel only) — two roles: `Administratör` (full) and `Samordnare` (list-download only, FR-104) |
| App hosting | Azure App Service, Basic B1 (Linux) |
| Rate limiting | Azure Managed Redis (Balanced B0) |
| File/image storage | Azure Blob Storage (Hot tier, LRS) — facility images only; the public pages' fixed photos are static files in the public app |
| Email | MailKit over One.com SMTP |
| CI/CD | Azure DevOps Pipelines (migrated from GitHub Actions, Phase 10a) |
| Infra-as-code | Bicep (prod + staging from the same templates, different parameter files) |

This stack is locked per the project's CLAUDE.md — changes require explicit discussion, not silent substitution.

---

## Diagram

```mermaid
flowchart TB
    Guest["Guest browser"]
    Admin["Admin browser"]

    subgraph Plan["Azure App Service Plan (Linux, B1) — one plan, two sites"]
        direction TB
        subgraph PublicSite["Public site — akerogarden.se"]
            Public["Blazor Static SSR\n(home, priser, calendar, inquiry form,\nmagic-link completion)\nno Identity · no SignalR · no admin code"]
        end
        subgraph AdminSite["Admin site — admin.akerogarden.se"]
            AdminApp["Admin panel\nBlazor Interactive Server\nIdentity login · roles:\nAdministratör / Samordnare"]
            BgJob["In-process BackgroundService\n(Always On)\nlink issuance · reminders · GDPR retention ·\npre-deletion warnings"]
        end
    end

    subgraph Data["Data & storage (VNet-only, EU region)"]
        Postgres[("Azure Database for PostgreSQL\nFlexible Server (Burstable B1ms)\napp entities + AspNetUsers")]
        Redis[("Azure Managed Redis (Balanced B0)\ndistributed rate-limit counters")]
        Blob[("Azure Blob Storage (Hot LRS)\nfacility images · Data Protection key rings\nPRIVATE — no anonymous access,\nreached only by the apps via managed identity")]
    end

    SMTP["One.com SMTP\n(via MailKit)"]

    Guest -->|"HTTPS"| Public
    Admin -->|"HTTPS + Identity cookie"| AdminApp

    Public -->|"read-only availability query\n(least-privilege role: no DDL)"| Postgres
    Public -->|"submit inquiry"| Postgres
    Public -->|"rate limit check"| Redis
    Public -->|"serve facility images\nvia /bilder/{name}"| Blob

    AdminApp -->|"CRUD reference data,\nbooking lifecycle, audit writes\n(owner role: runs migrations)"| Postgres
    AdminApp -->|"upload/read images"| Blob

    BgJob -->|"scan due bookings,\nwrite AuditLog (ActorType=System)"| Postgres
    BgJob -->|"send reminder/retention emails"| SMTP

    AdminApp -->|"send approval/denial/notification emails"| SMTP
    Public -->|"send inquiry acknowledgement"| SMTP

    Guest -.->|"magic-link token\n(single-use, ≤30d, no login)"| Public

    GH["Azure DevOps Pipelines CI/CD"] -->|"deploy both apps"| Plan
    Bicep["Bicep templates\n(infra/)"] -->|"provision"| Plan
    Bicep -->|"provision"| Data

    style Plan fill:#f5f0e6,stroke:#333
    style PublicSite fill:#faf6ee,stroke:#999
    style AdminSite fill:#faf6ee,stroke:#999
    style Data fill:#e8f0e8,stroke:#333
```

---

## Notes

1. **Two App Service sites on one B1 plan** (Phase 10b, FR-70). `akerogarden.se` (public) and `admin.akerogarden.se` (admin) are **two independent App Service sites** sharing the one Basic B1 App Service **Plan** — not one app serving both hostnames, and not slots (B1 doesn't support slots). Separation is a property of the **assembly graph and the deployment**: the code is three projects (`AkeroGarden.Web.Core` shared RCL + `.Public` + `.Admin`), and the internet-facing public process loads no Identity, SignalR, or admin code at all — so an admin route such as `/bokningar` on the public host is a genuine 404, not an `[Authorize]` redirect. The Plan is the billing unit, so the second site (and its second managed SSL cert) add **zero cost**; the only trade-off is two .NET processes sharing the one B1 instance's 1.75 GB RAM (see `infra/azure/README.md`, SKU limitations).

   > **Do not try to collapse this back into one app with host gating — it was tried and it does not work.** Two `MapRazorComponents<T>()` roots in a single app, each with `RequireHost(...)`, throw `AmbiguousMatchException`: both roots try to serve `/_framework/*` (dotnet/aspnetcore#52216, #52631; still no first-class API as of .NET 10). The single-app alternatives carry their own unverified sharp edges — `MapWhen` sub-pipelines hit `MapStaticAssets`-per-branch (#62249), and a host-scoped `Router` puts a render-mode endpoint inside a branch. The two-app topology sidesteps all of it, because each app has exactly one root component and its own `/_framework`. Verified during Phase 10b, which is also where the earlier single-app attempt was reversed.

2. **Rendering modes differ by surface.** Public pages are Blazor **Static SSR** — no persistent circuit, cheapest to serve, matches the read-mostly/inquiry-form nature of the public site. The admin panel is Blazor **Interactive Server** — a live SignalR circuit per session, justified by the admin panel's CRUD-heavy, stateful interactions (§1.5).

3. **Guests never authenticate.** There is no guest account system. The only guest-facing "auth" is the magic-link token — issued at approval when check-in is within 40 days, otherwise by the nightly job once it is (FR-108): single-use, ≥128-bit entropy, expiring at the earliest of 30 days after issue or 14 days before check-in; a link issued 14 days or fewer out runs instead to 3 days before check-in, never less than 48 hours — §2.6, FR-51–54, NFR-03. Admin auth is the only real login surface, via ASP.NET Core Identity (§6.4, FR-70–74), carrying two roles — `Administratör` with full access and `Samordnare` restricted to the two per-booking list downloads (FR-104). Authorisation is enforced **per page**: `Samordnare` has its own booking list and download view and no access to the booking detail page at all, rather than sharing a page with parts conditionally hidden. Hiding fragments would mean every future addition to that page needed a role check, and a single omission would leak guest contact data.

4. **The background job is in-process, not a separate service.** Scheduled work (pre-arrival reminders, completion nudges, deferred completion-link issuance (FR-108), GDPR retention deletes, 30-day pre-deletion warnings) runs as an in-process hosted `BackgroundService` inside the **admin** site's process (Phase 10b — it lives only on the admin host, so it runs in exactly one process; the public site is purely request-serving), kept alive by **Always On** (B1 supports it). An external scheduler (Azure Functions timer trigger or Logic App) was evaluated and deferred — §10, FR-69/91/98. It authors `AuditLog` rows with `ActorType = System`.

5. **Redis is rate-limiting only, and public-only.** Azure Managed Redis (successor to the retired Azure Cache for Redis) holds no application data — its sole job is distributed rate-limit counters for the inquiry form. Only the **public** site is rate-limited, so only the public site holds the Redis connection; the admin site never touches Redis (Phase 10b, NFR-04).

6. **Postgres is VNet-only.** No public internet access to the database — reachable only via VNet integration from the App Service (NFR-25). This is also why staging is a fully separate resource group rather than a slot: network isolation and synthetic data can't leak into/from production this way (§11.1).

7. **The browser never contacts Blob storage.** Facility images are served by each app from
   `/bilder/{name}`, not from a Blob URL. `img-src 'self'` (NFR-39) blocks a storage-account origin
   outright, so FR-81's "display the facility image" and the CSP could not both hold while images were
   fetched directly — the images were silently invisible for a day before anyone noticed, because
   production had none uploaded. Routing through the app satisfies both and means a visitor's IP never
   reaches the storage domain, which is what NFR-35 is really about. The container is private and both
   apps reach it with their own managed identity; no storage account key exists in configuration
   (post-launch security review #9, #25). The fixed photographs on the public pages never touch
   storage at all: they are static files in the public app (`wwwroot/images/`), served from the same
   origin and changed by deployment, not through admin (`docs/ux/design.md`, Imagery).

8. **The two apps use different database accounts.** The public site connects as a least-privilege
   role that can read and write rows but cannot create or alter tables; the admin site connects as the
   owner because it runs EF migrations at startup. Migrations cannot move to the deploy pipeline —
   PostgreSQL is VNet-only and the hosted build agent cannot reach it — so they must run from inside a
   container, which is why the admin credential still exists (post-launch security review #4).

9. **Identity tables live in the same Postgres instance** as the application schema, but are treated as infrastructure, not part of the application data model (§6.4) — see the note at the top of [erd.md](erd.md).

10. **Everything in this diagram must exist in Bicep.** Per the project's IaC rule, both production and staging are provisioned from the same `.bicep` templates with different parameter files — no resource shown here should ever be click-through-provisioned.
