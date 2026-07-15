# PostgreSQL Backup Verification + Restore Drill (NFR-12 / NFR-29)

**Purpose:** prove that (a) automated backups exist for the PostgreSQL Flexible Server (**NFR-12**) and (b) they are actually restorable via a point-in-time restore (**NFR-29**).
**Applies to both environments** — set the variables in Step 0 for staging *or* production. **Read the Staging-vs-Production safety box first — one step is unsafe on production.**
**Who runs it:** requires Azure CLI logged in with rights on the target resource group. (Claude drafted this; you execute the `az` commands.)

---

## ⚠️ Staging vs Production — read before running

|  | Staging | Production |
|---|---|---|
| Data in the restored copy | Synthetic | **Real guest PII** |
| Verification | **Path 2 only** (inside the VNet) | **Path 2 only** (inside the VNet) |
| Part D power-down of the *source* server | Yes (staging idles) | **No** — never stop the live prod server |
| Delete the throwaway restore | Yes | **Yes, promptly** — it holds a full copy of real PII |

The restore always creates a **new, independent** server — it never touches the source. The risk is not the restore; it's how you *reach* the restored copy to verify it.

> **Path 2 (inside the VNet) is now the only verification path — for staging as well as production.** The former "Path 1" (temporarily enable a public endpoint on the throwaway server) **does not work**: a point-in-time restore of a VNet-injected server produces another **VNet-injected, private-only** server, and that networking mode is fixed at creation — `az postgres flexible-server update --public-network-access Enabled` silently no-ops on it (same immutability confirmed in `staging-db-access-runbook.md`). So there is no public-endpoint shortcut even on staging.

---

## Step 0 — Set variables for the target environment

```bash
# ── STAGING ──
RG=akerogarden-staging;  SRC=akerogarden-staging-pg;  VNET=akerogarden-staging-vnet

# ── PRODUCTION (uncomment; do NOT use Path 1 below) ──
# RG=akerogarden-prod;   SRC=akerogarden-prod-pg;     VNET=akerogarden-prod-vnet

DB=akerogarden
DRILL=${SRC}-drill        # throwaway restored server — deleted in Part D
```

---

## Part A — Verify backups exist (NFR-12)

```bash
# Confirm the server is up (if staging was powered down, start PG FIRST and wait until Ready).
az postgres flexible-server show -g $RG -n $SRC \
  --query "{state:state, retentionDays:backup.backupRetentionDays, geo:backup.geoRedundantBackup, earliestRestore:backup.earliestRestoreDate}" -o table
```

**PASS (NFR-12) when:** `retentionDays` ≥ 7 and `earliestRestore` is a real timestamp inside that window (proves a rolling backup exists). Record the `earliestRestore` value.

---

## Part B — Restore drill (NFR-29)

```bash
# Restore time a few minutes in the past, inside the retention window (UTC, ISO-8601, >= earliestRestore).
RESTORE_TIME=$(date -u -v-15M +%Y-%m-%dT%H:%M:%SZ)   # macOS date syntax
echo "Restoring $SRC as of $RESTORE_TIME -> $DRILL"

# Point-in-time restore. A VNet-injected source restores into the SAME networking
# (same delegated subnet + private DNS zone) by default — no extra flags needed.
# (The db-subnet can host more than one flexible server as long as it has free IPs. If Azure
#  rejects the shared subnet, create a second delegated subnet — see the note at the bottom.)
az postgres flexible-server restore \
  -g $RG \
  --name $DRILL \
  --source-server $SRC \
  --restore-time "$RESTORE_TIME"
```

Restore takes ~5–15 min. When it returns, the drill server exists and is private-only (same as the source).

### Verify the restored data — Path 2 (inside the VNet), staging and production alike

The drill server is private-only (see the safety box), so verify it from **inside the VNet**, via the App Service SSH console — the same mechanism as `staging-db-access-runbook.md` (SSH into the running app container, install `psql` if missing, connect over the private FQDN).

1. Portal → the app in the same VNet (`akerogarden-staging-app` / prod equivalent) → **Development Tools → SSH**.
2. `psql --version` — if missing: `apt-get update && apt-get install -y postgresql-client`.
3. Connect to the **drill** server on its private endpoint and run the row-count checks:
   ```bash
   psql "host=$DRILL.private.postgres.database.azure.com dbname=akerogarden user=akerogarden_admin password='<postgresAdminPassword>' sslmode=require" \
     -c "select count(*) as bookings from \"Bookings\";" \
     -c "select max(\"CreatedAt\") as latest_booking from \"Bookings\";" \
     -c "select count(*) as participants from \"Participants\";"
   ```
   (Alternatively, a one-off `mcr.microsoft.com/azure-cli` container joined to the VNet's app-subnet.)

**PASS (NFR-29) when:** the restored server accepts a connection and the row counts / latest `CreatedAt` match what the source held at `RESTORE_TIME` (i.e. real data came back, not an empty schema).

---

## Part C — (optional) app-level smoke against the restore

Only for extra confidence: temporarily point the app's `ConnectionStrings__Postgres` at `$DRILL` (private FQDN), restart, load `/admin/bokningar`, confirm bookings render — then **revert the connection string** to `$SRC` and restart. Skip unless the Path 2 row counts leave doubt. *(On production, weigh the disruption — usually the row counts are enough.)*

---

## Part D — Cleanup (do not skip — cost + PII posture)

```bash
# Delete the throwaway restored server (mandatory — on prod it holds real PII).
az postgres flexible-server delete -g $RG -n $DRILL --yes

# The drill is VNet-only (no public endpoint was ever opened), so it dies with this one command —
# nothing else to undo.
```

**Staging only:** if idle, power down (PG last): `az webapp stop ...` then `az postgres flexible-server stop -g $RG -n $SRC`.
**Production:** do **not** stop the source server.

---

## Record the result

Log the drill outcome (env, date, `RESTORE_TIME`, row counts matched Y/N). For **production** this is a Phase-9 go-live exit criterion ("backups proven restorable"); for **staging** it's an optional pre-go-live rehearsal.

### Note — if the shared subnet is rejected
Some regions/tooling won't inject a second flexible server into an in-use delegated subnet. If `az ... restore` fails on networking, create a second delegated subnet in `$VNET` (delegation `Microsoft.DBforPostgreSQL/flexibleServers`) and a matching private DNS zone, then pass `--subnet <id> --private-dns-zone <id>` to the restore. Delete both after Part D.
