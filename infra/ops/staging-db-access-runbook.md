# Accessing the staging PostgreSQL database directly

**Purpose:** run an ad-hoc SQL query (e.g. deleting a stray admin-invite row) against the staging database. `jmk-ag-staging-pg` is VNet-integrated with `publicNetworkAccess: Disabled` — this is deliberate isolation (NFR-06/24/25) and cannot be toggled to public access after creation, so there is no way to reach it directly from a laptop or a plain (non-VNet) Azure Cloud Shell.

**Who runs it:** requires Azure Portal access to the `akerogarden-staging` resource group.

---

## Why the obvious routes don't work

- **`psql` from your own machine** — blocked; the server has no public endpoint.
- **Plain Azure Cloud Shell** — runs on Microsoft's shared network, not inside `jmk-ag-staging-vnet`, so it can't resolve the server's private DNS zone (`could not translate host name ... to address`).
- **Temporarily enabling public access** — not possible. A Postgres Flexible Server's networking mode (private/VNet-integrated vs. public) is fixed at creation and cannot be changed afterward. (Confirmed 2026-07-11: `az postgres flexible-server update --public-access Enabled` silently no-ops; the subsequent firewall-rule command errors with `"Firewall rule operations are not supported for a server without public access enabled."`)

## The working route: SSH into the App Service container

`jmk-ag-staging-app` has VNet integration into `app-subnet` in the same VNet as the database's `db-subnet` — it's how the running app reaches Postgres privately today. Its container is therefore already a valid vantage point to run `psql` from.

### Steps

1. **Open the SSH console.**
   In the Azure Portal, go to the `jmk-ag-staging-app` App Service → **Development Tools** → **SSH** (or **Advanced Tools** → **Go** → **SSH** from the Kudu page). This opens a browser-based terminal running inside the app's own container.

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

6. **Close the SSH session.** No Azure resource config was touched, so there's nothing to revert.

## Alternative: reset the whole database via the control plane (no VNet, no SSH)

**When to use:** you want to **drop/reset the entire database** (e.g. the pre-go-live migration wipe left staging's schema out of sync with a fresh EF baseline, so `MigrateAsync` crashes on boot trying to `CREATE TABLE` tables that already exist). This is destructive — staging holds synthetic data only, so that's fine.

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
3. **Restart the App Service.** On boot it runs `MigrateAsync` against the now-empty database and builds the fresh schema; `AdminSeeder` reseeds the admin account from the `Seed:AdminEmail/Password` app settings. First load is slow (~60–90 s: cold start + migrations).

> This wipes **all** staging data. To reset only the *data* while keeping the schema/migrations intact, don't use this — use `DemoSeed:Reset=true` (the app's own reseed path) or the SSH route above for row-level SQL.

## A proper long-term fix (not done yet)

If ad-hoc DB access becomes a regular need, the durable fix is VNet-integrating Cloud Shell (or standing up a small bastion/jumpbox inside `jmk-ag-staging-vnet`) so a real terminal can reach the private endpoint directly, instead of borrowing the app's own container. Not built — flagged here for whoever needs this next.
