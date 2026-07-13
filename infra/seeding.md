# Åkerögården — Demo/seed data (staging & dev)

Extracted from the Phase 8 demo script — everything about seeding and resetting the synthetic
demo dataset, for both staging and local dev.

## What the seeder does

A gated startup routine (`DemoDataSeeder`, in `src/AkeroGarden.Web/Services/DemoDataSeeder.cs`, wired in
`Program.cs`). On app startup, **only when the config flag `DemoSeed:Reset` is `true`**, it:

1. **Wipes every application table** — all bookings, participants, meals, selections, blocks, and all
   reference data (buildings, rooms, meals, prices, facilities, cleaning zones).
2. **Preserves the admin login** — the ASP.NET Identity users (`AspNetUsers`) are left untouched, so you
   never lose the account you sign in with.
3. **Reseeds** the full synthetic Swedish demo dataset: 3 buildings (Huvudbyggnaden · Annexet · Sjöstugan)
   + 12 rooms; 6 meal options; 6 price rules; 4 facilities; 4 cleaning zones incl. the "Hela lägret" bundle;
   3 blocks; and one booking in every lifecycle state (Pending/Approved/DetailsComplete/Active/Completed/
   Denied/Cancelled/Closed).

It runs **every time the app starts while the flag is on** — that's why you remove the flag after seeding.

## Why it works this way

The staging PostgreSQL is **VNet-only** (no public internet access), so you can't run SQL against it from
a laptop. The App Service container is the only thing inside the VNet that can reach the DB — so the wipe +
seed is done *by the app itself* at startup, toggled by a config flag. No public reset endpoint exists.

## Safety

- The flag is **never set in production** (not in the prod parameters/Bicep), so this can't fire there.
- In source it defaults to `false`. It's turned on **deliberately and temporarily**, then removed.
- **Footgun:** if you leave `DemoSeed__Reset=true` on staging, the DB is wiped + reseeded on *every*
  restart (deploy, platform recycle, idle power-cycle) — which would erase anything created live since.
  Always delete the setting after seeding.

## Re-seed — Staging

The seeder ships on `main` (merged since Phase 8), so no special branch is needed for a normal deploy —
just remember the `gh workflow run deploy-staging.yml` dropdown defaults to `main` anyway, so this is a
non-issue now; it only mattered while the seeder lived on an unmerged branch.

Resource group `akerogarden-staging`, app `akerogarden-staging-app` (az CLI logged in; mgmt-plane).

```bash
# 1. Turn the flag on — this AUTO-RESTARTS the app, which wipes + reseeds on boot.
az webapp config appsettings set -g akerogarden-staging -n akerogarden-staging-app \
  --settings DemoSeed__Reset=true

# 2. Wait ~1 min, then VERIFY AGAINST THE DATA (not just health — see gotcha below):
#    the public facilities page should list the seeded facilities.
curl -s https://akerogarden-staging-app.azurewebsites.net/faciliteter | grep -o "Bastu"

# 3. Remove the flag so no future restart re-wipes.
az webapp config appsettings delete -g akerogarden-staging -n akerogarden-staging-app \
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
   cd src/AkeroGarden.Web
   dotnet user-secrets set "DemoSeed:Reset" "true"
   ```

3. Run the app — this runs EF migrations against the now-empty DB, seeds the admin account, then
   wipes+reseeds the demo dataset (8 bookings across every lifecycle state):

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

Start **PostgreSQL first**, wait until Ready, **then** the app (it runs EF migrations on boot):

```bash
az postgres flexible-server start -g akerogarden-staging -n akerogarden-staging-pg   # wait until Ready
az webapp start -g akerogarden-staging -n akerogarden-staging-app
```

Power down when idle: `az webapp stop …` + `az postgres flexible-server stop …`.
