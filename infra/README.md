# Infrastructure

Bicep templates for Åkerögården. Both production and staging deploy from the same
`main.bicep` with different parameter files (`staging.parameters.json`,
`prod.parameters.json`).

> **Status:** staging is provisioned in **Sweden Central** (`akerogarden-staging`).
> Production is deferred to Phase 6.

## What `main.bicep` creates

User-assigned managed identity, Key Vault (access-policy model, **not** RBAC) with
the SMTP secrets, App Service Plan + App Service (Linux, .NET 10), PostgreSQL
Flexible Server (VNet-only, `publicNetworkAccess: Disabled`, zone 1), VNet with
delegated app/db subnets, private DNS zone for Postgres, and Blob storage.

Redis (Azure Managed Redis, Balanced B0 — the successor to the retired classic Azure
Cache for Redis) backs the inquiry rate limiter (NFR-04); its connection string is stored
in Key Vault and read by the app via managed identity.

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
az postgres flexible-server show -g akerogarden-staging -n akerogarden-staging-pg \
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
gh variable set AZURE_APP_NAME --env staging --body "akerogarden-staging-app"

# Publish profile (secret) — piped straight from Azure, never written to disk
az webapp deployment list-publishing-profiles \
  --resource-group akerogarden-staging \
  --name akerogarden-staging-app \
  --xml | gh secret set AZURE_PUBLISH_PROFILE --env staging
```

Publish-profile (basic-auth) deploys must be allowed on the App Service — they are by
default; confirm with:

```bash
az resource show -g akerogarden-staging --namespace Microsoft.Web \
  --resource-type basicPublishingCredentialsPolicies --name scm \
  --parent sites/akerogarden-staging-app --query "properties.allow" -o tsv  # expect: true
```

## 3. First deploy

Merging to `main` triggers CI → `Deploy — Staging`. The app applies EF migrations and
seeds the admin **on startup, inside the VNet** (the VNet-only DB is unreachable from
CI runners, so migrations cannot run from the pipeline — only from the running app).

## Deploy production (Phase 6)

Same as staging with `prod.parameters.json` and a `akerogarden-prod` resource group.
Production also binds the custom domains (`akerogarden.se`, `admin.akerogarden.se`) and
the staging custom subdomain (`staging.akerogarden.se`) + managed SSL via `ssl.bicep`,
after DNS is configured. See `ssl.bicep` and the Phase 6 plan.

## Power staging down (save cost)

```bash
az webapp stop --resource-group akerogarden-staging --name akerogarden-staging-app
az postgres flexible-server stop --resource-group akerogarden-staging --name akerogarden-staging-pg
```

> **Redis cannot be stopped.** Azure Managed Redis has no start/stop — Balanced B0 (~$45/mo)
> keeps billing even while the App Service and Postgres are stopped. To fully zero staging
> cost when idle, delete the cache (`az redisenterprise delete -g akerogarden-staging -n akerogarden-staging-redis`)
> and let the next `main.bicep` deploy recreate it (~15 min); otherwise leave it running.

## Gotchas hit during Phase 0 provisioning

- **Key Vault soft-delete.** Deleting a resource group soft-deletes its Key Vault for 7
  days and holds the name globally. Before redeploying after a teardown:
  `az keyvault purge --name akerogarden-staging-kv --location swedencentral`.
  (If the vault still exists inside an intact RG, there is nothing to purge.)
- **Region capacity / quota (MSDN).** North Europe had 0 App Service vCPU quota; West
  Europe restricted PostgreSQL; Sweden Central had transient PostgreSQL
  `CapacityNotAvailable`. Postgres is pinned to `availabilityZone: '1'`; if it fails,
  try zone `'2'`/`'3'`.
- **Key Vault access.** With RBAC disabled the vault must declare an `accessPolicies`
  array (empty is fine), the app uses a **user-assigned** identity created *before* the
  vault, and `keyVaultReferenceIdentity` must point at that identity so
  `@Microsoft.KeyVault(...)` app settings resolve.
- **Custom hostname binding** fails until the `asuid.<host>` TXT + CNAME DNS records
  exist; `customHostnames` is empty for staging until then (deferred to Phase 6).
