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

> **Path 2 (inside the VNet) is now the only verification path — for staging as well as production.** The former "Path 1" (temporarily enable a public endpoint on the throwaway server) **does not work**: a point-in-time restore of a VNet-injected server produces another **VNet-injected, private-only** server, and that networking mode is fixed at creation — `az postgres flexible-server update --public-network-access Enabled` silently no-ops on it (same immutability confirmed in `db-access-runbook.md`). So there is no public-endpoint shortcut even on staging.

---

## Step 0 — Set variables for the target environment

```bash
# ── STAGING ──
RG=akerogarden-staging;  SRC=jmk-ag-staging-pg;  VNET=jmk-ag-staging-vnet

# ── PRODUCTION (uncomment; do NOT use Path 1 below) ──
# RG=akerogarden-prod;   SRC=jmk-ag-prod-pg;     VNET=jmk-ag-prod-vnet

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

The drill server is private-only (see the safety box), so verify it from **inside the VNet**, via the App Service SSH console — the same mechanism as `db-access-runbook.md` (SSH into the running app container, install `psql` if missing, connect over the private FQDN).

1. Portal → an app in the same VNet — `jmk-ag-staging-admin` (or `jmk-ag-prod-admin` on production; both sites are VNet-integrated, admin is the convention) → **Development Tools → SSH**.
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

Only for extra confidence: temporarily point an app's `ConnectionStrings__Postgres` at `$DRILL` (private FQDN), restart, load `/admin/bokningar`, confirm bookings render — then **revert the connection string** to `$SRC` and restart. Skip unless the Path 2 row counts leave doubt. *(On production, weigh the disruption — usually the row counts are enough.)*

> **Use the ADMIN app for this** (post-launch security review #4, 2026-09-09). The two sites now use
> different database accounts, and `/admin/bokningar` is an admin page anyway. A PITR restore creates a
> whole new server and Postgres roles are cluster-level, so both `akerogarden_admin` and
> `akerogarden_public` come across with their existing passwords — the drill server's credentials are
> the same ones already in Key Vault, and no re-granting is needed.

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

NFR-29 asks for a drill **every quarter, with the result documented** — this table is where it is
documented. Add a row for every drill, pass or fail. Production is the one that counts: it holds real
guest data. The throwaway restored server holds that data too, so delete it (Part D) before writing
the row.

| Date | Environment | `RESTORE_TIME` | Row counts matched | Notes |
|---|---|---|---|---|
| 2026-07-21 | Production | not recorded | not recorded | Phase 10c, **before launch — no guest data yet**. Its only record is "restore drill … passed" in the 10c row of `docs/archive/implementation-plan.md` |

| 2026-10-08 | Production | — | — | **FAILED — no restore possible.** Part A passed (35-day retention, earliest restore 2026-09-04). Part B was refused three times with `RegionalAllocationFailed` ("not sufficient capacity for the requested compute size in this region") — `--zone 1`, `2` and `3` alike. Nothing was created. **Drill postponed**; retry with the same commands, zone 1 first, and add a row per attempt |

**The risk this exposed.** A point-in-time restore always creates a server of the **same size as the
source** — `az postgres flexible-server restore` has no size option, only `--zone`. Production is
Burstable `Standard_B1ms` in Sweden Central, and on 2026-10-08 no zone there had capacity for it. **If a
real restore had been needed that day — a bad migration, an accidental delete — it would have failed
the same way.** Shortages like this are usually temporary; if retries keep failing for about a week, treat
it as structural and weigh a larger tier against the risk (the July sizing decision is in
`infra/azure/README.md`, "SKU limitations"). Sweden Central compute, 2026-10-08 list price: B1ms
~$14.5/month, B2s ~$29, General Purpose D2ds_v5 ~$137 — and no size has guaranteed capacity.

**The deposit work was planned to wait for a passing drill** (`docs/archive/deposit-flow-2026-10-07.md` §7),
since it carries a schema change. **That was waived by the maintainer on 2026-10-09**, and the work went to
production without one. Four migrations have now reached production since launch without a passing drill
(`AddOnBehalfOfBooking`, `AddJobRuns`, `AddBookingDeposit`, `AddBookingEstimatedPrice`), all additive — the
drill is no less owed for that; NFR-29 asks for it quarterly regardless.

**Next drill: overdue** — the first against a database holding real guests has not yet succeeded.

### Note — if the shared subnet is rejected
Some regions/tooling won't inject a second flexible server into an in-use delegated subnet. If `az ... restore` fails on networking, create a second delegated subnet in `$VNET` (delegation `Microsoft.DBforPostgreSQL/flexibleServers`) and a matching private DNS zone, then pass `--subnet <id> --private-dns-zone <id>` to the restore. Delete both after Part D.
