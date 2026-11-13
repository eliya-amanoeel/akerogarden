# Client review 2026-11-11 — change requests and decisions

The client's wished changes after two months of live use, each discussed and locked on 2026-11-12. This is a
decision record, not a requirements document: **the SRS (`docs/product/requirements.md`) stays the authority on
behaviour**, and each change updates it when it is built. Items are sorted **easiest to hardest**; the number in
brackets is the client's original numbering.

## Working instructions (from the maintainer — apply to every item below)

- **Always consult with me before making changes.**
- **For each change, check whether it affects the booking terms and the privacy policy** (`docs/legal/Bokningsvillkor.md`, `docs/legal/Integritetspolicy.md` and their published pages).
- **For each change, check the existing code and styles before assuming anything.**
- **Follow our general coding pattern — don't invent new ways to code.**
- **Always check whether a change would add Azure costs.**
- **Always make sure all live docs are consistent with the change, in sync with each other, and truthful** — the SRS, `erd.md`, `architecture.md`, `booking-lifecycle-timeline.md`, `design.md`, the legal texts and their pages, the admin handbook (FR-117) and `CLAUDE.md`.

## Status at a glance

| # | Change | Status | Size |
|---|---|---|---|
| 1 | [4] One.com access for the client | Recorded — maintainer does it, no code | none |
| 2 | [6] "Husvagn" is a powered caravan pitch | Locked — client renames in admin; SRS wording only | none |
| 3 | [3] New phone number | ✅ **Done** 2026-11-12 — awaiting deploy | XS |
| 4 | [14] Sender shows as "Info" | ✅ **Done** 2026-11-12 — awaiting deploy | S |
| 5 | [8] Remove the extra cleaning day | ✅ **Done** 2026-11-12 — awaiting deploy | S |
| 6 | [1] Vegan as a dish label | ✅ **Done** 2026-11-13 (with 12) — awaiting deploy | S (migration) |
| 7 | [7] Completion link 3 weeks before the stay | ✅ **Done** 2026-11-13 (client: exactly 21 days) — awaiting deploy | S code, M docs |
| 8 | [10] Admin gets the guest's full summary email | ✅ **Done** 2026-11-13 — awaiting deploy | M |
| 9 | [11] One signature with logo and phone in all emails | ✅ **Done** 2026-11-12 — awaiting deploy | M |
| 10 | [13] Deposit: 14 days, reminder inside the period, admin notified | **Dropped** 2026-11-13 — overdue deposit made visible instead; ✅ built, awaiting deploy | S |
| 11 | [12] New cancellation terms | Locked rule — **3+1 questions open with client** | M–L |
| 12 | [5] One menu — any dish at any sitting | ✅ **Done** 2026-11-13 (with 6) — awaiting deploy | L (migration) |
| 13 | [2] One image per building and per room | Locked — layout by HTML mockup first | L (migration) |
| 14 | [9] Participants via Excel upload | Locked | L |
| 15 | [15] Image gallery | Future — not designed | — |
| 16 | "Anländ" status | Recorded only — added 2026-11-13, not yet discussed | — |
| 17 | Two age groups | ✅ **Done** 2026-11-13 — awaiting deploy | S (migration) |

---

## 1. [4] One.com access for the client

**Request.** The client wants admin access to the one.com control panel and webmail — they own the domain and pay for it.

**Decision.** Recorded only; the maintainer sets it up. **The client must be told that the DNS records there run the live site** — the App Service records, the email records (SPF/DKIM) and the Google verification TXT (which must never be deleted). Changing or removing them takes the site or its email down.

**Terms / privacy / Azure cost.** None.

## 2. [6] "Husvagn" is a powered caravan pitch

**Request.** The facility "Husvagn" is not a caravan; it is the option to park one's own caravan by a power outlet.

**Today.** Facility name and description are admin-editable (FR-28); "Husvagn" is reference data, not code.

**Decision.** **The client renames it in admin** (Faciliteter → Redigera, e.g. "Husvagnsplats med el"). We only update the SRS wording (§5 price table, "Caravan pitch (Husvagn)"). Existing bookings keep the snapshot "Husvagn" (NFR-15/16) — correct, it is what they booked.

**Client to-do.** Rename in admin. Optional: a phone photo of the pitch with its outlet — the 2026-09-16 "no photo" decision rested on there being no caravan to photograph.

**Terms / privacy / Azure cost.** None.

## 3. [3] New phone number

**Today.** The number appears in three places: `/kontakt` (`076-060 20 35`, `tel:` link), the booking terms page (`+46 760 602 035`) and `docs/legal/Bokningsvillkor.md` (test-guarded exact copy).

**Decision.** New number **073-952 78 73**, in each place's existing format: `/kontakt` `073-952 78 73` with `tel:+46739527873`; terms `+46 739 527 873`. **Held once in code**, read by every place that shows it — including the email signature (item 9).

**Built as the whole class, not the one number.** Every contact detail — name, phone, both email addresses, street address, organisation number — now lives once, as constants in `ContactDetails`, read by every page, the legal pages, the handbook, the signature and the emails. Found while doing it: the guest emails' "Adress" line (details received, 14-day reminder) had only ever said "Åkerögården, Norrtälje", a placeholder never replaced; it now gives the street address.

**Terms.** Yes — the contact line in the booking terms (page + `.md`, kept identical). The terms date stays 13 oktober 2026: a contact detail is not a term (maintainer, 2026-11-12). **Privacy / Azure cost.** None.

## 4. [14] Sender shows as "Info" instead of "Åkerögården"

**Cause (found 2026-11-12).** The code sends `From: <FromName> <FromAddress>`, but production never sets `Smtp__FromName`: Bicep sets host, port, address and credentials (`infra/azure/main.bicep`) but not the name, and the base `appsettings.json` has no `Smtp` block — only local dev sets "Åkerögården". Production sends with an empty name, so mail clients show the local part, "info".

**Decision.**
1. The sender name is **not a setting**: it is the business's name, the same everywhere, so it is a constant (`ContactDetails.Name`). The `FromName` setting is gone, and with it the gap a missing value fell through.
2. A test that every email is sent as "Åkerögården" unless a sender name is given.
3. **Only what guests receive says "Åkerögården"** (maintainer, 2026-11-12, after local testing). The admin notifications to `info@` about a booking or question carry the **guest's name** as sender, matching their Reply-To, so the inbox lists who wrote; one with no guest behind it (the pre-deletion warning) falls back to "Åkerögården". Admin invitations keep "Åkerögården".

**Terms / privacy / Azure cost.** None.

## 5. [8] Remove the extra cleaning day

**Today (FR-106/113/37).** A booking's check-out day **and the following day** are unavailable — two turnover days — on the public calendar, at inquiry and at approval. Computed in one place (`TurnoverWindow`).

**Decision.** Only the **check-out day** stays blocked: a group leaving Sunday → the next group can arrive Monday at the earliest. Existing bookings are unaffected; days blocked only for turnover open up. Exceptional cases (very late departure) are still handled by an admin block (FR-21).

**Built (2026-11-12) — from the new rule, not by shortening the old window.** A booking now holds every date from check-in **through** check-out, both inclusive; that is the whole rule. The separate "turnover window" (`TurnoverWindow`, the `Turnover` unavailability reason, the split between conflicting nights and turnover days, the Kalendervy **Städdagar** column) existed only to carry a buffer reaching past the stay, and is gone. When the check-out day is the conflict, both the guest's and the admin's message say so — the group does not sleep that night, so nobody expects it to need to be free. The public calendar's intro, which said *"Dagen efter en avresa är alltid avsatt för städning"*, now says the departure day is held and the next group can arrive the day after.

**Docs.** SRS FR-10, FR-37, FR-106 (with the why), FR-113 and the §2.3 diagram; the admin handbook (three places). `booking-lifecycle-timeline.md`, `erd.md`, `architecture.md` and `design.md` do not draw the window — checked. **Terms / privacy / Azure cost.** None — the terms never mention the cleaning days; no schema change.

## 6. [1] Vegan as a dish label

**Today.** Guests can already state vegan portions (`VeganCount`, §4.6). What's missing is on the dish: `MealOption.IsVegetarian` is one checkbox, used only for the green "Vegetariskt" label on `/mat-och-priser`.

**Decision.** Replace the checkbox with one choice: **Ingen / Vegetarisk / Vegansk**. A vegan dish shows "Veganskt" (not both). Migration maps `IsVegetarian = true` → Vegetarisk, everything else → Ingen.

**Built (2026-11-13), with item 12 in one migration (`OneMenuWithDishDiet`).** `MealOption.IsVegetarian` is gone; `Diet` (`DishDiet`: None / Vegetarian / Vegan) replaces it — one value, so the "not both" rule is the model's shape, not a check. Admin `/matalternativ`: a **Kost** select in the panel and a **Kost** column (Vegetarisk / Vegansk / –). Public menu label `.meal-diet` (was `.meal-veg`, which stopped being true).

**Docs.** FR-22, §8.7, `erd.md`, handbook (Matalternativ row). **Terms / privacy / Azure cost.** None — a dish label is not personal data, and the policy's "gruppens gemensamma val av måltidsalternativ" stays true.

## 7. [7] Completion link 3 weeks before the stay

**Today (§2.6, FR-108, FR-53).** The link is issued **40 days** before check-in and expires **14 days** before — about 4 weeks to fill it in. The 14-day deadline is the client's own: the terms require final participant numbers and dietary needs **14 days before arrival**, and kitchen and cleaning plan from it. On day 14 the completion nudge sends a fresh link valid to 3 days before.

**Decision (client, confirmed 2026-11-13): exactly 21 days.** The deadline stays at 14 days, so the guest gets **one week**; a group that misses it is nudged on day 14 with a link valid to 3 days before arrival, as today. Technically sound: the 30-day security ceiling only bites past 44 days, so at 21 no link can die while the guest still needs it.

**Built.** `CompletionTokenPolicy.IssueLeadDays` 40 → 21. The guest's approval email said *"cirka sex veckor före ankomst"* as a written-out phrase, and the admin page, its dialogs and the handbook repeated it in five places — all now built from the constant (*"21 dagar före ankomst"*), so the number cannot drift from the promise again. §2.6 rewritten (21 is the client's choice; 44 is the hard limit). Tests and the demo seed moved inside the new window.

**Bookings already approved when this deploys.** A booking approved more than 40 days ahead was told the link comes *"cirka sex veckor före ankomst"*; after the deploy it comes at 21 days. Nothing breaks — the link still arrives with a week to fill it in — but those guests were promised earlier. If that matters, the admin can send any of them the link early with **Skicka slutförandelänk nu**.

**Docs.** §2.6, FR-49/51/52/108, the email table, the flowchart, `booking-lifecycle-timeline.md` (scenarios redated), `architecture.md`, handbook, seeding, README. **Terms / privacy.** None — the terms keep the 14-day deadline and name no issue date. **Azure cost.** None.

## 8. [10] Admin gets the guest's full summary email

**Today (FR-107 / FR-61).** On submission the guest gets a full summary; the admin gets a five-line "details are in" alert.

**Decision.** The admin gets **the guest's full summary** — participants with allergies, dietary counts, meals per day, lodging, times, address — **plus** the contact person's name, email and phone and the "Öppna bokningen i adminpanelen" link, **minus** the guest-only lines ("Behöver något ändras?…" and the single-use-link note). Subject unchanged: "Bokningsuppgifter inskickade (AG-…)".

**Context.** `info@` is a One.com mailbox in the EU.

**Privacy — check when built.** The email puts named participants' **allergies (health data, art. 9)** in the `info@` mailbox. The policy says allergy data is shared only with the staff who plan meals (via their system accounts) and is deleted with the booking; email in `info@` is not auto-deleted (§5). Policy wording needs to cover this. **Docs.** FR-61, FR-107, handbook. **Azure cost.** None.

**Built 2026-11-13** (branch `feat/admin-full-summary`) — **narrowed from the decision above, maintainer's call, client to be told.** The admin's copy carries the group as **counts** — participants, each kind of allergy with how many have it, dietary preferences — and **no participant names or allergy notes**. Why: the email lands in `info@`, which nothing deletes, and **One.com cannot auto-delete mail** (checked by the maintainer). Named allergies there would be art. 9 health data outliving the booking indefinitely; the privacy policy would have had to say so, and a manual deletion routine would make it true only as long as someone keeps it. A count tied to no name is not about anyone — the reasoning that already keeps vegetarian/vegan as counts — so **the privacy policy does not change**. Names and who has which allergy stay in the panel and the lists. Built from the rule, not beside the old alert: the stay (times, lodging) and the meals are **one rendering** both emails share; only the participant section differs. A free-text note with no box ticked counts as "Annat", as on the kitchen list. **Found while building it, fixed in the same branch:** `BookingDisplay.AllergySummary` showed a note only beside a ticked "Annat", so a note on its own read "—" in the guest's summary email and the participant list (FR-44), and the completion form's collapsed row offered "Lägg till allergi" over it — only the kitchen list had a local workaround. The rule now lives in `AllergySummary` (a note is always shown: in "Annat (…)" when ticked, otherwise appended) and the workaround is deleted. **The address is left out of the admin's copy** (maintainer's call). The guest's copy now reads "**Boende:** …" instead of "Ert boende: … Ni fördelar själva gruppen mellan rummen" (that sentence stays in the approval email) and "Självhushåll — inga måltider är beställda." — the shared sections speak in no one's voice. **Residual:** in a group of one or two, a count can still point at a person — accepted. Terms: none. Azure cost: none.

## 9. [11] One signature with logo and phone in all emails

**Today.** Booking emails (`BookingEmailService.WithFooter`) carry a text-only footer: "Åkerögården / Kontakt: info@… / Integritetspolicy". Admin invitations (`AdminInviteService`) carry **none** — and neither does the password reset (`ForgotPassword.razor`), found while building this.

**Decision.**
- The signature moves to the one place every email passes through (`SmtpEmailService`), so no email — present or future — can go out without it.
- Content: logo, "Åkerögården", `Telefon: 073-952 78 73` (clickable), `E-post: info@akerogarden.se`, `akerogarden.se`, "Integritetspolicy".
- **Logo embedded as a PNG in the email** (inline attachment), not linked: Gmail/Outlook don't render SVG, and Outlook hides linked images by default. Rendered from the existing `lockup` SVG; ~15 KB per email.

**Depends on** item 3 (phone held once in code). **Terms / privacy / Azure cost.** None (email goes via one.com SMTP).

## 10. [13] Deposit: 14 days, reminder inside the period, admin notified

**Today (FR-115/116, `DepositPolicy.DaysToPay = 7`).** Due date 7 days ahead, never later than the day before check-in. The night after the due date passes, one reminder goes to the guest. After that nothing happens by itself: the booking is shown red on the dashboard, no admin email, and the dates stay held **until the admin acts — with no end**.

**Dropped 2026-11-13 (maintainer).** A change was drafted — 14 days to pay capped at 21 days before arrival, the reminder moved inside the period, an automatic admin email the night after the due date. It was the maintainer's change, not the client's, and it would have replaced the client's own 7 days (their answer of 2026-10-08), tied the due date to item 7's open question, and needed terms §6 reworded: §6 lets an unpaid booking be treated as cancelled only "**efter påminnelse**", which today's reminder *after* the due date satisfies and a reminder before it may not. **FR-115/116 stay as they are** — 7 days, one reminder after the due date, nothing released automatically, no admin email.

**What is built instead — the overdue booking is made visible.** The real weakness was that an overdue booking holds dates nobody else can book, and the admin screens did not say so. Now:
- **One display rule:** wherever a booking's status badge is shown — Bokningar, Kalendervy, Översikt, the booking page — a booking still waiting past its due date shows the red **Handpenning förfallen** badge instead of *Väntar på handpenning*. Before, only the dashboard card showed it.
- **The booking page's alert names the dates it holds:** *"Bokningen håller fortfarande [incheckning] – [utcheckning] — ingen annan kan boka de datumen. … annars avboka den för att frigöra datumen."*
- **The dashboard card** shows *"Förfallna bokningar håller sina datum tills ni bekräftar eller avbokar dem."* while one is overdue.

Agreed from an HTML mockup. **Docs.** FR-116, handbook. **Terms / privacy.** None — §6 unchanged. **Azure cost.** None.

## 11. [12] New cancellation terms

**Request (client).** Cancelled **≥ 8 weeks** before arrival: nothing charged, a paid deposit is refunded. **< 8 weeks**: a paid deposit is kept; if no deposit was paid, Åkerögården may bill **20 % of the total**. Reflected in all email communication.

**Today.** Terms §7: deposit never refunded; > 4 weeks no fee beyond it; 4–2 weeks 75 %; < 2 weeks 100 %; no-show 100 %. FR-79: emails **never restate** the scale, only point to the terms. (The new rule is close to the 20 % / two-month rule the system had before the October deposit work.)

**Decision.** The rule as stated by the client. Because the system knows the days to arrival, whether a deposit was paid, and the price (20 % = the deposit amount), the cancellation email states **what applies to this cancellation** ("…mer än 8 veckor före ankomst, handpenningen på X kr återbetalas") rather than a general scale. FR-79 is rewritten accordingly.

**Open questions for the client (to forward):**
1. **Late cancellations:** today 2 days before arrival costs 100 %; under the new rule at most the deposit (20 %). Intended?
2. **No-show:** what applies?
3. **Existing bookings:** old or new terms? (§32: the terms in force at confirmation apply.)
4. **Åkerögården cancels for a missing deposit** (FR-116): does the 20 % apply, or does today's "the booking was never confirmed, nothing is owed" stand?

**Terms.** Yes — §7 and the "never refunded" line (page + `.md`). **Emails.** Deposit request ("återbetalas inte vid avbokning"), guest cancellation, admin-cancellation variant. **Docs.** FR-79, FR-115, §5.6. **Azure cost.** None.

## 12. [5] One menu — any dish at any sitting

**Today.** Two separate things are called lunch/dinner:
- **A. Dish type** (`MealOption.MealType`): every dish is a lunch or a dinner dish; the completion form and admin booking detail only offer lunch dishes at lunch and dinner dishes at dinner; `/mat-och-priser` shows two lists.
- **B. Sitting** (date + lunch/dinner): the group's choice per sitting; the kitchen list, emails, arrival/departure-meal rules and booking detail rest on it.

**Decision.** **Remove A, keep B.** The admin meal form loses "Lunch / Middag"; every lunch and dinner dropdown offers the full menu; `/mat-och-priser` shows one list. Breakfast unchanged (buffet). Submitted bookings keep exactly what they chose.

**Client to-do after deploy.** Dishes that exist twice (e.g. "Wokade grönsaker med nudlar" as lunch and dinner) will appear twice — **deactivate the duplicate** in admin. Not merged automatically, since bookings reference both.

**Built (2026-11-13), with item 6.** `MealOption.MealType` is dropped — the `MealType` enum now means only the sitting. Both "dish must match the sitting" checks are deleted (the completion form's normaliser and the admin's meal edit); both forms build one menu list. `/mat-och-priser` shows **one list, flowing into two columns from 48em** (read down then across; maintainer picked this from an HTML mockup, 2026-11-12). **Display order:** it used to restart per type (lunch 1–5, dinner 1–5), so the migration moves dinners after the last lunch — the menu reads as it did. Demo seed: the stir-fried vegetables are one dish.

**Docs.** FR-22, FR-83, FR-105, §4.1, §4.4 (one menu), §4.5 (the rule and its why), §4.6, §8.7, `erd.md`, `design.md`, handbook. **Terms / privacy / Azure cost.** None.

## 13. [2] One image per building and per room

**Today.** The only admin image upload is Faciliteter → Bild (one per facility, Blob Storage, JPEG/PNG/WebP, 5 MB). Lodging appears publicly on `/faciliteter` as **text only**, grouped by physical building then lodging building; **rooms are never shown publicly** — they are capacity data.

**Decision.**
- **One image per lodging building and one per room**, uploaded exactly as facility images are (same component, container, formats, limit).
- **Rooms appear publicly for the first time:** each building on `/faciliteter` lists its rooms with photo, name and bed setup (e.g. "2 bäddar – våningssäng").
- No photo → the existing green "Foto" placeholder.
- **Layout agreed by HTML mockup before building** — a building with many rooms must not make `/faciliteter` endless on a phone.

**Docs.** FR for buildings/rooms, §8 entities, `erd.md`, `design.md`. **Azure cost.** Negligible — a few MB more in the existing Blob container. **Terms / privacy.** None.

## 14. [9] Participants via Excel upload

**Request.** For large groups, filling in every participant is a pain: send an Excel template, the guest fills it in and uploads it (maybe also PDF). Does it raise Azure costs?

**Azure cost — answered: no.** The template (~10 KB) is served by the existing app; the upload is read in memory on the existing App Service and discarded — never stored, so no Blob cost. ClosedXML (already used for the Excel lists) reads Excel too; no new package.

**Decision.**
1. **Excel only, no PDF** — a PDF is a printout, not data; extracting names reliably would need text recognition, which is where real cost and errors come in.
2. **Template downloaded from the completion form** ("Ladda ner mall"), **not attached to the email** — the link email must arrive, and attachments raise spam risk.
3. **The upload fills the form; it does not submit it.** The guest reviews every row, corrects, and submits as today, through the same validation.

**Privacy — check when built.** The file holds allergies (health data); not storing it keeps the policy's description of where participant data comes from true, but confirm the wording. **Docs.** FR for the completion form, handbook. **Terms.** None.

## 15. [15] Image gallery — future

**Recorded only, not designed.** When it comes up: it builds on item 13's upload and Blob storage (item 13 deliberately stays at one image per object so it doesn't become the gallery). Open then: where it lives, who curates it, how many images.

## 16. "Anländ" status

**Recorded only, not yet discussed** — added by the maintainer on 2026-11-13. Today a group on site has the status `Active`, shown as **"På plats"** (§2.5).

## 17. Two age groups

**Raised 2026-11-13** by the maintainer while planning item 14, and settled with the client the same day.

**Today (§8.5, FR-57/58).** Three age categories: *Vuxen (13+)*, *Barn (4–12)*, *Barn (0–3)*. The free/paid line is age 4; the 4–12 band was said to serve the kitchen's portion planning, but nothing uses it — no price is computed from age (C8), and the kitchen list counts dishes, not ages (FR-45). The age shows only as a label: the completion form, the admin's booking page, the participant list and the guest's summary email.

**Decision (client).** Two groups: **Barn (0–3)**, free, and **Vuxen (4+)**, the standard per-person rate. The line the client prices by is unchanged, so **no price changes** — only the unused 4–12 band goes.

**Built.** `AgeCategory` is `Adult` / `Child`; `Infant` is gone and `Child` now means 0–3, the client's own word. The labels live once, in `BookingDisplay.AgeLabel` — the completion form and the admin page each kept their own copy, now delegating to it — and both age dropdowns list the enum (`Enum.GetValues`) rather than a hand-kept array. Data migration `TwoAgeGroups`: the old 4–12 rows become `Adult` first, then the old `Infant` rows become `Child`; exact, because the 0–3 band is the same on both sides. The SRS §5.1/5.2 note claimed the tier "is derived from" the age category, which read as if the system priced by age; it now says the admin reads it off the participant list (C8).

**Docs.** §5.1, §5.2, FR-57, FR-58, §8.5, `erd.md`. **Terms / privacy.** None — neither names an age band; the policy's "ålderskategori" stays true. **Azure cost.** None.

---

## Manual test before the prod deploy

What has changed since the code production runs (`a903db5`) and has not yet been tested by hand. **Every built item adds its block here, in the same commit as the code**, scoped to what that commit changed — nothing older, which was tested before it went live. Tick a box when it passes; a failure is fixed and the box retested. After the prod deploy, delete the blocks that shipped.

Run locally (Mailpit at `http://localhost:8025`) unless a block says staging. Dates are chosen clear of the demo seed, which places its bookings within ~4 months of the reseed day.

**Item 5 — one cleaning day** (`5261ee4`)
- [ ] `/boka`: request a stay **2027-06-02 → 2027-06-05**. Admin → Bokningar → open it, tick a building, **Godkänn** with any price.
- [ ] `/kalender`, June 2027: **5 juni** is Upptaget, **6 juni** is Ledigt.
- [ ] `/boka`: request **2027-06-06 → 2027-06-08** → accepted. Admin → open it, tick a building, **Godkänn** → succeeds (arrive the day after the previous group leaves).
- [ ] `/boka`: request **2027-05-30 → 2027-06-02** → refused: *"Datumet 2 juni är tyvärr upptaget. Den 2 juni är er avresedag, och den behöver också vara ledig eftersom vi gör i ordning gården mellan grupperna. Välj gärna en tidigare avresa eller andra datum."*
- [ ] `/kalender` intro says *"Avresedagen är alltid avsatt för städning, så nästa grupp kan komma tidigast dagen efter."*
- [ ] Admin → **Kalendervy**: no **Städdagar** column, on desktop and at phone width; intro says the check-out day is held automatically.
- [ ] Admin → **Handbok** → *Godkänna en förfrågan*: "…kan nästa grupp komma tidigast på **måndag**."

**Items 3 + 4 — contact details and sender** (`093a4f5`)
- [ ] `/kontakt`: E-post **info@akerogarden.se**; Telefon **073-952 78 73** (on a phone, tapping it dials +46 73 952 78 73); Adress *Åkerögården / Åkerövägen 81 / 761 94 Norrtälje*; the map's link opens Google Maps on Åkerögården.
- [ ] Footer of any public page: **info@akerogarden.se**.
- [ ] `/bokningsvillkor` → Kontakt: *Åkerövägen 81, 761 94 Norrtälje, Sverige*, *+46 739 527 873*, *info@akerogarden.se*, *Organisationsnummer: 815601-0129*.
- [ ] `/integritetspolicy`: §1 organisationsnummer 815601-0129, the address, info@ and dataskydd@ links; §4 One.com row; §5 email paragraph.
- [ ] Admin → **Handbok** → *Del C · Konton och e-post*: info@ and dataskydd@ in the table.
- [ ] Mailpit, after a `/boka` request: the guest's receipt is from **Åkerögården**; the notification to admin is from **the guest's name** and Reply-To is the guest.
- [ ] Mailpit, after Admin → Administratörskonton → **+ Bjud in användare**: the invitation is from **Åkerögården**.

**Item 9 — signature, and the address in guest emails** (PR 117 `93cd0e4`; address `093a4f5`)
- [ ] In Mailpit, these end in the signature (logo, Åkerögården, Telefon link, E-post link, akerogarden.se, Integritetspolicy): guest receipt, admin notification, approval email, admin invitation, and the reset from `/glomt-losenord`.
- [ ] Complete a booking through its completion link → the *details received* email's **Adress** reads **Åkerövägen 81, 761 94 Norrtälje**.
- [ ] Start admin with `DemoSeed__Reset=true` and `Jobs__RunOnStartup=true` → the 14-day reminder to *ingrid.paminnelse@example.com* has **Adress: Åkerövägen 81, 761 94 Norrtälje**.
- [ ] **Staging**, real inboxes: the signature in **Gmail** and **Outlook** shows the logo without "load images", and stays readable in **dark mode**.

**Items 6 + 12 — one menu, dish diet** (branch `feat/one-menu-diet-label`)
- [ ] **Migration, before reseeding.** Start admin locally on the existing database (no `DemoSeed__Reset`) → Admin → **Matalternativ**: no Måltid column; a **Kost** column; the lunch dishes keep 1–5 and the dinners now read **6–10** in the old order; both *Wokade grönsaker med nudlar* rows show **Vegetarisk**, every other dish **–**.
- [ ] Same page: **Redigera** a dish → panel has **Kost** (Ingen / Vegetarisk / Vegansk), no Måltid. Set *Pannkakor med sylt och grädde* to **Vegansk** → **Spara** → the row shows **Vegansk**.
- [ ] `/mat-och-priser`: one list with no Lunch/Middag headings; at a wide window two columns reading down then across, at phone width one; *Pannkakor* labelled **Veganskt** (only), the noodles **Vegetariskt**. Set Pannkakor back to **Ingen** afterwards.
- [ ] `/boka`: request **2027-07-05 → 2027-07-08**, full board. Admin → open it, tick a building, **Godkänn** with any price → **Skicka slutförandelänk nu**. Open the link from Mailpit: the **tisdag 6 juli** Lunch dropdown lists *Grillad kycklingfilé med champinjonsås* (a former dinner) and the Middag dropdown lists *Panerad fisk med dansk remouladsås* (a former lunch). Choose those two, fill the rest, submit → acknowledgement.
- [ ] Admin → that booking → **Gruppens rätter**: *2027-07-06 lunch* shows the chicken fillet and *2027-07-06 middag* the fish; change one to another dish → saved. **Deltagar- och matlistor**: the 6 juli lunch and dinner show those dishes.
- [ ] **Staging, after the deploy** (temporary IP rule): Matalternativ as in the first check — staging's migrated data, not a fresh seed.

**Item 8 — admin gets the guest's summary** (branch `feat/admin-full-summary`)
- [ ] `/boka`: request **2027-08-02 → 2027-08-04**, full board, with a phone number. Admin → open it, tick **Smedjan**, **Godkänn** with any price → **Skicka slutförandelänk nu**. Open the link from Mailpit, add two participants, give one **Nötallergi**, tick the allergy consent, choose the dishes, submit → acknowledgement.
- [ ] Mailpit, **Bokningsuppgifter inskickade (AG-…)** to admin: reference; *Kontaktperson* with name, e-post and the phone you typed; Incheckning/Utcheckning with times; **Boende: Smedjan**; *Antal deltagare (övernattning): 2*; **Allergier: Nötallergi (1)**; the meals 2–4 augusti with the dishes chosen; *Namn och vem som har vilken allergi står i deltagar- och matlistorna…*; **Öppna bokningen i adminpanelen** opens that booking. **No** participant name, **no** *Adress*, **no** *Behöver något ändras?*.
- [ ] Mailpit, **Vi har tagit emot era uppgifter (AG-…)** to the guest: *Adress: Åkerövägen 81, 761 94 Norrtälje*; **Boende: Smedjan**; the same participants and meals as the admin's copy; ends in *Behöver något ändras?…*; no admin link, no Kontaktperson.
- [ ] Same flow, but on a second booking (**2027-08-09 → 2027-08-11**) give one participant **no box ticked** and only the note *selleri* → the admin email says **Allergier: Annat (1)** and does not contain *selleri*; the guest's email says **Allergier: selleri** for that person (it used to say *—*).
- [ ] Admin → that booking → **Deltagarlista** (PDF and Excel): the person's allergy column reads **selleri** (it used to read *—*); **Matlista**: listed under kostbehov with **selleri**, as before.
- [ ] Admin → **Handbok** → *Mejlen systemet skickar* → *Till er*: the **Uppgifter klara** row says names and who has which allergy are not in the email, and why.

**Site addresses in one `Site` section** (this branch, after `767365b`) — every absolute link is now built from `Site:PublicBaseUrl` / `Site:AdminBaseUrl`, so check each kind of link once:
- [ ] Mailpit: the approval email's completion link opens `/slutfor/…` on the public site; the terms link opens `/bokningsvillkor`.
- [ ] Mailpit: the admin notification's booking link opens that booking in the admin panel.
- [ ] Mailpit: an invitation's and a `/glomt-losenord` reset's link opens `/aterstall-losenord` on the admin panel.
- [ ] Admin → open a booking with an issued link: the completion link shown there opens the same page. Admin → **Skapa bokning**: the link to the public form opens `/boka`.
- [ ] Public: `/robots.txt` and `/sitemap.xml` name the public site's own address; a page's source has `<link rel="canonical" href="…">` on it.
- [ ] **Staging, after the Bicep apply**: one approval email's completion link and `/robots.txt` point at `staging.akerogarden.se`.

**Item 10 replaced — overdue deposit made visible** (branch `feat/overdue-deposit-visibility`). Start admin with `DemoSeed__Reset=true`: the seed has two overdue deposits, **Mikael Ström** (due 2 days before the reseed day) and **Lena Forsberg** (1 day), and one still within its time, **Sara Lindqvist**.
- [ ] Admin → **Bokningar**: Mikael Ström and Lena Forsberg show a red **Handpenning förfallen** badge; Sara Lindqvist shows *Väntar på handpenning*. Same at phone width (cards).
- [ ] Admin → **Kalendervy**: the same three badges.
- [ ] Admin → **Översikt** → *Väntar på handpenning*: under the heading *"Förfallna bokningar håller sina datum tills ni bekräftar eller avbokar dem."*; Mikael Ström and Lena Forsberg show **Handpenning förfallen [datum]**, Sara Lindqvist **Senast [datum]**.
- [ ] Open **Mikael Ström**: the header badge reads **Handpenning förfallen**; the red alert reads *"Bokningen håller fortfarande [incheckning] – [utcheckning] — ingen annan kan boka de datumen. En påminnelse skickas till gästen i natt. Bekräfta bokningen om pengarna har kommit in, annars avboka den för att frigöra datumen."* with the booking's own dates.
- [ ] Open **Sara Lindqvist**: blue *Väntar på handpenning* badge, no alert. Admin → Översikt: with only Sara left waiting (confirm the two overdue ones), the hint line is gone.
- [ ] Admin → **Handbok** → *Begära handpenning*: the "Kommer pengarna inte?" paragraph names the red badge on Bokningar and Kalendervy too.

**Item 7 — link 21 days before arrival** (branch `feat/link-at-three-weeks`). Start admin with `DemoSeed__Reset=true` and `Jobs__RunOnStartup=true`.
- [ ] Mailpit: **Elsa Wikander** (check-in exactly 21 days after the reseed day) gets *"Nu kan ni fylla i era uppgifter"* from the startup run; the link's expiry is **14 days before** her check-in, kl. 14:00. Admin → **Jobbhistorik**: that night's link-issuance row says *"…när incheckningen kom inom 21 dagar."*
- [ ] Admin → open **Nils Åkerlund** (far out): the rail reads **Slutförandelänk: Skickas automatiskt 21 dagar före ankomst.**
- [ ] `/boka`: request **2027-09-06 → 2027-09-08**. Admin → open it, tick a building, **Godkänn** → the dialog says *"Slutförandelänken skickas automatiskt 21 dagar före ankomst."*; after confirming, the toast says the same. Mailpit: the approval email has **no link** and says *"…skickas till er 21 dagar före ankomst. Fram till dess behöver ni inte göra något."*
- [ ] Admin → open **Johan Bergström** (17 days out): the rail shows a live link valid to **14 days before** his check-in. Open **Per Holmgren** (9 days out): the rail shows *"Kort varsel — 9 dagar till incheckning"*. **Godkänn** with any price → the dialog promises the link *"med slutförandelänk"*; the toast says the link is shortened; Mailpit: the approval email carries the link, valid to **3 days before** his check-in.
- [ ] Admin → **Handbok**: *Godkänna en förfrågan* says the link comes with the confirmation when at most **21 dagar** remain, otherwise at 21 dagar; *Mejlen systemet skickar*: the **Bokningsbekräftelse** and **Länk till formuläret** rows say **21 dagar**.

**Item 17 — two age groups** (branch `feat/two-age-groups`)
- [ ] **Migration, before switching branch.** On `main`, start admin with `DemoSeed__Reset=true` → open **Anna Sjöberg**'s booking → Deltagare → **✎ Redigera** *Olle Sjöberg* → **Barn (0–3)** → **Klar**. *Maja Sjöberg* stays **Barn (4–12)**. Stop admin, switch to this branch, start admin **without** `DemoSeed__Reset` → same booking: *Anna*, *Erik* and *Maja* read **Vuxen (4+)**, *Olle* reads **Barn (0–3)**.
- [ ] Same booking → **✎ Redigera** any participant: the Ålderskategori dropdown has exactly **Vuxen (4+)** and **Barn (0–3)**.
- [ ] `/boka`: request **2027-10-04 → 2027-10-06**, full board, 2 guests. Admin → open it, tick a building, **Godkänn** with any price → **Skicka slutförandelänk nu**. Open the link from Mailpit: each participant's age dropdown has exactly **Vuxen (4+)** (selected) and **Barn (0–3)**. Set the second to **Barn (0–3)**, fill the rest, submit → acknowledgement.
- [ ] Mailpit, **Vi har tagit emot era uppgifter**: the second participant reads **Barn (0–3)**. Admin → that booking → **Deltagarlista** PDF and Excel: Ålder column **Vuxen (4+)** / **Barn (0–3)**.
- [ ] **Staging, after the deploy** (temporary IP rule): open a booking with participants → ages read **Vuxen (4+)** / **Barn (0–3)**, none blank.

## To-do lists

**Client:** rename "Husvagn" in admin (item 2); deactivate duplicate dishes after item 12 is deployed (e.g. the second *Wokade grönsaker med nudlar*), and renumber Visningsordning if wanted; optionally a photo of the caravan pitch; answer item 11's four questions; be told that the admin's summary email shows participants and allergies as counts, not names (item 8).

**Maintainer:** one.com access for the client, with the DNS warning (item 1).

**Deploy order for this branch (site addresses).** The app now reads `Site__PublicBaseUrl` / `Site__AdminBaseUrl`; the App Service settings are still named `BookingEmail__…` until `main.bicep` is applied. Whichever lands first, the other half runs for a few minutes with an empty site address — links in any email sent in that window are broken. So apply the Bicep and the app deploy back to back, per environment:
- ✅ **Staging:** done 2026-11-12 — `main.bicep` applied right after the staging deploy (Succeeded, settings verified).
- **Production:** apply `main.bicep` to production immediately before approving the production deploy, at ~22:00 and well clear of 03:00. Run what-if against production first.
- The same apply also drops the unused `BookingEmail__ContactEmail` setting.
