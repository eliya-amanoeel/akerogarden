# Open items

The one list of open work on the system: what the maintainer still has to do, what is planned or only an
idea, and what is parked until something specific happens. If it is open and it is not here, it is in one
of the three places below — and nowhere else.

| Kind of item | Where it lives |
|---|---|
| A question waiting on the client | SRS [§12 Open Questions](product/requirements.md) |
| Scope decided *not now* | SRS [§10 Out of Scope](product/requirements.md) |
| A duty that recurs on a schedule (the quarterly restore drill) | [`infra/ops/operations.md`](../infra/ops/operations.md) → *Recurring duties* |

**How this list is kept.** One item, one place: an item here is not repeated in another document, and
another document points here rather than restating it. A finished item is **deleted**, not ticked — its
outcome goes where it belongs (the PR, the runbook's log, the SRS), and git history keeps the rest. A parked
item says what reopens it; if it cannot say, it is not parked, it is dropped.

**This file is public** (the GitHub mirror): no secrets, guest data or client-confidential detail.

---

## To do

- **Re-measure the database's backup storage — overdue since ~2026-10-14.** Backup retention went 7 → 35
  days on 2026-09-09 (security finding #15) and the free allotment equals provisioned storage, 32 GB.
  `backup_storage_used` was 7.0 GiB on 2026-09-10, but that is steady state for the *old* 7-day window and
  says nothing about the 35-day one, which completed its first cycle ~2026-10-14. If it lands near 32 GB,
  the SRS cost table (§11) needs a real number instead of its "no longer confirmed free" note.

- **Error pages for a few minutes after every deploy — candidate fix: "run from package".** After each
  production deploy the public site throws low-level runtime errors for ~2–3 minutes: `BadImageFormatException`
  (2026-09-15, 10-09, 10-10) and, on 2026-10-13, `IndexOutOfRangeException` inside `AkeroSecurityHeaders`,
  giving two home-page 500s. Low severity. **Likely cause, not proven:** `AzureWebApp@1` zip-deploys by
  overwriting files in `wwwroot` while the old instance still serves, so it reads half-replaced assemblies.
  **Fix:** `deploymentMethod: runFromPackage` on both `AzureWebApp@1` tasks (prod and staging pipelines),
  which sets `WEBSITE_RUN_FROM_PACKAGE=1` and swaps the package in atomically. **Check first** that neither
  app writes into its own folder at runtime (`wwwroot` becomes read-only). Try on staging, deploy twice,
  confirm no post-deploy exceptions in App Insights. The same fault in the logging path, outside deploy
  windows, is the 2026-09-09 open observation in the [post-launch review](archive/post-launch-security-review.md);
  **revisit that part if** its rate climbs independently of deploys, it appears on the admin host, or a real
  incident turns out to have gaps in its logs.

- **Tell the client three things** (from the [2026-11-11 client review](archive/demo-feedback-2026-11-11.md)):
  - One.com access for them — and that **the DNS records there run the live site** (App Service records,
    SPF/DKIM, and the Google verification TXT, which must never be deleted).
  - The admin's summary email shows participants and allergies as **counts, not names** (item 8).
  - Forward the four cancellation-terms questions in SRS §12 (item 11).

- **For the client to do in admin** (the same review): rename "Husvagn" (e.g. "Husvagnsplats med el"),
  optionally with a photo of the pitch; deactivate the duplicate dish (*Wokade grönsaker med nudlar* is
  there twice); renumber Visningsordning if wanted. *Good to know when talking to guests:* those approved
  more than 40 days ahead before the 2026-11-15 deploy were told the completion link comes "cirka sex veckor
  före ankomst"; it now comes at 21 days. If one asks, send it early (**Skicka slutförandelänk nu**).

- **Font-size scale — 45 distinct sizes in `site.css`, many near-duplicates.** Found in the 2026-10-03
  style audit: 0.8 / 0.8125 / 0.82rem · 0.85 / 0.875 / 0.88rem · 0.9 / 0.92 / 0.925rem · 0.7 / 0.72rem ·
  1.02 / 1.05 / 1.0625rem. `design.md` calls near-duplicates drift but defines a scale only for headings and
  body, not the small UI sizes. Fix: define the small scale first (as tokens), then move every rule onto it.
  It touches every page of both sites with visible pixel shifts, so its own branch. Low severity — hygiene.

## Planned, not started

- **Dataskydd view in admin** (planned 2026-10-02). Goal: the admin sees who has written to dataskydd@
  asking about their data, and whether each request is handled.
  - **Page:** menu item **Dataskydd**, Administratör only. Lists **Ohanterade** (sender name and address,
    received, subject, **Svar senast** = received + 1 month) and **Hanterade** (recent), with **Markera som
    hanterad / ohanterad**. No message bodies — those are read in webmail.
  - **Handled = moved to an IMAP folder "Hanterade"** in the mailbox. No personal data in our database; the
    mailbox and the admin always agree; reading a mail in webmail does not make it handled. Each move writes
    an audit entry, with no PII.
  - **Reading:** IMAP via MailKit (already used for SMTP). The page reads the mailbox only when opened — no
    menu badge in v1 (that would mean IMAP on every page load; add it later with a short cache).
  - **A login failure is shown as a clear error**, never as an empty list — a silent "no requests" is the
    info@-password trap again.
  - **Config/infra:** IMAP host, user and password per environment, the password in Key Vault via Bicep.
    Production reads dataskydd@; staging reads staging@akerogarden.se (already staging's sender and
    notification address — other mail landing there shows up as "requests", which is fine for testing).
  - **Dev/tests:** a fake mailbox behind an interface (Mailpit has no IMAP); unit (deadline, mapping),
    integration and E2E against the fake.
  - **Docs:** a new FR in the SRS; a handbook section "Dataskydd"; extend the "byt inte lösenordet" warning
    to dataskydd@.
  - **Before building:** the maintainer puts the dataskydd@ password into production Key Vault; confirm
    IMAP is enabled for dataskydd@ and staging@ at One.com (imap.one.com:993 assumed — verify, don't assert).

## Ideas, not decided

- **"Händelser": merge Jobbhistorik (FR-114) into the deferred audit view (FR-97, SRS §10).** One global page
  of all audit entries (actor / action / date / reference filters) where nightly runs are rows with their
  Lyckades/Misslyckades badge *including runs that did nothing* (from `JobRun`, which an audit-only page would
  lose), plus a per-booking activity list on the booking page; Jobbhistorik is then removed. Open decisions:
  Koordinator access; run rows go after 6 months while their entries stay 2 years. Jobbhistorik already pages
  (`AdminPager`, ten nights a page); a page holding every admin and guest action would need the same. ~10–14 h
  including SRS, ERD and handbook.

## Parked — reopen when the trigger fires

- **Admin-action emails ignore a failed send** (out of scope by decision, 2026-10-05). Godkänn, Skicka ny länk,
  Avslå, Avboka, Markera avslutad and invitations all report "skickat" even when the send failed; **Skicka ny
  länk** also invalidates the guest's working link first. A full design exists (red sticky toast + audit entry
  + restore the link on resend; password reset and guest-triggered mails deliberately unchanged). Left out
  because the audit entry would be invisible while FR-97's viewer is deferred. **Reopen with** the audit view
  ("Händelser" above).

- **Production admin failed to start twice on the 2026-10-04 deploy — cause unknown.** The container never
  opened port 8080 within the 230 s limit (19:39 and 19:45 UTC) and Azure stopped the site; the public app's
  deploy also took 7+ min. Staging started the same build in 67 s; the production database, Redis and plan
  were healthy; production came up later on its own or after a restart. The app's own startup output was not
  visible (App Service log capture is off; App Insights queries hung). **Reopen if it recurs** — and then turn
  on container logging (`az webapp log config --docker-container-logging filesystem`) *before* the next
  deploy and read `LogFiles/*_default_docker.log`.

- **Post-launch review #16 — every 404 throws a router exception.** Log noise, no user impact: 404s return
  404 correctly. The likeliest conflict is that both `NotFoundPage` and `UseStatusCodePagesWithReExecute` are
  wired. **Reopen if** it rises or reaches a user.

- **Post-launch review #13 — telemetry at 55–60 % of the daily ingestion cap.** `AppPerformanceCounters` is
  off; nothing is broken. **Reopen if** the cap is ever hit.

- **Post-launch review #2 — the structural fix: `AddDbContextFactory`.** The admin host shares one
  `DbContext` per Blazor circuit; what shipped removed the trigger of the `PK_AuditLogs` crash, not the
  possibility. The proper fix — a context per operation — touches the whole service layer and every test that
  composes one. **Do it first, before adding more guards, if** the admin panel ever gains concurrent users,
  background work writing through the same context, or a second instance.
