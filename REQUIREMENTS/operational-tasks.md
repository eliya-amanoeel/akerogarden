# Åkerögården — Operational Tasks

One-off content, migration, and go-live tasks that aren't system requirements — they don't belong in requirements.md, but need a home now that README.md is being retired. Tracked here so nothing gets lost.

---

## Open Items

- [ ] **Privacy policy legal text — must be (re)written before go-live.** The original approved copy lived only in the production Payload CMS database, which was already dropped during the server decommission — it's gone. There is no saved export, doc, or backup to carry over.

  New copy must be drafted (or a lawyer/Swedish-GDPR-literate reviewer engaged) covering the facts previously confirmed in README:
  - Data controller details (Åkerögården)
  - Swedish acknowledgment line at booking submission (previous wording: *"Genom att skicka godkänner du vår integritetspolicy."*)
  - Data retention: 2 years for completed stays, 12 months for denied/pending/cancelled (per requirements.md Section 2.6)
  - Rights contact: `info@akerogarden.se` (see also the deferred `dataskydd@akerogarden.se` item)
  - IMY (Integritetsskyddsmyndigheten) reference
  - Confirmation that personnummer is not collected

  **This blocks go-live** — NFR-20–24 in requirements.md require the policy to exist and cover these points, but there is currently no actual document satisfying them.
- [ ] **Cookie consent — assessed: no consent banner required; two small follow-ons.** The public site sets only **strictly-necessary** cookies — the admin auth cookie (login-gated) and the antiforgery/CSRF cookie — and **no analytics/tracking/marketing cookies** (no Google Analytics/gtag, Matomo, or Plausible; fonts are self-hosted per NFR-35, so no third-party font requests either). Under the ePrivacy Directive + the Swedish *Lag om elektronisk kommunikation*, consent is only required for **non-essential** cookies, so **no interactive consent gate is needed as things stand.** Residual actions: (1) add a short **"Kakor"** section to the privacy policy (above) describing the necessary cookies — informational, not a consent gate; (2) **watch third-party embeds** — the moment a Google Maps iframe, YouTube embed, or social widget is added to a public page it sets third-party cookies and *would* require a consent gate, so prefer a static map image + link. **Re-assess if analytics or any third-party embed is ever introduced.**
- [ ] **Azure cost + health alerts (staging and production).** No alerting exists yet. Set up, ideally codified in Bicep (Azure Monitor / `Microsoft.CostManagement` budgets + `Microsoft.Insights` metric alerts) rather than clicked in the portal:
  - **Cost / budget alerts** — a monthly budget per resource group (`akerogarden-staging`, `akerogarden-prod`) with email alerts at e.g. 50% / 80% / 100% of threshold. Staging especially, since it's meant to be powered down when idle — an alert catches a box left running.
  - **App Service health** — alerts on HTTP 5xx rate, response time, and app restarts/availability (so a crash-looping deploy is noticed without watching the portal).
  - **PostgreSQL** — alerts on high CPU/storage (Burstable B1ms is small), failed connections, and approaching storage limit.
  - **Key Vault** — alert on access failures (would surface a broken managed-identity/access-policy wiring like the one hit during Phase 0 provisioning).
  - **Optional** — Action Group wiring so all the above notify a real inbox/SMS; consider Application Insights for richer telemetry.

  Not go-live-blocking on its own, but production should not run unmonitored. Staging cost/health alerts belong in Phase 7 (hardening) and are worth doing sooner; production monitoring is wired up with the production environment at Phase 10 (go-live).
- [ ] **Occasional UI lag observed at the Phase 8 client demo — verify warm, act only if it reproduces.** During the demo (2026-07-09) the client noticed intermittent lag a couple of times on **both** the public site and the admin panel on **staging**. Most likely **environmental, not a code defect**: staging is App Service **B1** and is **powered down when idle**, so the first hits pay a cold-start, and the admin (Interactive Server) additionally waits for the SignalR circuit to warm up. Public-page perf was measured **PASS** on staging in Phase 7 (NFR-08: all pages 0.12–0.23 s vs the 3 s target). **Action:** confirm whether the lag reproduces on a **warm** instance / production-sized environment. If it only appears after idle → expected, no code change. **If it reproduces warm → it becomes a real Phase-8 (or go-live) bug to investigate** (candidate causes: circuit reconnection, an unindexed query, cold JIT). Tie the production check to the Phase 10 prod-perf verification (NFR-08/09/10). *(Diagnosis/verification item — not a content blocker.)*
