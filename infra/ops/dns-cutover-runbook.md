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
APP_NAME        = akerogarden-prod-app
APEX            = akerogarden.se
ADMIN_HOST      = admin.akerogarden.se
DEFAULT_HOST    = akerogarden-prod-app.azurewebsites.net   # App Service default hostname
```

`staging.akerogarden.se` / `admin.staging.akerogarden.se` are **handled in Phase 10a, not here** — see "Staging DNS (Phase 10a)" below. This runbook's Parts A–F are production-only.

---

## Part A — A day ahead: lower TTLs

In the one.com DNS panel, lower the TTL on the records you're about to change (apex `A`, `www`) to something short (e.g. 300s/5 min), and let the old TTL expire before proceeding. This bounds how long clients can be stuck resolving the old WordPress IP after cutover. Do this **the day before**, not the day of.

---

## Part B — Get the Azure-side target values

Prod must exist (Bicep deployed) before this step — these values come from the live resource.

```bash
# App Service inbound IP (for the apex A record)
az webapp show -g $RG -n $APP_NAME --query inboundIpAddress -o tsv

# Custom Domain Verification ID (for both asuid TXT records — same value for apex and admin)
az webapp show -g $RG -n $APP_NAME --query customDomainVerificationId -o tsv
```

Record both outputs — you'll paste them into one.com in Part C.

---

## Part C — Add the DNS records at one.com

An apex domain cannot use `CNAME` (DNS spec restriction), so apex and subdomain use different record types:

| Host | Type | Value | Purpose |
|---|---|---|---|
| `@` (apex, `akerogarden.se`) | `A` | *(inbound IP from Part B)* | Points the bare domain at App Service |
| `asuid` | `TXT` | *(verification ID from Part B)* | Proves apex domain ownership to Azure |
| `admin` | `CNAME` | `akerogarden-prod-app.azurewebsites.net` | Points `admin.akerogarden.se` at App Service |
| `asuid.admin` | `TXT` | *(same verification ID)* | Proves `admin` subdomain ownership to Azure |

Leave existing `MX`/`SPF`/`DKIM`/`DMARC`/mailbox records untouched.

**Do not delete the old WordPress `A`/`www` records yet** — see Part F (soak period).

---

## Part D — Bind the custom hostnames + issue SSL

Once the DNS records above have propagated (check with `dig akerogarden.se A` / `dig asuid.akerogarden.se TXT` until they resolve to the new values):

```bash
# Bind hostnames (main.bicep already declares hostNameBindings for customHostnames —
# redeploying main.bicep with prod.parameters.json will create these, since
# customHostnames = ["akerogarden.se", "admin.akerogarden.se"] there).
az deployment group create -g $RG --template-file infra/azure/main.bicep --parameters @infra/azure/prod.parameters.json

# Then issue the managed certificates (must run AFTER hostnames resolve to Azure — see ssl.bicep header comment)
az deployment group create -g $RG --template-file infra/azure/ssl.bicep --parameters @infra/azure/prod.ssl-parameters.json
```

`infra/azure/prod.ssl-parameters.json` doesn't exist yet — create it before this step (mirrors `main.bicep`'s `customHostnames`, plus `appServiceName`/`appServicePlanId` — both available as `main.bicep` outputs).

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

Log: date/time of cutover, DNS propagation confirmation method used, SSL binding confirmed (both hostnames), WordPress decommission date. This closes out the Phase 10b exit criterion "production reachable on `akerogarden.se` and `admin.akerogarden.se`, with `www` redirecting to the apex."

---

## Staging DNS (Phase 10a)

Simpler than the production cutover above — no WordPress to retire, just new records pointing at the recreated staging App Service in the client's tenant. Same rule applies: never touch `MX`/`SPF`/`DKIM`/`DMARC` or any mailbox (same one.com DNS zone as production).

```
RG              = akerogarden-staging   # in the client's tenant, after Phase 10a recreation
APP_NAME        = akerogarden-staging-app
PUBLIC_HOST     = staging.akerogarden.se
ADMIN_HOST      = admin.staging.akerogarden.se
```

1. Get the target values (same commands as Part B, against the staging app/resource group):
   ```bash
   az webapp show -g $RG -n $APP_NAME --query inboundIpAddress -o tsv
   az webapp show -g $RG -n $APP_NAME --query customDomainVerificationId -o tsv
   ```
2. Add the DNS records at one.com:

   | Host | Type | Value | Purpose |
   |---|---|---|---|
   | `staging` | `A` or `CNAME`* | *(inbound IP, or `akerogarden-staging-app.azurewebsites.net`)* | Points `staging.akerogarden.se` at App Service |
   | `asuid.staging` | `TXT` | *(verification ID)* | Proves `staging` subdomain ownership to Azure |
   | `admin.staging` | `CNAME` | `akerogarden-staging-app.azurewebsites.net` | Points `admin.staging.akerogarden.se` at App Service |
   | `asuid.admin.staging` | `TXT` | *(same verification ID)* | Proves `admin.staging` subdomain ownership to Azure |

   *Unlike the apex, `staging` is itself a subdomain, so it can use `CNAME` — prefer that over `A` for consistency with `admin.staging`, unless one.com's panel requires otherwise.
3. Once records resolve, bind hostnames (`main.bicep` with the updated `staging.parameters.json`) and run `ssl.bicep` with a new `staging.ssl-parameters.json`, same pattern as Part D.
4. No soak/decommission step needed — there's no prior hosting on these subdomains to retire.

Closes out the Phase 10a exit criterion: staging reachable on `staging.akerogarden.se` / `admin.staging.akerogarden.se` with valid managed SSL.
