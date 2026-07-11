# Accessing the staging PostgreSQL database directly

**Purpose:** run an ad-hoc SQL query (e.g. deleting a stray admin-invite row) against the staging database. `akerogarden-staging-pg` is VNet-integrated with `publicNetworkAccess: Disabled` — this is deliberate isolation (NFR-06/24/25) and cannot be toggled to public access after creation, so there is no way to reach it directly from a laptop or a plain (non-VNet) Azure Cloud Shell.

**Who runs it:** requires Azure Portal access to the `akerogarden-staging` resource group.

---

## Why the obvious routes don't work

- **`psql` from your own machine** — blocked; the server has no public endpoint.
- **Plain Azure Cloud Shell** — runs on Microsoft's shared network, not inside `akerogarden-staging-vnet`, so it can't resolve the server's private DNS zone (`could not translate host name ... to address`).
- **Temporarily enabling public access** — not possible. A Postgres Flexible Server's networking mode (private/VNet-integrated vs. public) is fixed at creation and cannot be changed afterward. (Confirmed 2026-07-11: `az postgres flexible-server update --public-access Enabled` silently no-ops; the subsequent firewall-rule command errors with `"Firewall rule operations are not supported for a server without public access enabled."`)

## The working route: SSH into the App Service container

`akerogarden-staging-app` has VNet integration into `app-subnet` in the same VNet as the database's `db-subnet` — it's how the running app reaches Postgres privately today. Its container is therefore already a valid vantage point to run `psql` from.

### Steps

1. **Open the SSH console.**
   In the Azure Portal, go to the `akerogarden-staging-app` App Service → **Development Tools** → **SSH** (or **Advanced Tools** → **Go** → **SSH** from the Kudu page). This opens a browser-based terminal running inside the app's own container.

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

## A proper long-term fix (not done yet)

If ad-hoc DB access becomes a regular need, the durable fix is VNet-integrating Cloud Shell (or standing up a small bastion/jumpbox inside `akerogarden-staging-vnet`) so a real terminal can reach the private endpoint directly, instead of borrowing the app's own container. Not built — flagged here for whoever needs this next.
