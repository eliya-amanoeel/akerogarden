# DNS + one.com Cutover Runbook (Phase 10b — go-live)

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

**What *does* change:** the apex `A` record, the `admin` subdomain's `CNAME`, `asuid`/`asuid.admin` `TXT` records (Azure domain-ownership verification), and the `www` handling. That's it.

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

---

## Part B — Get the Azure-side target values

Prod must exist (Bicep deployed) before this step — these values come from the live resource.

```bash
# App Service inbound IP for the apex A record (both sites share the plan's inbound IP;
# query the public site since the apex binds there)
az webapp show -g $RG -n $PUBLIC_APP --query inboundIpAddress -o tsv

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
| `asuid` | `TXT` | *(verification ID from Part B)* | Proves apex domain ownership to Azure |
| `admin` | `CNAME` | `jmk-ag-prod-admin.azurewebsites.net` | Points `admin.akerogarden.se` at the **admin** site |
| `asuid.admin` | `TXT` | *(same verification ID)* | Proves `admin` subdomain ownership to Azure |

Leave existing `MX`/`SPF`/`DKIM`/`DMARC`/mailbox records untouched.

**Do not delete the old WordPress `A`/`www` records yet** — see Part F (soak period).

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

`infra/azure/prod.ssl-parameters.json` doesn't exist yet — create it before this step, mirroring
`staging.ssl-parameters.json`: `appServicePlanId` (a `main.bicep` output) plus a `bindings` array of
`{ siteName, hostname }` pairs — `{jmk-ag-prod-public, akerogarden.se}` and `{jmk-ag-prod-admin,
admin.akerogarden.se}`.

> **Gotcha (seen at the staging cutover):** if a hostname already has a managed cert on the plan
> under a different site, `ssl.bicep` fails with `CanonicalName is invalid … duplicate certificate` —
> `az resource delete … Microsoft.Web/certificates` the stale cert first. Not expected for a
> born-fresh prod (no prior site owns these hostnames), but watch for it on any re-point.

---

## Part E — `www` → apex redirect

The implementation plan requires apex-canonical with `www` 301-redirecting to it. One.com's DNS panel typically offers a **URL forward/redirect** feature for `www` (redirect-type record, not a real `CNAME`) — set `www.akerogarden.se` → `https://akerogarden.se` (301) there, rather than trying to point `www` at Azure. Confirm one.com's panel supports this before the cutover window; if it doesn't, this needs an App Service-level rewrite rule instead (would require adding `www.akerogarden.se` as a bound + certificated hostname too).

---

## Part F — Soak period, then decommission WordPress

1. Keep the WordPress hosting **powered but unlinked** (DNS no longer points to it) for a short soak — long enough to confirm the new DNS resolves cleanly worldwide and the app serves correctly on both hostnames with valid SSL.
2. Confirm `https://akerogarden.se` and `https://admin.akerogarden.se` both load, SSL padlock valid, no mixed-content warnings.
3. Once confirmed, decommission the WordPress **web hosting only** in one.com — leave the DNS zone, registration, and all email records exactly as they are.
4. Now safe to remove the old WordPress `A`/`www` records from one.com, if they weren't already overwritten in Part C.

Content parity is a non-issue here — implementation plan notes the live site today is a trivial info page (images + phone/email), so there's nothing to migrate.

---

## Record the result

Log: date/time of cutover, DNS propagation confirmation method used, SSL binding confirmed (both hostnames), WordPress decommission date. This closes out the Phase 10c exit criterion "production reachable on `akerogarden.se` and `admin.akerogarden.se`, with `www` redirecting to the apex."

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
   | `asuid.staging` | `TXT` | *(verification ID)* | Proves `staging` subdomain ownership to Azure |
   | `admin.staging` | `CNAME` | `jmk-ag-staging-admin.azurewebsites.net` | Points `admin.staging.akerogarden.se` at the **admin** site |
   | `asuid.admin.staging` | `TXT` | *(same verification ID)* | Proves `admin.staging` subdomain ownership to Azure |

3. Bind each hostname to its own site (`az webapp config hostname add`, secret-free) and run `ssl.bicep` with `staging.ssl-parameters.json`, same pattern as Part D. (When re-pointing from a prior site — as the 10b cutover did — first remove the hostname from the old site and delete its stale managed cert; see Part D's gotcha.)
4. No soak/decommission step needed — there's no prior WordPress hosting on these subdomains to retire.
