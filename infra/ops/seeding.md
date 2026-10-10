# Åkerögården — Demo/seed data (staging & dev)

Everything about seeding and resetting the synthetic demo dataset, for both staging and local dev.

## What the seeder does

A gated startup routine (`DemoDataSeeder`, in `src/AkeroGarden.Web.Core/Services/DemoDataSeeder.cs`,
wired into `src/AkeroGarden.Web.Admin/Program.cs` only — the public app never runs it). On startup,
**only when the config flag `DemoSeed:Reset` is `true`**, it:

1. **Wipes every application table** — all bookings, participants, meals, selections, blocks, and all
   reference data (physical buildings, buildings, rooms, meals, prices, facilities, cleaning zones).
2. **Preserves the admin login** — the ASP.NET Identity users (`AspNetUsers`) are left untouched, so you
   never lose the account you sign in with.
3. **Reseeds** the real Åkerögården property layout and a synthetic Swedish demo dataset: 6 physical
   buildings, 8 buildings (Blidö/Ljusterö/Singö/Tjockö/Vätö 1/Vätö 2/Smedjan/Tallstugan) with 28 rooms
   (79 beds total); 10 meal options; 5 price rules; 5 facilities; 7 cleaning zones incl. the "Hela
   lägret" bundle; 3 blocks; and one booking in every lifecycle state (Pending/AwaitingDeposit/
   Approved/DetailsComplete/Active/Completed/Denied/Cancelled/Closed). Two stay requests are
   Pending, so both ways of saying yes can be tried: one with no building yet (for *Begär
   handpenning*), one with a building assigned and check-in inside 40 days (for *Godkänn*, with a
   link). Three bookings wait for their deposit (FR-115): one on time, and two **overdue and not yet
   reminded** — so the next nightly run sends deposit reminder emails (FR-116) to their `example.com`
   addresses, the same way the seeded link-issuance booking demonstrates that job. Of the waiting
   bookings, two carry an estimated price and the overdue one without a price shows the wording for a
   deposit asked before prices were entered (§5.6). One directly approved booking also has a price;
   the remaining approved and later bookings deliberately have none.

It runs **every time the app starts while the flag is on** — that's why you remove the flag after seeding.

## Why it works this way

The staging PostgreSQL is **VNet-only** (no public internet access), so you can't run SQL against it from
a laptop. The admin App Service is the only thing inside the VNet that both reaches the DB and runs EF
migrations — so the wipe + seed is done *by that app itself* at startup, toggled by a config flag. No
public reset endpoint exists.

**Two-app topology:** staging runs two separate App Services — `jmk-ag-staging-admin` and
`jmk-ag-staging-public`. Only the admin app migrates the DB and wires `DemoDataSeeder`; setting the flag
on the public app does nothing.

## Safety

- The flag is **never set in production** (not in the prod parameters/Bicep), so this can't fire there.
- In source it defaults to `false`. It's turned on **deliberately and temporarily**, then removed.
- **Footgun:** if you leave `DemoSeed__Reset=true` on staging, the DB is wiped + reseeded on *every*
  restart (deploy, platform recycle, idle power-cycle) — which would erase anything created live since.
  Always delete the setting after seeding.

## Re-seed — Staging

The seeder ships on `main`, so any deploy from the "Deploy — Staging" Azure Pipeline already has it —
no special branch needed.

Resource group `akerogarden-staging`, admin app `jmk-ag-staging-admin` (az CLI logged in; mgmt-plane).

```bash
# 1. Turn the flag on, on the ADMIN app only — this AUTO-RESTARTS it, which wipes + reseeds on boot.
az webapp config appsettings set -g akerogarden-staging -n jmk-ag-staging-admin \
  --settings DemoSeed__Reset=true

# 2. Wait ~1 min, then VERIFY AGAINST THE DATA (not just health — see gotcha below):
#    the public facilities page should list a seeded facility. Staging is locked by default, so
#    allow your IP on the public site first (infra/ops/staging-access-runbook.md) or this gets a 403.
curl -s https://staging.akerogarden.se/faciliteter | grep -o "Kapellet"

# 3. Remove the flag so no future restart re-wipes.
az webapp config appsettings delete -g akerogarden-staging -n jmk-ag-staging-admin \
  --setting-names DemoSeed__Reset
```

## Re-seed — Dev

1. Wipe the Postgres volume completely (stops containers and deletes the named volume, so it's a truly
   fresh DB, not just an empty schema):

   ```bash
   docker compose down -v
   docker compose up -d
   ```

2. Turn on demo seeding for the next run:

   ```bash
   cd src/AkeroGarden.Web.Admin
   dotnet user-secrets set "DemoSeed:Reset" "true"
   ```

3. Run the app — this runs EF migrations against the now-empty DB, seeds the admin account, then
   wipes+reseeds the demo dataset:

   ```bash
   dotnet run
   ```

   Watch the startup log for the "Demo data seeded: ..." line — that confirms it worked.

4. Turn seeding back off so a later restart during manual testing doesn't silently re-wipe your
   in-progress test data:

   ```bash
   dotnet user-secrets remove "DemoSeed:Reset"
   ```

**Timing gotcha:** immediately after step 1 (staging) or `dotnet run` (dev), the app can still return
`200` from the *previous* process/container (serving the old, un-seeded data) while the new one boots and
seeds. Don't trust a bare health check — confirm the **seeded data** is actually present before assuming
it worked.

## If staging is powered down

Start **PostgreSQL first**, wait until Ready, **then both apps** (the admin app runs EF migrations on
boot, and the public app depends on the schema it creates):

```bash
az postgres flexible-server start -g akerogarden-staging -n jmk-ag-staging-pg   # wait until Ready
az webapp start -g akerogarden-staging -n jmk-ag-staging-admin
az webapp start -g akerogarden-staging -n jmk-ag-staging-public
```

Power down when idle: `az webapp stop …` (both apps) + `az postgres flexible-server stop …`.
