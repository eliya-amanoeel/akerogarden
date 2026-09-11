# DNS + one.com Cutover Runbook (Phase 10d — go-live)

**Purpose:** move `akerogarden.se` and `admin.akerogarden.se` from the current one.com/WordPress hosting to the production App Service, without disrupting email or the domain registration.
**Who runs it:** requires access to the one.com DNS control panel and Azure CLI logged in with rights on `akerogarden-prod` (in the client's Azure tenant — see Phase 10a/10b). (Claude drafted this; you execute the DNS edits and `az` commands.)

**Staging's own DNS (Phase 10a) is simpler and covered separately below** — it's new records only, no WordPress hosting to retire, since staging never had a custom domain.

---

## ⚠️ Read before touching one.com

one.com is the **registrar + DNS host + email provider** for `akerogarden.se`. This cutover is **DNS-record surgery only** — it retires the WordPress *web hosting*, nothing else.

**Never touch, at any step:**
- The domain registration itself
- `MX` records
- `SPF` / `DKIM` / `DMARC` records
- Any mailbox (`info@akerogarden.se`, `noreply@akerogarden.se`, etc.)

The app sends every guest/admin email through One.com SMTP (`send.one.com`) authenticating as a mailbox on this domain — breaking those records silently kills all mail, and DMARC/SPF changes can also land future mail in spam (see the Phase 8 spam-placement note in the implementation plan).

**What *does* change:** the apex `A` record, the `admin` subdomain's `CNAME`, the `asuid` `TXT` record (Azure domain-ownership verification), a temporary `asuid.admin` `TXT` (removed again after binding — see Part C), and the `www` handling. That's it.

---

## Step 0 — Variables

```
RG              = akerogarden-prod
PUBLIC_APP      = jmk-ag-prod-public                    # serves the apex akerogarden.se
ADMIN_APP       = jmk-ag-prod-admin                     # serves admin.akerogarden.se
APEX            = akerogarden.se
ADMIN_HOST      = admin.akerogarden.se
PUBLIC_DEFAULT  = jmk-ag-prod-public.azurewebsites.net  # public site default hostname
ADMIN_DEFAULT   = jmk-ag-prod-admin.azurewebsites.net   # admin site default hostname
```

**Two-app topology (Phase 10b, FR-70):** production is two App Service sites on one B1 plan — the apex binds to the **public** site, `admin` to the **admin** site. Both sites share the plan's inbound IP; App Service routes by `Host` header, so the apex `A` record and the `admin` `CNAME` both resolve to the same scale unit but land on different sites via their hostname bindings.

`staging.akerogarden.se` / `admin.staging.akerogarden.se` are **handled separately** — see "Staging DNS" below. This runbook's Parts A–F are production-only.

---

## Part A — A day ahead: lower TTLs

In the one.com DNS panel, lower the TTL on the records you're about to change (apex `A`, `www`) to something short (e.g. 300s/5 min), and let the old TTL expire before proceeding. This bounds how long clients can be stuck resolving the old WordPress IP after cutover. Do this **the day before**, not the day of.

> **What actually happened at the production cutover (2026-09-07): this step was skipped entirely, and it did not matter.** The apex records were never editable in the first place (see Part C), so there was no TTL to lower — the new root `A` was *created* with its own TTL, and one.com's minimum is **600s, not 300**. Every public resolver (Cloudflare, Google, Quad9) returned the new address within seconds of the create, because the implicit record being replaced had never been cached under an explicit name. **Do not treat Part A as a blocker.** Lower the TTLs if there are editable records to lower; if there are not, proceed and measure propagation directly.

---

## Part B — Get the Azure-side target values

Prod must exist (Bicep deployed) before this step — these values come from the live resource.

> ⚠️ **`inboundIpAddress` returns `null` in this subscription — do not wait for it to populate.** Verified 2026-09-07 against both prod apps *and* against staging, which has a working bound custom domain: `inboundIpAddress`, `possibleInboundIpAddresses` and `virtualIp` are all `null` regardless of binding state. This is not a "bind the hostname first" ordering problem. **Get the address from DNS instead** — resolve the app's default hostname, which is the scale unit's inbound VIP and is exactly what the apex `A` record needs.

```bash
# Apex A-record target. Resolve the default hostname — the ARM property is null here (see above).
# Both prod sites share one B1 plan, so this single address serves the apex and the admin host.
dig +short jmk-ag-prod-public.azurewebsites.net | tail -1
#   → 51.12.73.224 at the 2026-09-07 cutover. Re-resolve; it can change if the app is
#     recreated or moved to another scale unit.

# Cross-check (should contain the value above):
az webapp show -g $RG -n $PUBLIC_APP --query outboundIpAddresses -o tsv

# Custom Domain Verification ID for both asuid TXT records. This is SUBSCRIPTION-scoped, so it is
# the SAME value for apex, admin, and every site in the subscription — one value covers both records.
az webapp show -g $RG -n $PUBLIC_APP --query customDomainVerificationId -o tsv
```

Record both outputs — you'll paste them into one.com in Part C.

---

## Part C — Add the DNS records at one.com

An apex domain cannot use `CNAME` (DNS spec restriction), so apex and subdomain use different record types:

| Host | Type | Value | Purpose |
|---|---|---|---|
| `@` (apex, `akerogarden.se`) | `A` | *(inbound IP from Part B)* | Points the bare domain at the **public** site |
| `asuid` | `TXT` | *(verification ID from Part B)* | **Permanent.** The only ownership proof the apex has — see the box below |
| `admin` | `CNAME` | `jmk-ag-prod-admin.azurewebsites.net` | Points `admin.akerogarden.se` at the **admin** site |
| `asuid.admin` | `TXT` | *(same verification ID)* | **Temporary.** Speeds up binding; delete after Part D — see the box below |

Leave existing `MX`/`SPF`/`DKIM`/`DMARC`/mailbox records untouched.

> ### `asuid` — the apex record is permanent, the subdomain ones are temporary
>
> Identical value, completely different lifetimes:
>
> - **`asuid.akerogarden.se` is mandatory and permanent.** An apex cannot be a `CNAME`, so the apex is an `A` record and Azure has no mapping to validate against — this TXT is the only ownership proof it has. It is re-checked at **App Service Managed Certificate renewal**, not only at binding, so deleting it fails silently: the apex certificate quietly stops renewing and the site breaks on expiry, months later and with nothing linking the two events.
> - **`asuid.<subdomain>` is optional.** A CNAME-mapped hostname is validated against its own `CNAME`, so the TXT is a redundant second proof. Its only value is *timing* — pre-staged, it satisfies the ownership check before the `CNAME` flips, so binding and certificate issuance start immediately rather than waiting on TXT propagation inside the cutover window.
>
> **So: add the subdomain `asuid` records for a cutover, delete them once the hostnames are bound and the certs issued.** They earn nothing in steady state.
>
> The one thing that would make a subdomain `asuid` load-bearing is the hostname not being a real `CNAME` — a provider that flattens `CNAME`s to `A` records leaves CNAME validation with nothing to read. one.com does not flatten (verified 2026-09-10). Confirm before deleting; the answer must be a hostname, not an address:
>
> ```bash
> dig +short @ns01.one.com admin.akerogarden.se CNAME   # → jmk-ag-prod-admin.azurewebsites.net.
> ```
>
> **State as of 2026-09-10:** `asuid.admin`, `asuid.staging` and `asuid.admin.staging` were **deleted** after binding. All four managed certs (issuer `GeoTrust TLS RSA CA G1`, expiring 2027-03-07) renew off the `CNAME` mapping; only `asuid.akerogarden.se` remains. If you are reading this wondering where they went — nothing is missing.

> ### ⚠️ The apex `A`, `AAAA`, `www` and `MX` records are NOT in one.com's DNS list — and there is nothing to edit or delete
>
> Discovered at the production cutover, 2026-09-07. one.com's **"Personal DNS settings"** list shows only records a human added by hand — at that point the two staging `CNAME`s, `SPF`, `DMARC` and the four `asuid` `TXT`s. The apex `A`, the apex `AAAA`, `www` and `MX` all resolved publicly but appeared **nowhere in the panel**: they are managed implicitly by one.com's web-hosting and mail services.
>
> **So "change the apex A record" is impossible as written.** What works instead:
>
> **Create a new root `A` record.** In `Create new record` → `A` tab, **leave the Hostname field empty** (the form says this creates the record at the root of the domain), point it at the Part B address, TTL 600 (one.com's minimum — it rejects 300).
>
> **Creating it displaces one.com's implicit records — including the `AAAA`.** This is the important part and the happiest surprise of the cutover: immediately after the create, `dig @ns01.one.com akerogarden.se AAAA` returned **empty**. No separate deletion step was needed, and no IPv6 traffic was left stranded on WordPress.
>
> **But verify it, every time.** The apex `AAAA` is the single most dangerous record in this cutover: leave one pointing at the old host and every dual-stack client — most mobile networks — silently keeps reaching WordPress over IPv6 while you stare at a correct `A` record. Always confirm:
>
> ```bash
> dig +short @ns01.one.com akerogarden.se A      # → the Azure address
> dig +short @ns01.one.com akerogarden.se AAAA   # → MUST be empty
> dig +short @ns01.one.com akerogarden.se MX     # → must still list the one.com mailpods
> ```
>
> Query the **authoritative** nameserver (`@ns01.one.com`), not your local resolver. A macOS resolver returned empty for the apex `A` during pre-flight while `@1.1.1.1` answered correctly — nearly mistaken for "the record does not exist".

**Do not delete the old WordPress `A`/`www` records yet** — see Part F (soak period). *(At the 2026-09-07 cutover there were none to delete: the apex was displaced automatically, and `www` was replaced by the Part E web forward.)*

> **The `asuid` `TXT` records should already be in place** — they were staged early, back in **Phase 10c**, because they only prove domain ownership and carry no traffic, so adding them cannot affect the still-live WordPress site. Pre-staging them means Azure's ownership check is already satisfied when the `A`/`CNAME` flip here, and certificate issuance in Part D starts immediately instead of waiting on TXT propagation inside the cutover window. If they are missing, add them now and allow for propagation before Part D. **Expect `asuid.admin` to be missing** — it is deleted in steady state by design (see the box in Part C), so a rebuild starts by re-adding it; the apex `asuid`, by contrast, should never have been absent. **The `A` and `CNAME` records are the only genuinely destructive edits in this runbook** — they are what moves live traffic.

---

## Part D — Bind the custom hostnames + issue SSL

Once the DNS records above have propagated (check with `dig akerogarden.se A` / `dig asuid.akerogarden.se TXT` until they resolve to the new values):

Bind each hostname to its own site, then issue the certs. `main.bicep` declares the per-site
bindings (`publicHostnames` → public site, `adminHostnames` → admin site), so redeploying it with
`prod.parameters.json` creates them — **but that needs the secret parameters again** (Postgres/SMTP/
seed passwords are `REPLACE_VIA_CI_SECRET` placeholders in the param file; passing placeholders would
reset them). To avoid re-handling secrets, bind via the CLI instead (the `asuid` TXT proves ownership,
so binding succeeds as soon as the TXT resolves — the `A`/`CNAME` need not have propagated yet):

```bash
# Bind each custom hostname to its own site (secret-free)
az webapp config hostname add -g $RG --webapp-name $PUBLIC_APP --hostname $APEX
az webapp config hostname add -g $RG --webapp-name $ADMIN_APP  --hostname $ADMIN_HOST

# Then issue + SNI-bind the managed certificates (must run AFTER the A/CNAME resolve to Azure —
# managed-cert issuance validates against DNS; see ssl.bicep header comment). No secrets needed.
az deployment group create -g $RG --template-file infra/azure/ssl.bicep --parameters @infra/azure/prod.ssl-parameters.json
```

`infra/azure/prod.ssl-parameters.json` **exists and is ready** (finalized 2026-07-21): its
`appServicePlanId` is filled in with the real plan resource ID (prod was provisioned 2026-07-20), and
the `bindings` array already pairs `{jmk-ag-prod-public, akerogarden.se}` and `{jmk-ag-prod-admin,
admin.akerogarden.se}`. No edit needed at cutover — just run the deployment above.

> **Gotcha (seen at the staging cutover):** if a hostname already has a managed cert on the plan
> under a different site, `ssl.bicep` fails with `CanonicalName is invalid … duplicate certificate` —
> `az resource delete … Microsoft.Web/certificates` the stale cert first. Not expected for a
> born-fresh prod (no prior site owns these hostnames), but watch for it on any re-point.
>
> ---
>
> **Production result, 2026-09-07 — clean run, ~4 minutes.** `provisioningState: Succeeded`, both
> certificates created and both bindings updated to `SniEnabled` in one deployment. **The ARM
> multi-hostname `for`-loop Conflict that Phase 10a hit did not recur**, and no manual
> `az webapp config ssl bind` fallback was needed — budget for it, but don't expect it.
>
> **Expect a few minutes of lag between "Succeeded" and the certificate actually being served.**
> Immediately after the deployment the apex served its new cert correctly while
> `admin.akerogarden.se` was still being handed Azure's default `*.azurewebsites.net` cert, and two
> public routes returned connection failures. Both resolved on their own within a couple of minutes
> — the SNI binding propagating to the front ends. **Do not start deleting certificates or
> re-running the deployment on the strength of a probe taken seconds after it completes.** Re-measure
> first:
>
> ```bash
> echo | openssl s_client -connect admin.akerogarden.se:443 -servername admin.akerogarden.se 2>/dev/null \
>   | openssl x509 -noout -subject -dates
> # want: subject= /CN=admin.akerogarden.se — NOT /CN=*.azurewebsites.net
> ```

---

## Part E — `www` → apex redirect

The implementation plan requires apex-canonical with `www` 301-redirecting to it. One.com's DNS panel offers this as the **`Web forward`** tab in `Create new record` — set `www.akerogarden.se` → `https://akerogarden.se` there, rather than trying to point `www` at Azure.

> **Confirmed working, 2026-09-07.** Hostname `www`, "Will direct to" `https://akerogarden.se`. The form offers **no redirect-type choice**, so the behaviour has to be measured afterwards rather than configured. What it produced:
>
> ```
> www A        195.47.247.18   (one.com's forward service — not the old hosting IP)
> www AAAA     gone
> http://www   → 307 → https://www
> https://www  → 308 → https://akerogarden.se
> ```
>
> **A 308, not the 301 the plan specifies** — the permanent redirect that preserves the request method. Search engines treat 308 and 301 identically for canonicalisation, so this satisfies the requirement; don't chase the literal 301. It is **not** a masked/frame forward (the address bar lands on the apex, which is what canonicalisation needs), and one.com terminates TLS for `www` itself, so there is no certificate error on the way through and `www` never needs binding in Azure.
>
> Verify with `curl -sIL https://www.akerogarden.se` and check `url_effective` ends at the apex. If a future change makes it a frame or a 302, bind `www.akerogarden.se` in Azure as a third hostname (with its own managed cert) and redirect at the app instead.

There is a second measured behaviour of that forward, found later and worth knowing before anyone
relies on a `www` link:

> ### ⚠️ The web forward **discards the path** — every `www` URL lands on the homepage
>
> Measured 2026-09-09, while verifying the WordPress decommission. one.com's forward sends a
> bare `Location: https://akerogarden.se` no matter what was requested:
>
> ```
> www/boka             → Location: https://akerogarden.se → 200 (homepage, NOT /boka)
> www/nonexistent-xyz  → Location: https://akerogarden.se → 200 (homepage, NOT a 404)
> ```
>
> Direct on the apex the same paths behave correctly (`/boka` 200, `/nonexistent-xyz` 404), so
> this is the forward's behaviour, not the app's.
>
> **Why this is easy to miss:** the flattened request returns **200**, so a broken deep link never
> reports an error anywhere — not to the visitor, not in logs, not to a link checker. It was found
> only because `www/wp-admin/` returned 200 during the post-decommission sweep and briefly looked
> like a surviving WordPress install.
>
> **Impact is low and was accepted, though not for the reason first given here.** This box used
> to say little was indexed under `www`. That was wrong: `www` was WordPress's host, and on
> 2026-09-11 Google's main result for "åkerögården" was still the `www` WordPress copy, last
> crawled 2026-09-05, with sitelinks to WordPress pages ("Läger", "Konferensanläggning"). Keeping
> the path would not help those links, because none of those pages exists on the new site. Sent
> through a path-preserving redirect they would 404, and landing on the homepage is the better
> outcome. Google follows the redirect on its next crawl and moves the result to the apex; a
> recrawl of `www/` was requested in Search Console on 2026-09-11. The cost that remains is a
> printed or legacy link to a page that *does* exist on the new site, such as
> `www.akerogarden.se/boka`. It silently delivers the homepage instead.
>
> **The fix, if it ever becomes worth a deploy,** is the one named above: bind `www.akerogarden.se`
> in Azure as a third hostname with its own managed cert and redirect in the app, which preserves
> the path. Do not "fix" this inside one.com's forward — it offers no path option.

---

## Part F — Soak period, then decommission WordPress

1. Keep the WordPress hosting **powered but unlinked** (DNS no longer points to it) for a short soak — long enough to confirm the new DNS resolves cleanly worldwide and the app serves correctly on both hostnames with valid SSL.
2. Confirm `https://akerogarden.se` and `https://admin.akerogarden.se` both load, SSL padlock valid, no mixed-content warnings.
3. Once confirmed, decommission the WordPress **web hosting only** in one.com — leave the DNS zone, registration, and all email records exactly as they are.
4. Now safe to remove the old WordPress `A`/`www` records from one.com, if they weren't already overwritten in Part C.
5. **Delete the subdomain `asuid` `TXT` records** (`asuid.admin`, and on staging `asuid.staging` / `asuid.admin.staging`) now that every hostname is bound and every certificate issued. Keep `asuid.akerogarden.se` — it is permanent (Part C box). Re-verify afterwards: `curl -sI https://admin.akerogarden.se` must still return 200 with a valid cert.

Content parity is a non-issue here — implementation plan notes the live site today is a trivial info page (images + phone/email), so there's nothing to migrate.

### ✅ WordPress decommissioned — 2026-09-09

Two-day soak served its purpose; nothing resolved to the old hosting and nothing depended on it.

**What was done:** the uploaded files were downloaded off one.com first, then WordPress was
uninstalled from the one.com control panel and the web root confirmed empty in the file manager.

**Deliberately not taken: a database export.** Judged unnecessary — the old site was a trivial info
page, the images were already saved, and nothing was being migrated. one.com's uninstall drops the
WordPress database with the files, so the old page text and settings are gone. This was a decision,
not an oversight; do not record it as a gap.

**What was left alone, and must stay that way:** the one.com **subscription itself**, the DNS zone,
the domain registration, every mailbox, `MX`/`SPF`/`DMARC`, and the `www` **Web forward** record.

> ⚠️ **Never execute this step as "cancel the one.com hosting plan".** On one.com the web space, the
> mailboxes, the DNS zone, the domain registration and the `Web forward` are facets of **one
> subscription**. Cancelling it takes mail down with the web space — the silent-email-death this
> runbook warns about, arriving through a door the warning at the top does not name. The job is
> *empty the web space*, nothing more.

**Verified after (2026-09-09):** apex `A` `51.12.73.224`, apex `AAAA` still empty, `admin` CNAME
intact, all 6 public routes 200, admin 302 to login, both managed certs valid to 2027-03-07, all
4 `MX` records present, SPF and DMARC unchanged, `ns01/ns02.one.com` still authoritative,
`send.one.com` resolving, and zero `wp-content`/`wp-includes`/`wordpress` markers in the served HTML.

> **`www/wp-admin/` returning 200 after the uninstall is not a surviving install** — it is the web
> forward flattening the path to the apex homepage (see Part E). Check `/wp-admin/` **on the apex**,
> where it correctly 404s. Anyone re-running this sweep will hit the same false positive.

---

## Part G — base-URL flip + magic-link re-verify (config, not DNS)

Not a DNS step, but it belongs in the same cutover window and must not be skipped:

1. **Flip the base URLs.** During Phase 10c prod ran on `*.azurewebsites.net`, so `publicBaseUrl` / `adminBaseUrl` point there and every email link (esp. the `/slutfor/{token}` magic link) carries the Azure default hostname. Switch both to the real domains (`https://akerogarden.se` / `https://admin.akerogarden.se`) and redeploy.
2. **Re-verify one magic-link email end-to-end** after the flip — submit a test booking, approve it, click the completion link, confirm it now points at `akerogarden.se` and completes. This is the one 10c email verification that does **not** survive the base-URL flip, so it must be re-checked here.

## Part H — final pre-launch data cleanup (do LAST, before announcing go-live)

Production accumulated **test bookings** during 10c verification (e.g. `AG-2026-0001`) and the Part G magic-link re-check adds another. Remove **all** test bookings so the client launches with zero fake data. Reference data (buildings, rooms, meals, prices, facilities, cleaning zones) **stays** — only bookings and their guest data go.

Bookings are not hard-deletable from the admin UI (business records), so delete via the DB from inside the VNet — same SSH-into-`jmk-ag-prod-admin` + `psql` mechanism as the restore drill (`restore-drill-runbook.md` Path 2), connecting to the **live** server `jmk-ag-prod-pg.postgres.database.azure.com`. The booking's child rows (participants, meal preferences, cleaning/facility selections, booking-scoped audit entries) all have `ON DELETE CASCADE`, so one statement clears everything:

```sql
-- inside psql, connected to the live prod DB. Deletes ALL bookings (only test data exists pre-launch).
DELETE FROM "Bookings";
```

*(Reminder: bash history-expansion eats `!` in the Postgres password inside the container shell — run `set +H` first. See the restore-drill runbook.)*

Confirm `select count(*) from "Bookings";` returns **0** before announcing go-live.

## Record the result

Log: date/time of cutover, DNS propagation confirmation method used, SSL binding confirmed (both hostnames), base-URL flip + magic-link re-verify done, test-data cleanup confirmed (0 bookings), WordPress decommission date. This closes out the Phase 10d exit criterion "production reachable on `akerogarden.se` and `admin.akerogarden.se`, with `www` redirecting to the apex."

### ✅ Production cutover — 2026-09-07

**`akerogarden.se` went live on Azure at ~19:00 UTC.** Verified 19:18 UTC.

| Check | Result |
|---|---|
| apex `A` | `51.12.73.224` (prod scale unit) |
| apex `AAAA` | **none** — displaced automatically by the root `A` create |
| `admin` `CNAME` | `jmk-ag-prod-admin.azurewebsites.net` |
| `www` | `195.47.247.18` → **308** → `https://akerogarden.se` |
| `MX` / `SPF` / `DMARC` | untouched throughout — 4 MX records, SPF intact |
| Certificates | `CN=akerogarden.se` and `CN=admin.akerogarden.se`, GeoTrust, valid to 2027-03-07 |
| Public routes | all 9 return 200 over HTTPS on the apex |
| Admin | 302 to login |
| Base URLs | flipped to the real domains on both apps |
| Magic link | re-verified end to end — inquiry → approval → email → `https://akerogarden.se/slutfor/…` → completion form submitted |
| Test data | `DELETE FROM "Bookings"` run; reference data retained |
| WordPress | powered but unlinked at cutover; **decommissioned 2026-09-09** after a two-day soak — see Part F |

**Order actually used:** bind hostnames → DNS records → `ssl.bicep` → `www` forward → base-URL flip → magic-link test → data cleanup. Binding the hostnames *before* touching DNS worked exactly as Part D describes: the pre-staged `asuid` TXT records satisfy ownership, so both bindings succeeded while the apex still pointed at WordPress.

**Part A (lower TTLs a day ahead) was skipped and it cost nothing** — see the note there. Propagation was effectively instant.

**Still open after cutover:**

- ~~WordPress hosting not yet decommissioned~~ — **done 2026-09-09**, web hosting only; zone, registration, mail and the `www` forward all intact. Part F carries the record.
- The `www` forward is a 308, not the 301 the implementation plan names. Equivalent for canonicalisation; the plan's wording is what's imprecise, not the configuration. **It also drops the path** — see the second Part E note; low severity, accepted.
- Unrelated to DNS but pending at go-live: the eight client questions in `docs/legal/`, and the email/terms cancellation divergence recorded in `docs/legal/known-conflicts-at-launch.md`.

---

## Staging DNS

Simpler than the production cutover above — no WordPress to retire, just records pointing at the staging App Service sites in the client's tenant. Same rule applies: never touch `MX`/`SPF`/`DKIM`/`DMARC` or any mailbox (same one.com DNS zone as production).

**Status: DONE.** Staging was first bound single-app in Phase 10a, then **re-pointed to the two-site topology in Phase 10b (2026-07-17)** and verified live on both subdomains with valid managed SSL. The two-site target state is below (for reference / re-provisioning).

```
RG              = akerogarden-staging   # in the client's tenant
PUBLIC_APP      = jmk-ag-staging-public
ADMIN_APP       = jmk-ag-staging-admin
PUBLIC_HOST     = staging.akerogarden.se        # → public site
ADMIN_HOST      = admin.staging.akerogarden.se  # → admin site
```

1. Get the target values (verification ID is subscription-scoped — one value for all records):
   ```bash
   az webapp show -g $RG -n $PUBLIC_APP --query customDomainVerificationId -o tsv
   ```
2. DNS records at one.com (both subdomains use `CNAME`, each pointing at its own site's default hostname):

   | Host | Type | Value | Purpose |
   |---|---|---|---|
   | `staging` | `CNAME` | `jmk-ag-staging-public.azurewebsites.net` | Points `staging.akerogarden.se` at the **public** site |
   | `asuid.staging` | `TXT` | *(verification ID)* | **Temporary** — delete after binding (Part C box) |
   | `admin.staging` | `CNAME` | `jmk-ag-staging-admin.azurewebsites.net` | Points `admin.staging.akerogarden.se` at the **admin** site |
   | `asuid.admin.staging` | `TXT` | *(same verification ID)* | **Temporary** — same |

3. Bind each hostname to its own site (`az webapp config hostname add`, secret-free) and run `ssl.bicep` with `staging.ssl-parameters.json`, same pattern as Part D. (When re-pointing from a prior site — as the 10b cutover did — first remove the hostname from the old site and delete its stale managed cert; see Part D's gotcha.)
4. No soak/decommission step needed — there's no prior WordPress hosting on these subdomains to retire.
