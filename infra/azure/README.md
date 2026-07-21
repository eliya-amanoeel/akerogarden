# Infrastructure

Bicep templates for Åkerögården. Both production and staging deploy from the same
`main.bicep` with different parameter files (`staging.parameters.json`,
`prod.parameters.json`).

> **Status:** staging is live in **Sweden Central** (`akerogarden-staging`), in the **client's own Azure subscription/tenant** (PAYG), on `staging.akerogarden.se` (public) + `admin.staging.akerogarden.se` (admin).
> **Phase 10a ✅ done (2026-07-16)** — staging was recreated fresh in the client's tenant from these same Bicep templates (a redeploy, not a migration; Azure's resource-move feature doesn't work across tenants) and the old MSDN-hosted copy deleted.
> **Phase 10b ✅ done (2026-07-17)** — the single web app was split into two App Service sites on the one shared plan (see "Two-app topology" below); staging is cut over and verified.
> **Phase 10c (production pre-stage) — not started.** Production is provisioned directly in the client's tenant against this same two-app topology, and runs on its Azure default `*.azurewebsites.net` hostnames throughout 10c — no DNS involvement. ⚠️ `prod.parameters.json` is **not yet deployable**: `seedAdminEmail` / `seedAdminPassword` are required by `main.bicep` and still absent, pending creation of the `info@akerogarden.se` mailbox. It also still declares the real hostnames in `publicHostnames`/`adminHostnames`, which cannot bind while the apex resolves to WordPress — empty both for the 10c deploy and populate them in 10d. `location` reads `swedencentral` (staging was forced there by a 0 App Service quota in North Europe) — verify that quota actually exists in the client's subscription before deploying, since a quota request is a multi-day support ticket.
> **Phase 10d (cutover) — not started.** Custom-hostname binding, managed SSL (`ssl.bicep`) and the WordPress retirement. Split out from 10c because Azure cannot issue a managed certificate until the domain already resolves to the App Service, so these are the only genuinely DNS-dependent steps.

## What `main.bicep` creates

Two user-assigned managed identities (one per site), Key Vault (access-policy model, **not** RBAC)
with the SMTP + Redis secrets, one App Service **Plan** (Linux, B1) hosting **two App Service sites**
— `jmk-ag-<env>-public` and `jmk-ag-<env>-admin` (see "Two-app topology" below) — PostgreSQL
Flexible Server (VNet-only, `publicNetworkAccess: Disabled`, zone 1), VNet with delegated app/db
subnets, private DNS zone for Postgres, and Blob storage.

Redis (Azure Managed Redis, Balanced B0 — the successor to the retired classic Azure
Cache for Redis) backs the inquiry rate limiter (NFR-04); its connection string is stored
in Key Vault and read by the **public** site via its managed identity (the admin site has no
rate limiting, so it never reads the Redis secret).

### SKU limitations — accepted deliberately (2026-07-20)

Both load-bearing tiers are entry-level, and both carry limitations that are easy to rediscover the
hard way. They were reviewed and **kept as-is** for production: this site's traffic is a handful of
inquiries a week, and Åkerögården has taken bookings by phone and email for years, so an outage
degrades to the pre-existing manual process rather than stopping business. Moving up a tier would
roughly triple the ~$45/month hosting envelope in `requirements.md` §11 to buy headroom for load that
will not arrive.

**PostgreSQL Flexible Server — `Standard_B1ms` (Burstable):**

- **No read replicas.** The portal states plainly: *"Read replica is not supported for the Burstable
  pricing tier."* This rules out the geo-replica DR pattern in the Azure docs entirely — not on cost
  grounds, on availability grounds.
- **No high availability**, and the server sits in a single availability zone (zone 1).
- **CPU is credit-based** — sustained load exhausts burst credits and throttles. Fine for this
  workload's spiky, low-volume profile; it would not be for a continuously busy one.
- Microsoft positions Burstable for **dev/test** workloads. It is nonetheless a GA tier with an SLA,
  and supported in production — the positioning is guidance about workload *shape*, not a support
  restriction.
- `geoRedundantBackup` is left **disabled** (the template does not set it, so it defaults off). Backup
  protection is local PITR, which is what actually defends against the realistic failure — a bad
  migration or an accidental delete. Note this is believed to be settable **only at server creation**.
- **Storage autogrow is disabled** and storage is 32 GiB. If the database ever fills that, PostgreSQL
  goes read-only and the booking system stops accepting writes. Autogrow *can* be enabled after
  creation, so this is a monitoring concern rather than a provisioning decision.

**App Service — B1 (Basic):**

- **No deployment slots** — this is why staging is a separate resource group rather than a slot.
- **No autoscale**, and a **single instance**: a platform restart or a bad deploy is downtime, with no
  second instance to absorb it.
- Both sites share the one B1 plan's 1.75 GB RAM (see "Two-app topology" below).

**Redis (Azure Managed Redis, Balanced B0) — checked and cleared, *not* an entry-tier concern.**
Unlike the two above, this was audited against MS Learn + the live portal (2026-07-21) and is a
legitimate production configuration:

- **Balanced is a production tier** ("standard workloads"), SLA-backed — it carries no dev/test
  positioning. B0 (0.5 GB) is merely the smallest *size*, with a 15,000 max-connection ceiling this
  workload will never approach.
- **High availability is Enabled** (verified on the staging instance's portal Overview). Managed Redis
  becomes dev/test-only *only if HA is explicitly disabled* — the Bicep does not disable it, so this is
  clean. HA means primary + replica across ≥2 nodes, zone-distributed where the region supports it.
- **Data persistence is Disabled, by design** — the only data is inquiry rate-limiter keys, all
  short-lived (TTL) and behind a fail-open limiter (NFR-04); if the cache is ever emptied, rate
  limiting momentarily resets and nothing is lost. Persistence would be pure cost for no benefit.

> Verify feature availability against the live portal or MS Learn before assuming a tier supports
> something — several of the limits above were assumed the other way round first, and were only caught
> by checking the portal.

## Two-app topology (Phase 10b / FR-70)

The public and admin surfaces are **two independent App Service sites on the one B1 plan** —
genuine process isolation: the internet-facing public process never loads Identity, SignalR, or
admin code, so `/admin` on the public host is a real 404, not an `[Authorize]` redirect. The
**App Service Plan is the billing unit**, so the second site (and its second managed SSL cert) add
**zero cost**; the only trade-off is two .NET processes sharing the one B1 instance's 1.75 GB RAM.

Everything stateful (Postgres, Redis, Blob, Key Vault, VNet, telemetry) is **shared**. `main.bicep`
loops over an `apps` array (index 0 = public, 1 = admin) to emit the per-site resources; the only
divergence is:

| Concern | Public site | Admin site |
|---|---|---|
| Hostnames | `publicHostnames` param | `adminHostnames` param |
| Render mode | Static SSR only | Interactive Server + Identity |
| Redis rate limiter | ✅ (`Redis__ConnectionString`) | — |
| Seed-admin + image-store Blob | — | ✅ (`Seed__*`, `Blob__ConnectionString`) |
| Background jobs / DB migration | — | ✅ (admin is the sole migrating process) |
| Data-Protection ring | `dataprotection-keys-public` container | `dataprotection-keys-admin` container |
| Managed identity | `jmk-ag-<env>-public-identity` | `jmk-ag-<env>-admin-identity` |

`ssl.bicep` issues one managed cert per `{ siteName, hostname }` pair (see `<env>.ssl-parameters.json`)
and upgrades each hostname binding to SNI SSL, after DNS points each hostname at its site.

## Naming

The resource-group name stays `akerogarden-staging` / `akerogarden-prod`, but every resource
*inside* it is named from a `jmk-ag-<environmentName>` prefix (`jmk-ag-staging-public`,
`jmk-ag-staging-admin`, `jmk-ag-staging-kv`, etc. — `jmk` = Jungfru Maria Kyrkan, the church that owns the project;
`ag` = Åkerögården), not `akerogarden-<environmentName>-*`. This was changed during Phase 10a:
several of these names are globally unique across **all** of Azure, not just this subscription
(App Service hostname, Storage Account, Key Vault, Azure Managed Redis, Postgres Flexible
Server), and the plain `akerogarden-staging-*` names collided with unrelated resources —
see the gotchas below.

## Prerequisites

- Azure CLI logged in: `az login`
- Bicep CLI: `az bicep install`
- The secret values below, kept in a password manager (they are **never** committed —
  the parameter files carry `REPLACE_VIA_CI_SECRET` placeholders and the real values
  are passed on the CLI at deploy time):
  - `postgresAdminPassword`
  - `smtpUsername`, `smtpPassword` (One.com SMTP; staging currently uses a temporary
    `temp@amaeli.se` mailbox on port 465)
  - `seedAdminEmail`, `seedAdminPassword` (bootstraps the first admin on app startup)

## 1. Provision staging

```bash
az group create --name akerogarden-staging --location swedencentral

az deployment group create \
  --resource-group akerogarden-staging \
  --template-file main.bicep \
  --parameters @staging.parameters.json \
  --parameters postgresAdminPassword='<pg-password>' \
               smtpUsername='<smtp-user>' \
               smtpPassword='<smtp-password>' \
               seedAdminEmail='<admin-email>' \
               seedAdminPassword='<admin-password>'
```

Verify the DB is not publicly reachable:

```bash
az postgres flexible-server show -g akerogarden-staging -n jmk-ag-staging-pg \
  --query "network.publicNetworkAccess" -o tsv   # expect: Disabled
```

## 2. Wire the Azure DevOps deploy pipeline (one-time, manual)

CI/CD moved from GitHub Actions to Azure DevOps (`dev.azure.com/akerogarden/akerogarden`)
during Phase 10a. Deploys authenticate via a **service connection** backed by a service
principal (`akerogarden-devops-deploy`) — not a publish-profile secret — so there is no
per-environment credential to regenerate on every re-provision:

```bash
# One-time: service principal scoped to the resource group, then wired as a
# service connection named "akerogarden-azure-connection" in the Azure DevOps project.
az ad sp create-for-rbac --name "akerogarden-devops-deploy" \
  --role Contributor \
  --scopes "/subscriptions/<sub-id>/resourceGroups/akerogarden-staging"

az devops service-endpoint azurerm create \
  --azure-rm-service-principal-id <appId> \
  --azure-rm-subscription-id <sub-id> \
  --azure-rm-subscription-name "Åkerögården" \
  --azure-rm-tenant-id <tenant-id> \
  --name "akerogarden-azure-connection" \
  --org https://dev.azure.com/akerogarden --project akerogarden
```

When re-provisioning (a new resource group, e.g. a future tenant move), add a role
assignment for the *same* service principal scoped to the new resource group — the
service connection itself doesn't need to change.

The `Deploy - Staging` pipeline (`infra/pipelines/azure-pipelines-deploy-staging.yml`) is manual-only
(`trigger: none`) — run it from Pipelines → Deploy - Staging → Run pipeline. The first
run against a fresh service connection prompts "This pipeline needs permission to
access resources" — click Authorize once.

## PR validation on `main` (branch policy — *not* the pipeline YAML)

`main` is PR-gated by a **build-validation branch policy** named `CI must pass`, which runs the
`CI` pipeline against the PR merge commit. This is the only mechanism that validates a PR.

**Azure DevOps ignores YAML `pr:` triggers for Azure Repos** (they work for GitHub/Bitbucket
only). A `pr:` block in `infra/pipelines/azure-pipelines.yml` would look authoritative and do
nothing — one was removed for exactly that reason. The `trigger:` block in that file *is* real,
but it only governs CI on merge to `main`, not PR validation.

### The path filter has three ways to silently disable the gate

All three were hit on 2026-07-18 while trying to skip CI for docs-only PRs. Every one produces the
same symptom — **no build runs, no error anywhere, and the policy still reports itself as enabled
and blocking** — so `main` sat unvalidated across several PRs before anyone noticed:

1. **Exclusions alone select nothing.** `!*.md;!**/*.md` matches no file, so the policy applies to
   nothing. The filter needs an **include** for the exclusions to subtract from: `/*;!*.md;!**/*.md`.
2. **Patterns are separated by `;`, not spaces.** `'!*.md !**/*.md'` is stored as one malformed
   pattern.
3. **`!` is history-expanded by zsh, even inside double quotes.** `"!*.md"` reaches `az` as
   something else entirely (it silently became `test.md` once). **Always single-quote** a
   `--path-filter` containing `!`.

The filter is currently `/*` — every PR runs CI, including docs-only ones. That is deliberate:
wasted CI minutes on a README edit are cheaper than an ungated `main`.

```bash
# Read the current filter (do this after ANY change — the command's exit code proves nothing)
az repos policy show --policy-id 1 --query "settings.filenamePatterns" -o json

# Update it. Note the single quotes.
az repos policy build update --policy-id 1 \
  --repository-id <repo-id> --branch main \
  --blocking true --enabled true --build-definition-id 1 \
  --display-name "CI must pass" \
  --queue-on-source-update-only false --manual-queue-only false \
  --valid-duration 0 \
  --path-filter '/*'
```

A policy change does **not** re-evaluate open PRs — push a commit (or use ⋯ → Re-evaluate policies
on the PR) and confirm a run appears with reason `pullRequest`:

```bash
az pipelines runs list --status inProgress \
  --query "[].{id:id, reason:reason, branch:sourceBranch}" -o tsv
```

The `codecoverage` status that appears on PRs is posted automatically by Azure Pipelines and
resolves to `notApplicable` (CI publishes no coverage data). It is informational and cannot block a
merge — there is no status-check policy.

## Code coverage in CI (reporting only, `AkeroGarden.Web.Core` only)

The `CI` pipeline collects coverage on the **unit and integration** tiers and publishes a merged
report to the run's **Code Coverage** tab. Two deliberate limits, both recorded here so the numbers
are not misread later:

**1. Reporting only — there is no coverage threshold and no build failure on a drop.** A percentage
gate rewards tests that execute lines without asserting much, which is precisely what this repo's
test-integrity rule forbids (`.claude/CLAUDE.md`). Coverage is used as an occasional gap-finder —
"what did we forget exists?" — not as a target. When it was last run as such (2026-07-18) it closed
two genuine 0% gaps and surfaced a real DST bug, while moving the headline figure only 66.5% →
66.9%: a threshold would have been nearly blind to all of it.

**2. The published report is filtered to `AkeroGarden.Web.Core`** (`-assemblyfilters:"+AkeroGarden.Web.Core"`).
`AkeroGarden.Web.Admin` and `AkeroGarden.Web.Public` are **excluded, and their pages are not
uncovered — they are unmeasurable**. The E2E tier launches both host apps *out-of-process*, so the
coverage collector never attributes that execution back to the component classes. The tell is
internal to the report: `Program` shows 74–82% and the shared layouts show 100% (lit up by the
*in-process* integration tests) while every `@page` component shows 0%, even though the Playwright
suite genuinely drives those pages in a real browser.

Publishing unfiltered would therefore advertise ~67% when the meaningful figure is Core's ~94%, and
invite someone to "fix" a measurement artefact by writing Razor tests that assert nothing. **Do not
write tests to move the Admin/Public numbers.**

Coverage is likewise **not collected on the E2E tier at all** — same reason: it costs runtime and
attributes nothing.

Known-low areas inside Core that are *not* worth chasing, already assessed: `DataProtectionSetup`
(its pure `ResolveMode` is fully covered by design; the remainder is unmockable Azure I/O — a live
`BlobContainerClient` call and `DefaultAzureCredential` — verifiable only by the staging rehearsal),
`BlobImageStorage` (same), and the display-shaping helpers.

To reproduce the full, unfiltered picture locally, see the coverage commands in the project memory
or run `reportgenerator` without `-assemblyfilters` — remembering that Admin/Public will look far
worse than they are.

## 3. First deploy

Run `Deploy - Staging` manually from Azure Pipelines. The app applies EF migrations and
seeds the admin **on startup, inside the VNet** (the VNet-only DB is unreachable from
hosted agents, so migrations cannot run from the pipeline — only from the running app).

## Recreate staging in the client's tenant (Phase 10a)

Same steps as "Provision staging" above, but targeting the client's subscription: switch
context first (`az login --tenant <client-tenant-id>`, `az account set --subscription
<client-sub-id>`), then deploy with `staging.parameters.json` updated so `publicHostnames`
binds `staging.akerogarden.se` and `adminHostnames` binds `admin.staging.akerogarden.se`, and
re-run steps 2–3 (add a role assignment for `akerogarden-devops-deploy` scoped to the new
resource group, re-verify the first deploy). Once verified, delete the old `akerogarden-staging`
resource group in the MSDN subscription.

## Re-point staging to the two-app topology (Phase 10b)

Phase 10b turns the one `jmk-ag-staging-app` site into two sites (`-public` + `-admin`) on the
**same** plan. Deploying the updated `main.bicep` **adds** the two new sites (it does not delete the
old `-app` site — incremental mode). Procedure:

1. `Deploy - Staging` with the updated `main.bicep` → creates `jmk-ag-staging-public` +
   `jmk-ag-staging-admin`, each with its own identity and DP container. The old `-app` site is left
   running but orphaned.
2. Deploy the **public** artifact to `-public` and the **admin** artifact to `-admin` (Slice 5's
   two-target pipeline). The admin site migrates the DB on startup; start it before relying on the
   public site's DB-backed pages.
3. Re-point DNS: `staging` CNAME → `jmk-ag-staging-public.azurewebsites.net`, `admin.staging` CNAME →
   `jmk-ag-staging-admin.azurewebsites.net` (the `asuid.<host>` TXT records are unchanged). The
   per-site `hostNameBindings` in `main.bicep` validate against these.
4. Run `ssl.bicep` with `staging.ssl-parameters.json` (the `bindings` array pairs each hostname with
   its site) to issue the two managed certs and SNI-bind them.
5. Verify `200` on both hostnames, then **delete the orphaned old site**:
   `az webapp delete -g akerogarden-staging -n jmk-ag-staging-app` (and remove its now-unused
   `jmk-ag-staging-identity` if the pre-10b single-identity is still around).

## Deploy production (Phase 10c)

Same as staging with `prod.parameters.json` and a `akerogarden-prod` resource group, in the
client's tenant, and **born** in the two-app topology (no re-point step — production is provisioned
as two sites from the start). This step is DNS-independent: the apps come up on their
`*.azurewebsites.net` hostnames and everything except hostname binding is verified there.

Binding the custom domains (`akerogarden.se` → public, `admin.akerogarden.se` → admin) and
issuing managed SSL via `ssl.bicep` happen later, in **Phase 10d**, because certificate
issuance requires DNS to already resolve to the App Service. See `ssl.bicep` and the
Phase 10c / 10d plan.

## Cost + health alerts (Phase 11)

`alerts.bicep` is a separate template from `main.bicep` (same pattern as `ssl.bicep`), deliberately
kept minimal for a solo-maintained, low-traffic site rather than the fuller per-resource-type alert
set a team with an on-call rotation might build. It creates:

- an Action Group (email → `adminNotificationEmail`, no new mailbox)
- a monthly cost budget (default 600 SEK, alerts at 80%/100% actual — `Microsoft.Consumption/budgets`
  has no currency property, the amount is always in the subscription's own billing currency; this
  one bills in SEK, not USD, confirmed via `az consumption budget show ... --query currentSpend.unit`
  after an earlier deploy accidentally set 60 assuming USD)
- Http5xx alerts on both App Service sites
- one Postgres alert on `cpu_credits_remaining` — the specific canary tied to the 2026-07-21
  decision to accept the Burstable B1ms tier and watch rather than load-test
- one Application Insights availability ping test against the public site's default
  `*.azurewebsites.net` hostname (survives the Phase 10d DNS cutover unchanged), from 2 EU
  locations so a single edge PoP blip doesn't fire a false alert

Deliberately left out (add only if something real motivates it): response-time thresholds,
Postgres cpu%/storage%/connection-count alerts, Key Vault availability, an admin-site ping (admin
shares the same App Service plan as public — if the plan is down, the public ping already catches
it).

**Cost note:** unlike metric alerts (free up to 10 time series), availability tests are billed per
execution — $0.0005 each. At the default 15-minute interval × 2 locations that's ~$2.88/month, on
top of a few cents for the metric alerts. Not free, but small next to the ~$45/month base spend.

```bash
az deployment group create --resource-group akerogarden-prod \
  --template-file alerts.bicep --parameters @prod.alerts-parameters.json
```

## Dashboard (Phase 11)

`dashboard.json` is a one-glance portal dashboard pulling together everything `alerts.bicep`
creates: the availability chart, both sites' Http5xx, Postgres `cpu_credits_remaining`, the budget
spend summary, and an alerts overview. It's **not** a Bicep template — Azure Dashboard tile JSON
(`Microsoft.Portal/dashboards`) is impractical to hand-author, so this was built visually in the
portal (Dashboard → Edit → pin/add each tile) and captured via `az portal dashboard show`, the
same way anyone builds one, then committed here so it's still tracked as code rather than a
click-through-only artifact.

**Note:** dashboards default to **private** (per-user, not an ARM resource) unless explicitly
published — use **Share → Publish** in the portal, targeting the `akerogarden-prod` resource
group, before it's exportable at all.

Redeploy (e.g. after deleting and wanting it back, or standing up the same dashboard in another
subscription):

```bash
az extension add --name portal   # one-time
az portal dashboard create --resource-group akerogarden-prod \
  --name jmk-ag-prod-dashboard --location swedencentral \
  --tags hidden-title="Åkerögården Prod" \
  --input-path dashboard.json
```

To re-export after further editing it in the portal:

```bash
az portal dashboard list --resource-group akerogarden-prod --query "[].name" -o tsv
az portal dashboard show --resource-group akerogarden-prod --name <name-from-above> \
  --query properties -o json > dashboard.json
```

## Power staging down (save cost)

```bash
az webapp stop --resource-group akerogarden-staging --name jmk-ag-staging-public
az webapp stop --resource-group akerogarden-staging --name jmk-ag-staging-admin
az postgres flexible-server stop --resource-group akerogarden-staging --name jmk-ag-staging-pg
```

> **Redis cannot be stopped.** Azure Managed Redis has no start/stop — Balanced B0 (~$13/mo)
> keeps billing even while the App Service and Postgres are stopped. To fully zero staging
> cost when idle, delete the cache (`az redisenterprise delete -g akerogarden-staging -n jmk-ag-staging-redis`)
> and let the next `main.bicep` deploy recreate it (~15 min); otherwise leave it running.

## Gotchas hit during Phase 0 provisioning

- **Key Vault soft-delete.** Deleting a resource group soft-deletes its Key Vault for 7
  days and holds the name globally. Before redeploying after a teardown:
  `az keyvault purge --name jmk-ag-staging-kv --location swedencentral`.
  (If the vault still exists inside an intact RG, there is nothing to purge.)
- **Global-uniqueness naming collisions (Phase 10a).** App Service hostnames, Storage
  Account names, Key Vault names, Azure Managed Redis names, and Postgres Flexible Server
  names are unique across **all of Azure**, not just this subscription. The original
  `akerogarden-staging-*` names collided both with the old MSDN-hosted staging environment
  itself (App Service name reservations can outlive resource-group deletion by longer than
  DNS propagation) and, in the storage account's case, with an unrelated third party's
  account entirely. Fixed by moving every resource name onto the `jmk-ag-<environmentName>`
  prefix (see "Naming" above) instead of `akerogarden-<environmentName>`.
- **Region capacity / quota (MSDN).** North Europe had 0 App Service vCPU quota; West
  Europe restricted PostgreSQL; Sweden Central had transient PostgreSQL
  `CapacityNotAvailable`. Postgres is pinned to `availabilityZone: '1'`; if it fails,
  try zone `'2'`/`'3'`.
- **Key Vault access.** With RBAC disabled the vault must declare an `accessPolicies`
  array (empty is fine), the app uses a **user-assigned** identity created *before* the
  vault, and `keyVaultReferenceIdentity` must point at that identity so
  `@Microsoft.KeyVault(...)` app settings resolve.
- **Custom hostname binding** fails until the `asuid.<host>` TXT + CNAME DNS records
  exist; `publicHostnames` / `adminHostnames` are empty until then. Each hostname's CNAME must
  point at **its own** site (`*-public` vs `*-admin`), not the old single `*-app`.
- **ARM `for`-loop conflict on multi-hostname deploys (hit during Phase 10a's DNS/SSL
  binding).** Deploying a `hostNameBindings` / `certificates` array with **2+ hostnames against the
  same site** in one `az deployment group create` intermittently fails one element with
  `Conflict — Cannot modify this site because another operation is in progress` (a fresh
  operation ID on every retry, so it isn't a transient blip — ARM's parallel `for`-loop
  resource writes race each other on the same underlying site). **Phase 10b's two-app split largely
  dissolves this** — each site now binds a **single** hostname (`publicHostBindings` → the public
  site, `adminHostBindings` → the admin site), so the two bindings target *different* sites and no
  longer race. It can still recur if one site is ever given 2+ hostnames (e.g. prod apex + `www` on
  the public site). **Workaround if it does:** bind the failed hostname/cert directly, bypassing the
  Bicep loop — `az webapp config hostname add -g <rg> --webapp-name <site> --hostname <host>`, or
  `az webapp config ssl bind -g <rg> -n <site> --certificate-thumbprint <thumb> --ssl-type SNI`
  (thumbprint via `az resource show -g <rg> -n <cert-name> --resource-type
  Microsoft.Web/certificates --query properties.thumbprint -o tsv`). End state matches the template;
  only the deploy mechanism differs.
