# Åkerögården — Operational Tasks

One-off content, migration, and go-live tasks that aren't system requirements — they don't belong in requirements.md, but need a home now that README.md is being retired. Tracked here so nothing gets lost.

---

## Open Items

- [ ] **Playroom / Spelrum facility — upload images.** Photos already exist (not yet taken from scratch) — just need to be sourced from the admin and uploaded once the facility record exists in the new system. This is the same room as the "Spelrum" game room in requirements.md (foosball, pinball, shuffleboard) — **blocked on Q9** (final name: Spelrum, Lekrum, or other) and **Q10** (whether it's selectable in the booking inquiry form and the cleaning checklist) being resolved first, since the facility record's name and scope aren't final.
- [ ] **Image migration.** README's stated source path (`old_source/wp-content/uploads/`) doesn't actually exist in this repo. The real source material currently on hand is at repo root:
  - `/images/chappell.jpg` → Kapell (chapel) facility image
  - `/images/about.jpg` → Om oss page image
  - `/images/welcome.jpg`, `/images/welcome-1.jpg` → homepage hero candidates
  - `/logo/*.svg` → already covered separately in design.md, not part of this task

  **Not yet complete** — more source images exist outside this repo (other facilities, additional hero candidates) and still need to be gathered before the full migration can be scoped. Once gathered, upload everything via the new system's media/blob storage and wire each image to its corresponding Facility, Page, or SiteSettings field.
- [ ] **Privacy policy legal text — must be (re)written before go-live.** The original approved copy lived only in the production Payload CMS database, which was already dropped during the server decommission — it's gone. There is no saved export, doc, or backup to carry over.

  New copy must be drafted (or a lawyer/Swedish-GDPR-literate reviewer engaged) covering the facts previously confirmed in README:
  - Data controller details (Åkerögården)
  - Swedish acknowledgment line at booking submission (previous wording: *"Genom att skicka godkänner du vår integritetspolicy."*)
  - Data retention: 2 years for completed stays, 12 months for denied/pending/cancelled (per requirements.md Section 2.6)
  - Rights contact: `info@akerogarden.se` (see also the deferred `dataskydd@akerogarden.se` item)
  - IMY (Integritetsskyddsmyndigheten) reference
  - Confirmation that personnummer is not collected

  **This blocks go-live** — NFR-20–24 in requirements.md require the policy to exist and cover these points, but there is currently no actual document satisfying them.
