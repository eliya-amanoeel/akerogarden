# Infrastructure

Bicep templates for Åkerögården. Both production and staging deploy from the same
`main.bicep` with different parameter files (`staging.parameters.json`,
`prod.parameters.json`).

> **Status:** staging is provisioned in **Sweden Central** (`akerogarden-staging`), currently in the developer's personal (MSDN) subscription.
> **Phase 10a** recreates staging fresh in the **client's own Azure subscription/tenant** (a new PAYG subscription) with a real subdomain split — `staging.akerogarden.se` (public) + `admin.staging.akerogarden.se` (admin) — instead of today's Azure default hostname; the old MSDN-hosted copy is deleted once the new one is verified. This is a redeploy from these same Bicep templates, not a migration — Azure's resource-move feature doesn't work across tenants.
> Production is provisioned directly in the client's tenant in **Phase 10b**.

## What `main.bicep` creates

User-assigned managed identity, Key Vault (access-policy model, **not** RBAC) with
the SMTP secrets, App Service Plan + App Service (Linux, .NET 10), PostgreSQL
Flexible Server (VNet-only, `publicNetworkAccess: Disabled`, zone 1), VNet with
delegated app/db subnets, private DNS zone for Postgres, and Blob storage.

Redis (Azure Managed Redis, Balanced B0 — the successor to the retired classic Azure
Cache for Redis) backs the inquiry rate limiter (NFR-04); its connection string is stored
in Key Vault and read by the app via managed identity.

## Naming

The resource-group name stays `akerogarden-staging` / `akerogarden-prod`, but every resource
*inside* it is named from a `jmk-ag-<environmentName>` prefix (`jmk-ag-staging-app`,
`jmk-ag-staging-kv`, etc. — `jmk` = Jungfru Maria Kyrkan, the church that owns the project;
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

## 2. Wire the GitHub deploy pipeline (one-time, manual)

The `Deploy — Staging` workflow deploys on CI success on `main`. It needs a GitHub
**environment** named `staging` with one variable and one secret:

```bash
# Create the 'staging' environment first (GitHub UI: Settings → Environments → New),
# or via API:
gh api -X PUT repos/<owner>/<repo>/environments/staging --silent

# App name (variable)
gh variable set AZURE_APP_NAME --env staging --body "jmk-ag-staging-app"

# Publish profile (secret) — piped straight from Azure, never written to disk
az webapp deployment list-publishing-profiles \
  --resource-group akerogarden-staging \
  --name jmk-ag-staging-app \
  --xml | gh secret set AZURE_PUBLISH_PROFILE --env staging
```

Publish-profile (basic-auth) deploys must be allowed on the App Service — they are by
default; confirm with:

```bash
az resource show -g akerogarden-staging --namespace Microsoft.Web \
  --resource-type basicPublishingCredentialsPolicies --name scm \
  --parent sites/jmk-ag-staging-app --query "properties.allow" -o tsv  # expect: true
```

## 3. First deploy

Merging to `main` triggers CI → `Deploy — Staging`. The app applies EF migrations and
seeds the admin **on startup, inside the VNet** (the VNet-only DB is unreachable from
CI runners, so migrations cannot run from the pipeline — only from the running app).

## Recreate staging in the client's tenant (Phase 10a)

Same steps as "Provision staging" above, but targeting the client's subscription: switch
context first (`az login --tenant <client-tenant-id>`, `az account set --subscription
<client-sub-id>`), then deploy with `staging.parameters.json` updated so `customHostnames`
binds `staging.akerogarden.se` + `admin.staging.akerogarden.se`, and re-run steps 2–3
(regenerate `AZURE_PUBLISH_PROFILE` from the new App Service, re-verify the first deploy).
Once verified, delete the old `akerogarden-staging` resource group in the MSDN subscription.

## Deploy production (Phase 10b)

Same as staging with `prod.parameters.json` and a `akerogarden-prod` resource group, in the
client's tenant. Production also binds the custom domains (`akerogarden.se`,
`admin.akerogarden.se`) + managed SSL via `ssl.bicep`, after DNS is configured. Staging's own
custom subdomains are bound separately in Phase 10a. See `ssl.bicep` and the Phase 10a/10b plan.

## Power staging down (save cost)

```bash
az webapp stop --resource-group akerogarden-staging --name jmk-ag-staging-app
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
  exist; `customHostnames` is empty for staging until then (deferred to Phase 10a).
