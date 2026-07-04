# Phase 4 — Manual Test Plan (Completion Flow)

A hands-on checklist for the booking **completion flow**: token page, completion form
(overnight participants, meals, day guests, rooms), submission, and admin participant
editing. Work top to bottom — later sections reuse bookings created earlier.

> This is a scratch test doc (untracked). Delete it whenever you like.

---

## 0. Environment bring-up

- [ ] Docker stack running: `docker compose ps` shows postgres / redis / azurite / mailpit **Up**.
- [ ] App running on **:5285**: `cd src/AkeroGarden.Web && dotnet run` → open <http://localhost:5285>.
- [ ] Mailpit inbox reachable: <http://localhost:8025>.
- [ ] Admin login works: <http://localhost:5285/login> with `eliyaamanoeel@gmail.com` + your local admin password.
- [ ] **Reference data exists** (needed for meals/rooms to appear). Check `/meny`, `/priser`, and
      `/admin/byggnader` show buildings, rooms, and meal options. If empty, re-seed before continuing.

> **Port note:** completion/approval email links now point at `:5285` (fixed). If you ever see a
> `:5000` link with a **403**, that's the macOS AirPlay Receiver — just change `:5000` → `:5285`.

---

## 1. Create + approve a booking (gets you a completion link)

Do this three times, once per meal plan, so you have bookings for later sections. On `/boka`:

- [ ] **Booking A — FullBoardAll:** submit a stay request, check-in ~3 weeks out, 2–3 nights,
      **3 overnight guests**, meal plan **"Alla helpension"**, add **2 day guests**.
- [ ] **Booking B — SelfCateringAll:** stay request, **2 overnight guests**, meal plan
      **"Alla självhushåll"**. (Day-guest field should be **hidden** on this form — verify.)
- [ ] **Booking C — Mixed:** stay request, **3 overnight guests**, meal plan **"Blandat"**, 1 day guest.

For each: check <http://localhost:8025> — you get an **acknowledgement email** (guest) + a
**new-inquiry email** (admin).

Then approve each:

- [ ] `/admin/bokningar` → open the booking → **Godkänn**.
- [ ] Mailpit shows an **"Godkänd" approval email** with a **"Slutför din bokning här"** link and an
      **exact expiry date** in plain text.
- [ ] Booking status is now **Approved**.

**Keep the three completion links handy** (from Mailpit).

---

## 2. Token / link states

Open Booking A's completion link → you should see **"Slutför din bokning"** with the booking summary.
Now check the failure states (each should be a clean **Swedish message**, no error page):

- [ ] **Invalid link:** change one character in the token in the URL → *"Länken är inte giltig"*.
- [ ] **Already completed:** (come back to this after §6 — a submitted booking's link shows
      *"Uppgifterna är redan inskickade"*).
- [ ] **Expired:** hard to trigger by hand; the automated tests cover it. (Optional: ask me and I'll
      expire one via the DB.)
- [ ] **Regenerate:** on the admin booking detail (Approved), click **"Återutfärda och skicka länk på
      nytt"** → a **new** email arrives; the **old** link now shows *"Länken är inte giltig"*.

---

## 3. Completion form — full-board (Booking A)

Open Booking A's link. For the **3 overnight participants**:

- [ ] Each row has **Namn**, **Ålderskategori** (Vuxen / Barn 4–12 / Barn 0–3), a **Helpension** checkbox
      (pre-ticked), a **meal section**, and **Kostbehov** checkboxes.
- [ ] **Meals-by-day rule:** arrival day shows **dinner only**; middle days show **lunch + dinner**;
      the departure day shows **no meal picker**. (1-night stay would show a single dinner.)
- [ ] **"Helpension" toggle:** untick it → that participant's meal section **disappears**; re-tick → it
      comes back.
- [ ] **"Samma för alla dagar":** pick a lunch and a dinner in the shortcut selects → they **copy into
      every day** for that participant.
- [ ] **Kostbehov:** tick "Annat" **without** filling the note → on submit you get a validation error
      asking for the note.
- [ ] Fill everything validly and leave §3 for now (submit in §6, or after §4/§5).

---

## 4. Completion form — self-catering (Booking B)

Open Booking B's link:

- [ ] Overnight participants show the **Helpension** checkbox **unticked** and **no meals** required.
- [ ] **No day-guest section** appears (self-catering has no day guests).
- [ ] Submitting with just names (no meals) **succeeds** → *"Tack!"* (this is the exact case that used to
      500 — confirm it's clean).

---

## 5. Completion form — day guests (Booking A or C)

On a **FullBoardAll / Mixed** booking's completion form, scroll to **Dagsgäster**:

- [ ] **"Lägg till dagsgäst"** adds a row **without losing** anything you already typed above.
- [ ] Add 2 day guests. Each has **Namn**, **Ålder**, **Dag** (a dropdown of the stay's dates),
      **Deltar på frukost**, optional **Lunch**/**Middag**, **Kostbehov**.
- [ ] Type into day guest 1, then **"Lägg till dagsgäst"** again → day guest 1's data is **preserved**.
- [ ] **"Ta bort dagsgäst"** on the first row → it's removed and the rest **re-number** correctly.
- [ ] Leave a fully-empty day-guest row → it's **ignored** on submit (not an error).
- [ ] A day guest with a **date outside** the stay → validation error *"dagen måste ligga inom vistelsen"*.

---

## 6. Room assignment (Booking C, Mixed)

Room picking only appears when the admin turns it on:

- [ ] `/admin/bokningar` → open Booking C → **Rumsval** section → tick **"Gästen väljer rum själv"** →
      **Spara rumsval-inställning**.
- [ ] Re-open Booking C's completion link → each **overnight** participant now has a **Rum** dropdown,
      **grouped by building**, showing bed counts. (Day guests have **no** room field.)
- [ ] Pick a room for each overnight participant, fill the rest, and **Skicka in uppgifter** → *"Tack!"*.
- [ ] (Optional block test) Ask me to add a room block over the dates → re-open the link → the blocked
      room is **no longer offered**.

---

## 7. Submission outcome (any completed booking)

After submitting a completion form:

- [ ] Page shows **"Tack!"**.
- [ ] Mailpit shows an **admin "Bokningsuppgifter inskickade"** email (FR-61).
- [ ] `/admin/bokningar` → the booking status is now **DetailsComplete**.
- [ ] **Re-open the same completion link** → *"Uppgifterna är redan inskickade"* (no reopen, C10). This
      closes the §2 "already completed" check.

---

## 8. Admin participant view + edit (FR-48)

Open a **DetailsComplete** booking in `/admin/bokningar/{…}`:

- [ ] A **Deltagare** section lists every participant: **overnight** rows (name, age, room dropdown, meal
      dropdowns, dietary) and **day-guest** rows (name, age, attendance date, breakfast, dietary).
- [ ] Change a participant's **name / age / dietary** → **Spara deltagaruppgifter** → reload → it stuck.
- [ ] **Assign / change a room** on an overnight participant → save → reload → it stuck.
- [ ] **Change a meal choice** in a dropdown → save → reload → it stuck.
- [ ] **Room conflict:** assign **more overnight participants to one room than it has beds** → save → a
      red **"Rumskonflikter"** banner appears listing the over-assigned room.
- [ ] Confirm editing here **does NOT change the booking status** (stays DetailsComplete) and the guest
      link stays closed.

> **Known limitation (gap we'll discuss):** the admin editor can change existing meal choices but can't
> yet add/remove meal days or flip a participant's full-board status. Expected for now.

---

## 9. Quick regression sweep

- [ ] A booking that is **not** GuestChoosesRooms shows **no** room field on the completion form (admin
      assigns in the panel instead).
- [ ] All text you saw is **Swedish** (no stray English).
- [ ] No page threw a raw error / 500 at any point.
- [ ] The completion form is a normal **full page load** each time (no live-connection spinner) — that's
      the intended Static-SSR behaviour.

---

### If you hit anything odd
Note the **booking (contact name)**, the **step**, and **what you expected vs saw** — paste it to me and
I'll dig in. Several real bugs already surfaced this way.
