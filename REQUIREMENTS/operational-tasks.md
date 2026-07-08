# Åkerögården — Operational Tasks

One-off content, migration, and go-live tasks that aren't system requirements — they don't belong in requirements.md, but need a home now that README.md is being retired. Tracked here so nothing gets lost.

---

## Open Items

- [ ] **Spelrum (game room) facility — upload images.** Photos already exist — just need to be sourced from the admin and uploaded once the facility record exists in the new system. This is the "Spelrum" game room in requirements.md (foosball, pinball, shuffleboard). Name and scope are now final (Q9/Q10 resolved): the facility is named **Spelrum**, is selectable on the booking inquiry form, and its cleaning is included in its 1 500 kr/day facility fee (not a separate cleaning line). No longer blocked — ready once the Facility record is created.
- [ ] **Image migration.** README's stated source path (`old_source/wp-content/uploads/`) doesn't actually exist in this repo. The real source material currently on hand is at repo root:
  - `/images/chappell.jpg` → Kapell (chapel) facility image
  - `/images/about.jpg` → Om oss page image
  - `/images/welcome.jpg`, `/images/welcome-1.jpg` → homepage hero candidates
  - `/logo/*.svg` → already covered separately in design.md, not part of this task

  **Not yet complete** — more source images exist outside this repo (other facilities, additional hero candidates) and still need to be gathered before the full migration can be scoped. Once gathered, upload everything via the new system's media/blob storage (Azure Blob) and wire each Facility image to its `Facility` record. Hero and about-page images are hardcoded public-page assets — there is no `SiteSettings` entity (it was dropped in requirements v0.13), so those images are wired directly into the page markup, not an admin-managed field.
- [ ] **Privacy policy legal text — must be (re)written before go-live.** The original approved copy lived only in the production Payload CMS database, which was already dropped during the server decommission — it's gone. There is no saved export, doc, or backup to carry over.

  New copy must be drafted (or a lawyer/Swedish-GDPR-literate reviewer engaged) covering the facts previously confirmed in README:
  - Data controller details (Åkerögården)
  - Swedish acknowledgment line at booking submission (previous wording: *"Genom att skicka godkänner du vår integritetspolicy."*)
  - Data retention: 2 years for completed stays, 12 months for denied/pending/cancelled (per requirements.md Section 2.6)
  - Rights contact: `info@akerogarden.se` (see also the deferred `dataskydd@akerogarden.se` item)
  - IMY (Integritetsskyddsmyndigheten) reference
  - Confirmation that personnummer is not collected

  **This blocks go-live** — NFR-20–24 in requirements.md require the policy to exist and cover these points, but there is currently no actual document satisfying them.
- [ ] **VAT rates per category — confirm with the client's accountant.** Prices are stored ex-VAT with a per-item `VatRate` (on each `PriceRule` and `CleaningZone`) because rates differ by category — e.g. 12% for lodging and food, potentially 25% for facility/conference hire (requirements.md §5). The exact rate per category is admin-entered configuration and must be **confirmed with the client's accountant** before the reference data is finalised, then entered via the admin CRUD screens. Named as a Phase-7 blocker in the implementation plan; previously only a prose aside in requirements.md §5 with no tracked home — logged here so it isn't lost. *(Not code — an external/data-entry input.)*
- [ ] **Azure cost + health alerts (staging and production).** No alerting exists yet. Set up, ideally codified in Bicep (Azure Monitor / `Microsoft.CostManagement` budgets + `Microsoft.Insights` metric alerts) rather than clicked in the portal:
  - **Cost / budget alerts** — a monthly budget per resource group (`akerogarden-staging`, `akerogarden-prod`) with email alerts at e.g. 50% / 80% / 100% of threshold. Staging especially, since it's meant to be powered down when idle — an alert catches a box left running.
  - **App Service health** — alerts on HTTP 5xx rate, response time, and app restarts/availability (so a crash-looping deploy is noticed without watching the portal).
  - **PostgreSQL** — alerts on high CPU/storage (Burstable B1ms is small), failed connections, and approaching storage limit.
  - **Key Vault** — alert on access failures (would surface a broken managed-identity/access-policy wiring like the one hit during Phase 0 provisioning).
  - **Optional** — Action Group wiring so all the above notify a real inbox/SMS; consider Application Insights for richer telemetry.

  Not go-live-blocking on its own, but production should not run unmonitored. Staging cost/health alerts belong in Phase 7 (hardening) and are worth doing sooner; production monitoring is wired up with the production environment at Phase 9 (go-live).
