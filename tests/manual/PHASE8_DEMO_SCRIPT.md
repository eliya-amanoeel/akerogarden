# Åkerögården — Demo script (Phase 8, client demo on staging)

> **Untracked working doc** — not committed to git. For presenting the app to the client on the
> staging environment. Runs entirely on the seeded demo data.

---

## 0. Before the demo — checklist

- [ ] **Staging is up.** Open `https://akerogarden-staging-app.azurewebsites.net/login` → expect the login page (200). If it's slow/down, start **PostgreSQL first** (`az postgres flexible-server start -g akerogarden-staging -n akerogarden-staging-pg`, wait until Ready), **then** restart the app.
- [ ] **Data is seeded.** `https://…/faciliteter` shows Bastu, Konferenssal, Kanoter, Lekplats. (If empty, the DB got wiped without reseed — re-seed: set App Setting `DemoSeed__Reset=true` → restart → wait → **delete the setting** again.)
- [ ] **Admin login ready:** `/login` with the seeded admin account (`eliyaamanoeel@gmail.com`). Have the password handy.
- [ ] **Two browser tabs / windows:** one public (incognito is nice — no admin session), one admin.
- [ ] **`DemoSeed__Reset` is NOT set** on staging (so nothing wipes mid-demo). It was removed after seeding.
- [ ] **Phone/mobile** ready if you want to show the responsive booking funnel + completion form on a real device.

### Set expectations up front (say this to the client)

> "This is the **staging** environment on a temporary Azure address — the real domains
> (`akerogarden.se` / `admin.akerogarden.se`) come at go-live. The **photos, the privacy-policy text,
> and the VAT figures/prices are placeholders** — today is exactly when I'd like to collect the real
> ones from you. All the guest data you'll see is **made-up test data**."

### What we want OUT of this demo

1. Their reaction to the public site + the booking flow (does it feel like Åkerögården?).
2. Confirmation the admin workflows match how they actually run the place.
3. **Collect:** real photos, confirmed VAT rates per category (accountant), privacy-policy review.
4. **Decide** the parked questions (see §5).
5. **Pitch** the booking reference number (see §5).

---

## The seeded data at a glance (your cheat-sheet)

**Reference data:** 3 buildings (Huvudbyggnaden · Annexet · Sjöstugan) + 12 rooms; 6 meal options
(3 lunch / 3 dinner, some vegetarian); 6 price rules (incl. a summer-season row); 4 facilities;
4 cleaning zones incl. the **"Hela lägret"** bundle; 3 blocks (whole-property, Annexet, Rum 4).

**One booking in every lifecycle state** — each is chosen to show a different screen/feature:

| State | Contact | Shows off |
|---|---|---|
| **Pending** | Karin Lundgren *(Roslagens syförening)* | A fresh inquiry awaiting decision; org/invoice details; facility + "Hela lägret" selections |
| **Approved** | Johan Bergström | Approved, completion link issued (valid magic link), guest hasn't filled details yet |
| **DetailsComplete** | Anna Sjöberg | Guest submitted everything — participants, per-day meals, dietary needs, room assignments |
| **Active** | Peter Nyström | A group **on site right now** (arrived yesterday); admin-assigned rooms; deposit paid |
| **Completed** | Margareta Holm | A finished past stay; fully invoiced/paid; retention date set |
| **Denied** | Fredrik Ohlsson | Declined with a reason (60 guests, no room) |
| **Cancelled** | Sara Lind | Guest-initiated cancellation with reason |
| **Closed** | Ingrid Persson | A **general inquiry** (not a stay) that was answered and closed |

---

## 1. The public site — "what a guest sees" (public tab)

Walk the guest journey in order; the site is built to **convert a visitor into an inquiry**.

1. **Home `/`** — the hero + value proposition + primary call-to-action. Point out the calm/editorial
   feel and the clear "boka / se tillgänglighet" path.
2. **Om oss `/om-oss`** — the trust/story page. *(Text is placeholder-ish; note real copy comes from them.)*
3. **Faciliteter `/faciliteter`** — data-driven from admin: Bastu, Konferenssal, Kanoter, Lekplats.
   Note images are placeholders — **ask them for real photos here.**
4. **Mat & priser `/mat-och-priser`** — the merged menu + prices page. Show the lunch/dinner options and
   the price rules (ex-VAT + VAT rate, **no computed totals** — that's a deliberate rule). Flag VAT as
   unconfirmed → **accountant follow-up.**
5. **Kalender `/kalender`** — the 12-month rolling availability calendar. Show the three states
   (ledigt / delvis bokat / fullbokat) and the disclaimer note. The seeded bookings + the whole-property
   block (~2 months out) make real Fully/Partial days appear. Selecting a date range pre-fills the form.
6. **Boka `/boka`** — the inquiry form (the money path). Demo:
   - Stay request vs general inquiry.
   - Meal plan choice; day guests appear only when meals are involved.
   - Facility multi-select + cleaning-zone checklist (show the **"Hela lägret" bundle disabling the
     zones it covers**).
   - The confirm-email field + inline validation (Phase-7 hardening).
   - Privacy-policy acknowledgement on submit.
   - **Submit one live** to show the acknowledgement screen. *(This creates a real Pending booking on
     staging — fine; it'll show up in admin in §2. Use an address you control if you want to see the
     ack email.)*
7. **Kontakt `/kontakt`** and **Integritetspolicy `/integritetspolicy`** — quick show; note privacy text
   is placeholder pending their legal review.
8. *(Optional)* Resize / show on a phone — everything is mobile-first.

---

## 2. The admin panel — "how you run it" (admin tab)

Log in at `/login`. This track is **productivity design**, not the public aesthetic.

1. **Dashboard `/admin`** — the landing overview.
2. **Bokningar `/admin/bokningar`** — the list with **filters by status/type**. Filter through the
   states so they see all 8 seeded bookings. Point out it's scannable and status is clear.
3. **Open the Pending — Karin Lundgren** (`/admin/bokningar/{id}`):
   - Contact + org/invoice details, message, the facility + "Hela lägret" selections (with the **price
     snapshot** taken at inquiry time).
   - **Internal notes**, deposit/payment flags (boolean status — no amounts stored).
   - **Godkänn (approve)** — show the **approval warning panel** if dates overlap a block or exceed beds
     (non-blocking, amber). Also **Neka (deny)** requires a reason; **Avboka (cancel)**.
   - *(You can approve it live to show token generation + the approval email path.)*
4. **Open the Approved — Johan Bergström** — show the state after approval: the **completion (magic) link**
   the guest gets. Use **"Skicka/Regenerera länk"** to show how you re-send it. To demo the guest side,
   open that completion link (§3).
5. **Open the DetailsComplete — Anna Sjöberg** — the payoff: the guest filled everything.
   - **Participants** (overnight + 1 day guest), **per-day meals** following the arrival=middag /
     mellandag=lunch+middag / avresedag=inget rule, **dietary needs** (laktos, nötallergi, vegetarian).
   - **Room assignments** (with snapshots).
   - **Download the lists** — participant list + food list, each **PDF and Excel** (buttons on the detail
     page). Open them so they see the real output the kitchen/reception would use.
   - Show **admin edit** of a participant (the only post-submission edit path).
6. **Open the Active — Peter Nyström** — a group on site now; show "Markera avslutad" (→ Completed +
   thank-you email).
7. **Open the Completed — Margareta Holm** — a finished stay; note the retention date (auto-deletes 2 yr
   after check-out, GDPR).
8. **Blockeringar `/admin/blockeringar`** — the 3 seeded blocks (whole property / Annexet / Rum 4). Create
   one live to show scope selection; note it immediately reflects on the public calendar.
9. **Admin-kalender `/admin/kalender`** — the internal calendar view.
10. **Skapa bokning `/admin/bokningar/ny`** — manual booking on a guest's behalf (phone bookings).
11. **Reference data** — quick tour so they see they control everything:
    - Byggnader `/admin/byggnader` (+ rooms/beds, accessibility flag)
    - Matalternativ `/admin/matalternativ`
    - Priser `/admin/priser` *(here's where confirmed VAT/prices get entered)*
    - Städzoner `/admin/stadszoner` (incl. the bundle)
    - Faciliteter `/admin/faciliteter` *(here's where real photos get uploaded)*
    - Show **deactivate (soft)** and that a referenced item **can't be hard-deleted**.
12. **Konton `/admin/konton`** — admin account management (invite/deactivate; no self-deactivation).

---

## 3. The completion flow — "what an approved guest does" (public/phone)

The magic-link, no-account completion form — ideally shown **on a phone**.

- Get the link from the **Approved (Johan Bergström)** booking's admin detail (the completion/"skicka
  länk" action), or approve the live Pending from §2 to generate a fresh one. URL form: `/slutfor/{token}`.
- Walk the form: per-participant name/age, full-board toggle, **per-day meal pickers** (clear day
  headings), the **"standard för hela gruppen"** shortcut, dietary needs, day guests (add/remove),
  room choice (when the guest assigns rooms), org/invoice.
- Submit → acknowledgement; the booking flips to **DetailsComplete** and the admin is notified.
- Note token safety: single-use, expires, can be regenerated by the admin.

---

## 4. Reassurances to land during the demo

- **Swedish throughout**, both public and admin.
- **GDPR built in:** no personnummer, no personal data in links, EU-only hosting, auto-deletion on a
  retention schedule, self-hosted fonts (no Google/CDN calls).
- **Nothing computes a price total** — amounts are reference figures the staff read; the system never
  invoices automatically (deliberate, per the spec).
- **Emails** fire on the key transitions (acknowledgement, admin notification, approval + link, denial,
  cancellation, reminders, thank-you).

---

## 5. Decisions & asks — the real point of the meeting

**Collect from the client:**
- [ ] Real **photos** for facilities + pages.
- [ ] Confirmed **VAT rates** per category (via their accountant) + final prices.
- [ ] Review/approve the **privacy-policy** text.

**Settle these parked questions (how do they actually operate?):**
- [ ] **(b) Day-guest edge days** — do breakfast-only / dinner-only day visitors ever happen? If not, we
  simplify the date options.
- [ ] **(c) Admin participant-editing depth** — how much should staff be able to change after submission
  (add/remove meal days, flip full-board)?
- [ ] **(e) Self-catering vs per-participant meals** — reconcile "whole booking self-caters" with
  "individual can override" — which wins in practice?

**Pitch — booking reference number:**
- [ ] Today a booking has only an internal ID; guests get no friendly reference. Propose a short human
  reference like **`ÅG-2026-0042`** shown in the guest email + admin, so phone/email correspondence has a
  shared "your booking is…". Not personal data, not a security key. **If they want it, it becomes a small
  feature we add this phase.**

---

## 6. After the demo

- Note every piece of feedback (issues / changes / improvements) → triage → implement on `phase_8` with
  tests + a staging smoke each.
- Any change to the spec gets mirrored into `requirements.md` / `erd.md`.
- When they've signed off and agreed items are done → merge `phase_8` → main → Phase 9 (go-live).
- **Re-seed anytime** to reset the demo DB — see the appendix below for the exact steps.

---

## Appendix — seeding & resetting staging (full reference)

### What the seeder does

A gated startup routine (`DemoDataSeeder`, in `src/AkeroGarden.Web/Services/DemoDataSeeder.cs`, wired in
`Program.cs`). On app startup, **only when the config flag `DemoSeed:Reset` is `true`**, it:

1. **Wipes every application table** — all bookings, participants, meals, selections, blocks, and all
   reference data (buildings, rooms, meals, prices, facilities, cleaning zones).
2. **Preserves the admin login** — the ASP.NET Identity users (`AspNetUsers`) are left untouched, so you
   never lose the account you sign in with.
3. **Reseeds** the full synthetic Swedish demo dataset (the cheat-sheet at the top of this doc).

It runs **every time the app starts while the flag is on** — that's why you remove the flag after seeding.

### Why it works this way

The staging PostgreSQL is **VNet-only** (no public internet access), so you can't run SQL against it from
a laptop. The App Service container is the only thing inside the VNet that can reach the DB — so the wipe +
seed is done *by the app itself* at startup, toggled by a config flag. No public reset endpoint exists.

### Safety

- The flag is **never set in production** (not in the prod parameters/Bicep), so this can't fire there.
- In source it defaults to `false`. It's turned on **deliberately and temporarily**, then removed.
- **Footgun:** if you leave `DemoSeed__Reset=true` on staging, the DB is wiped + reseeded on *every*
  restart (deploy, platform recycle, idle power-cycle) — which would erase anything created live during
  the demo. Always delete the setting after seeding.

### Prerequisite: the deployed build must contain the seeder

The seeder lives on branch **`phase_8`** (not yet merged to `main`). Setting the flag does nothing unless
the **`phase_8`** build is what's deployed to staging. When deploying:

```bash
gh workflow run deploy-staging.yml --ref phase_8
```

⚠️ The Actions "Run workflow" dropdown **defaults to `main`** — always pick/pass **`phase_8`**, or you'll
ship a build with no seeder.

### Re-seed - Staging / reset — exact steps

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

### Re-seed - Dev
1. Wipe the Postgres volume completely (stops containers and deletes the named volume, so it's a truly fresh DB, not just an empty schema):


docker compose down -v
docker compose up -d
2. Turn on demo seeding for the next run:


cd src/AkeroGarden.Web
dotnet user-secrets set "DemoSeed:Reset" "true"
3. Run the app — this runs EF migrations against the now-empty DB, seeds the admin account, then wipes+reseeds the demo dataset (8 bookings across every lifecycle state, per PHASE8_DEMO_SCRIPT.md):


dotnet run
Watch the startup log for the "Demo data seeded: ..." warning line — that confirms it worked.

4. Turn seeding back off so a later restart during your smoke testing doesn't silently re-wipe your in-progress manual test data:


dotnet user-secrets remove "DemoSeed:Reset"

**Timing gotcha:** immediately after step 1, the app can still return `200` from the *previous*
container (serving the old, un-seeded data) while the new container boots and seeds. Don't trust a bare
health check — confirm the **seeded data** is actually present (step 2) before assuming it worked.

### If staging is powered down

Start **PostgreSQL first**, wait until Ready, **then** the app (the app runs EF migrations on boot):

```bash
az postgres flexible-server start -g akerogarden-staging -n akerogarden-staging-pg   # wait until Ready
az webapp start -g akerogarden-staging -n akerogarden-staging-app
```

Power down when idle: `az webapp stop …` + `az postgres flexible-server stop …`.
