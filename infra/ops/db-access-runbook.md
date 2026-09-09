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

*(Production names confirmed live at Phase 10c provisioning, 2026-07-21.)*

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
   App settings are exposed as env vars with `__` for nesting. Key Vault references are resolved by
   App Service before the process sees them, so this prints the real value even though the app
   setting is a `@Microsoft.KeyVault(...)` reference:
   ```bash
   echo $ConnectionStrings__Postgres | sed -E 's/Password=[^;]*/Password=<hidden>/'
   ```
   Prints an Npgsql-format string: `Host=...;Database=...;Username=...;Password=...;SslMode=Require`.

   > On production the un-masked form prints a **live credential** to the terminal. Don't screenshot
   > it, and close the session when done.

   > ⚠️ **The two sites now use DIFFERENT database accounts** (post-launch security review #4,
   > 2026-09-09). The **admin** site connects as `akerogarden_admin`, the owner — it runs the EF
   > migrations, so it needs DDL. The **public** site connects as `akerogarden_public`, which can read
   > and write rows but **cannot create or alter tables**. SSH into the **admin** site for anything
   > schema-related; from the public container you will get `permission denied` on any DDL and it will
   > look like a broken database rather than a deliberate restriction.

4. **Connect.** Pass the password via `PGPASSWORD` rather than putting it on the command line — real
   passwords here contain `!` and `$`, which bash will try to interpret inside double quotes and which
   otherwise land in your shell history:
   ```bash
   PGPASSWORD=$(printf '%s' "$ConnectionStrings__Postgres" | sed -n 's/.*Password=\([^;]*\).*/\1/p') \
   psql "host=<Host> dbname=<Database> user=<Username> sslmode=require"
   ```
   That takes the credential straight from the app's own environment — no copy-paste, no quoting
   problems. If you must paste a password by hand, use `set +H` first (turns off history expansion)
   and `read -rs` to take it without echoing.

5. **Run your query**, e.g.:
   ```sql
   SELECT "Id", "Email" FROM "AspNetUsers" WHERE "Email" = 'someone@example.com';
   DELETE FROM "AspNetUsers" WHERE "Email" = 'someone@example.com';
   ```
   *(`AspNetUsers` is admin-account infrastructure, not guest data — which is why it's the example here. Guest tables carry PII; see the warning at the top.)*

6. **Close the SSH session.** No Azure resource config was touched, so there's nothing to revert.

## Alternative: reset the whole database via the control plane (no VNet, no SSH)

> ## 🛑 STAGING ONLY once the system is live — and it is a one-time exception before then
>
> This **drops the entire database**. On staging that is fine: synthetic data, rebuilt on next boot. On production it destroys every booking, participant, meal selection and audit record, and the only way back is a point-in-time restore (`restore-drill-runbook.md`) with data loss between the restore point and the drop.
>
> This route exists to fix one specific problem: a database holding a schema that a **regenerated** baseline migration cannot apply over. `MigrateAsync` sees an unapplied migration, issues `CREATE TABLE` against tables that already exist, and the container dies on the 230-second startup timeout.
>
> **It was run against production once, deliberately, on the demo-feedback slice 8 (2026-08-10)** — before go-live, with no real guest data anywhere, when the final baseline regeneration made production's 10c-era schema unappliable. That was the last time. **After the Phase 10d cutover, production holds real guest data and this section must never be run against it again**; the baseline is never regenerated again either, so the situation cannot recur. If you believe live production needs a database reset, stop and treat it as an incident, not a runbook step.
>
> **An earlier version of this box said the reset was "not needed on production" because production is provisioned empty at 10c.** That was wrong from the moment 10c deployed: 10c migrated production, so it *did* hold a pre-baseline schema. What decides whether a drop is needed is the **schema state of the target database**, never which environment it is.

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
3. **Restart the App Service.** On boot it runs `MigrateAsync` against the now-empty database and builds the fresh schema; `AdminSeeder` creates both roles (`Administratör`, `Samordnare` — FR-104) and reseeds the admin account from the `Seed:AdminEmail/Password` app settings, holding `Administratör`. Any account left without a role is adopted into `Administratör` on the same pass, so a database restored from before FR-104 does not lock its admins out. First load is slow (~60–90 s: cold start + migrations).

> This wipes **all** staging data. To reset only the *data* while keeping the schema/migrations intact, don't use this — use `DemoSeed:Reset=true` (the app's own reseed path) or the SSH route above for row-level SQL.
>
> **When the reset is for a regenerated baseline, the order above is not enough.** The three steps assume the deployed build already matches the schema you want built. After a baseline regeneration it does not — the new build only reaches staging once the PR is merged and the auto-deploy runs. Sequence it as: **stop both apps → merge → wait for the deploy pipeline to go green → then** drop and recreate → start admin → start public. Stopping first is what stops the *old* build booting against the empty database and rebuilding the old schema; dropping only after a green deploy is what keeps a failed deploy from leaving you with an empty database and nothing but the old build to start. `azure-pipelines-deploy-staging.yml` has no start step, so the sites stay stopped through the deploy and you start them yourself.

## The one-time production rebuild (demo-feedback slice 8, 2026-08-10)

> Recorded because it was run, not because it should be run again. See the stop box above: after the Phase 10d cutover this must never be repeated.

Production could not be reseeded like staging — `DemoSeed:Reset` is deliberately absent there, and the reference data is the client's own, entered by hand during Phase 10c. It is also not recreated by anything on boot: `AdminSeeder` restores the two roles and the bootstrap admin account, and nothing else. So the client's buildings, rooms, meal options, price rules, facilities and cleaning zones had to be carried across the rebuild by hand.

**Why that is safe rather than clever:** none of the four demo-feedback migrations touched a single reference-data table. They changed `Bookings`, `Participants`, the meal-selection table and the new assignment table only. The seven reference tables are byte-identical before and after, so copying the rows out and back is a plain restore, not a transformation.

**Step 1 — copy the reference data out, before anything destructive.** SSH into the production admin container (the route at the top of this file) and dump the seven tables to `/home`, which is persistent storage and survives both the deploy and container restarts:

```bash
apt-get update && apt-get install -y postgresql-client   # if pg_dump is missing
# read the connection string, then, in libpq form:
pg_dump "host=<Host> dbname=<Database> user=<Username> password=<Password> sslmode=require" \
  --data-only --column-inserts \
  -t '"PhysicalBuildings"' -t '"Buildings"' -t '"Rooms"' \
  -t '"MealOptions"' -t '"Facilities"' -t '"PriceRules"' -t '"CleaningZones"' \
  > /home/refdata.sql
grep -c "^INSERT" /home/refdata.sql
```

Check the `INSERT` count against what the admin panel shows — 6 physical buildings, 8 buildings, 28 rooms, 10 meal options, 5 price rules, 5 facilities, 7 cleaning zones at the time this was run. **This is the step with no second chance.** Download a copy off the container too (Advanced Tools → Go → the file browser under `/home`); `/home` surviving the deploy is the documented behaviour, not something worth betting the client's data-entry on.

`--column-inserts` is deliberate: it names every column in each statement, so the restore does not depend on column ordering matching between the old schema and the regenerated one.

**Step 2 — stop both apps** (`az webapp stop` on `jmk-ag-prod-admin` and `jmk-ag-prod-public`). Skipping this lets the old build boot against the empty database and rebuild the old schema, putting you back where you started. **Neither deploy pipeline starts an App Service** — there is no start step in `azure-pipelines-deploy-prod.yml` — so the sites stay stopped through the deploy and starting them is a deliberate act.

**Step 3 — deploy the build carrying the regenerated baseline** (run the production pipeline and approve it). **Wait until the pipeline is green before going further.**

**Step 4 — drop and recreate the database**, same two `az postgres flexible-server db delete` / `create` commands as the staging section, against `-g akerogarden-prod --server-name jmk-ag-prod-pg`.

> **The drop comes after the deploy, and the order is not interchangeable.** If it ran first and the deploy then failed, the only build available to start would be the old one, and starting it against the empty database would silently rebuild the old schema — leaving you apparently working, on the wrong schema, with the reference data already dumped. Dropping last means a failed deploy costs nothing: the database is untouched and you simply retry.

**Step 5 — start the admin app.** Watch the log stream, not the URL: you want the migration to run and a normal startup. **The failure mode is the container dying at ~230 seconds with a "relation already exists" error**, which means step 4 did not take.

**Step 6 — restore the reference data.** SSH back in and:

```bash
psql "host=... sslmode=require" -v ON_ERROR_STOP=1 -f /home/refdata.sql
```

`ON_ERROR_STOP=1` matters — without it psql reports success having skipped failed statements. **The failure mode to expect is a foreign-key violation**, which means the tables restored in the wrong order (a room before its building). Fix by restoring the parents first — physical buildings, then buildings, then rooms — rather than by dropping constraints.

**Step 7 — start the public app, then verify against the data, not against a health check.** The admin panel lists the buildings and rooms again, and the public facilities page renders the client's facilities. A `200` from either site proves only that the app booted.

## A proper long-term fix (not done yet)

If ad-hoc DB access becomes a regular need, the durable fix is VNet-integrating Cloud Shell (or standing up a small bastion/jumpbox inside the environment's VNet) so a real terminal can reach the private endpoint directly, instead of borrowing the app's own container. Not built — flagged here for whoever needs this next. This matters more once production is live: borrowing the app container is a fine story for rare staging work, but a weak one for touching real guest data.
