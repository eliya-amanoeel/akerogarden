# Accessing the PostgreSQL database directly (staging or production)

**Purpose:** run an ad-hoc SQL query (e.g. deleting a stray admin-invite row) against the staging **or** production database. Both servers are VNet-integrated with `publicNetworkAccess: Disabled` — this is deliberate isolation (NFR-06/24/25) and cannot be toggled to public access after creation, so there is no way to reach either directly from a laptop or a plain (non-VNet) Azure Cloud Shell.

**Who runs it:** requires Azure Portal access to the relevant resource group.

---

## ⚠️ Production is not staging — read this first

The mechanics below are identical in both environments. The **consequences are not**:

- **Production holds real guest PII** — names, emails, phone numbers, and article-9 dietary data. Ad-hoc SQL against production is a data-protection action, not a routine dev task. Query the narrowest thing that answers your question, never `SELECT *` a participant table onto your screen "to look around", and don't paste result sets into tickets, chat, or an AI assistant.
- **Direct DB access bypasses the audit trail.** The `AuditLog` table (§6.9, FR-95–98) records changes made *through the application*. A `DELETE` you run here is invisible to it. If you mutate production data directly, write down what you did and why — that note is the only record that will exist.
- **The database-reset route in the last section must never be run against production.** See the stop box there.

Prefer, in order: fix it through the admin UI → fix it through the app's own reseed/maintenance paths → ad-hoc `SELECT` here → ad-hoc mutation here as a last resort.

---

## Step 0 — Pick your environment

Everything below uses these names. Substitute per environment:

| | Staging | Production |
|---|---|---|
| Resource group | `akerogarden-staging` | `akerogarden-prod` |
| Postgres server | `jmk-ag-staging-pg` | `jmk-ag-prod-pg` |
| Admin App Service | `jmk-ag-staging-admin` | `jmk-ag-prod-admin` |
| Public App Service | `jmk-ag-staging-public` | `jmk-ag-prod-public` |
| VNet | `jmk-ag-staging-vnet` | `jmk-ag-prod-vnet` |
| Database name | `akerogarden` | `akerogarden` |
| Data | Synthetic — destructive ops are cheap | **Real guest PII — destructive ops are not recoverable without a restore** |

*(Production names follow the staging convention and should be confirmed against `prod.parameters.json` at Phase 10c provisioning; if they differ there, that file wins.)*

## Why the obvious routes don't work

- **`psql` from your own machine** — blocked; neither server has a public endpoint.
- **Plain Azure Cloud Shell** — runs on Microsoft's shared network, not inside the environment's VNet, so it can't resolve the server's private DNS zone (`could not translate host name ... to address`).
- **Temporarily enabling public access** — not possible. A Postgres Flexible Server's networking mode (private/VNet-integrated vs. public) is fixed at creation and cannot be changed afterward. (Confirmed 2026-07-11 on staging: `az postgres flexible-server update --public-access Enabled` silently no-ops; the subsequent firewall-rule command errors with `"Firewall rule operations are not supported for a server without public access enabled."`) The same immutability is what forces Path 2 in `restore-drill-runbook.md`.

## The working route: SSH into the App Service container

Both sites in an environment have VNet integration into `app-subnet`, in the same VNet as the database's `db-subnet`; it's how the running apps reach Postgres privately. Either container is therefore a valid vantage point to run `psql` from. **Use the admin site** below: it is the site that owns the EF migrations and the daily background job, so it is the one whose DB connectivity you actually want to confirm.

### Steps

1. **Open the SSH console.**
   In the Azure Portal, go to the admin App Service → **Development Tools** → **SSH** (or **Advanced Tools** → **Go** → **SSH** from the Kudu page). This opens a browser-based terminal running inside the app's own container.

2. **Check for `psql`.**
   ```bash
   psql --version
   ```
   If missing, install it (the container is Debian-based):
   ```bash
   apt-get update && apt-get install -y postgresql-client
   ```

3. **Read the DB connection string from the environment.**
   App settings are exposed as env vars with `__` for nesting:
   ```bash
   echo $ConnectionStrings__Postgres
   ```
   Prints an Npgsql-format string: `Host=...;Database=...;Username=...;Password=...;SslMode=Require`.

   > On production this prints a **live credential** to the terminal. Don't screenshot it, and close the session when done.

4. **Convert to `psql` (libpq) format and connect.**
   `psql` wants lowercase, space-separated `key=value` pairs, not semicolons:
   ```bash
   psql "host=<Host> dbname=<Database> user=<Username> password=<Password> sslmode=require"
   ```

5. **Run your query**, e.g.:
   ```sql
   SELECT "Id", "Email" FROM "AspNetUsers" WHERE "Email" = 'someone@example.com';
   DELETE FROM "AspNetUsers" WHERE "Email" = 'someone@example.com';
   ```
   *(`AspNetUsers` is admin-account infrastructure, not guest data — which is why it's the example here. Guest tables carry PII; see the warning at the top.)*

6. **Close the SSH session.** No Azure resource config was touched, so there's nothing to revert.

## Alternative: reset the whole database via the control plane (no VNet, no SSH)

> ## 🛑 STAGING ONLY — NEVER RUN THIS AGAINST PRODUCTION
>
> This **drops the entire database**. On staging that is fine: synthetic data, rebuilt on next boot. On production it destroys every booking, participant, meal preference and audit record, and the only way back is a point-in-time restore (`restore-drill-runbook.md`) with data loss between the restore point and the drop.
>
> **It is also not needed on production.** This route exists to fix one specific problem: a database holding a *pre-baseline* schema that the consolidated `20260713155158_InitialSchema` migration cannot apply over. Production is provisioned empty at Phase 10c, so its first deploy builds the fresh schema cleanly and this situation cannot arise. If you believe production needs a database reset, stop and treat it as an incident, not a runbook step.

**When to use (staging):** you want to **drop/reset the entire database** — e.g. the pre-go-live migration wipe left staging's schema out of sync with the fresh EF baseline, so `MigrateAsync` crashed on boot trying to `CREATE TABLE` tables that already existed (this happened 2026-07-13, fixed 2026-07-14).

**Why this beats the SSH route here:** deleting/creating a database is an **Azure management-plane** operation (it hits the ARM API, not the database over the network), so the VNet-only restriction that blocks `psql` does **not** apply — it runs from a plain Azure Cloud Shell / any `az login`. Crucially, it also works when the **app container is crash-looping** (which the SSH route can't: the browser SSH needs a *running* container to attach to, and a boot-crashing app never gives you one — you get a red "SSH CONNECTION CLOSE").

### Steps

1. Open **Cloud Shell** (portal top bar, or the PostgreSQL resource's CLI/PS option) — no VNet integration needed.
2. Drop and recreate the (empty) database. The database name is `akerogarden` (see the connection string in `infra/azure/main.bicep`); the parameter is **`--name`/`-n`**, *not* `--database-name`:
   ```bash
   az postgres flexible-server db delete \
     --resource-group akerogarden-staging --server-name jmk-ag-staging-pg \
     --name akerogarden --yes

   az postgres flexible-server db create \
     --resource-group akerogarden-staging --server-name jmk-ag-staging-pg \
     --name akerogarden
   ```
   (In PowerShell, drop the `\` line-continuations and put each command on one line.)
   *(Resource names are hardcoded to staging here deliberately — see the stop box. This is the one section that is **not** environment-parameterised.)*
3. **Restart the App Service.** On boot it runs `MigrateAsync` against the now-empty database and builds the fresh schema; `AdminSeeder` reseeds the admin account from the `Seed:AdminEmail/Password` app settings. First load is slow (~60–90 s: cold start + migrations).

> This wipes **all** staging data. To reset only the *data* while keeping the schema/migrations intact, don't use this — use `DemoSeed:Reset=true` (the app's own reseed path) or the SSH route above for row-level SQL.

## A proper long-term fix (not done yet)

If ad-hoc DB access becomes a regular need, the durable fix is VNet-integrating Cloud Shell (or standing up a small bastion/jumpbox inside the environment's VNet) so a real terminal can reach the private endpoint directly, instead of borrowing the app's own container. Not built — flagged here for whoever needs this next. This matters more once production is live: borrowing the app container is a fine story for rare staging work, but a weak one for touching real guest data.
