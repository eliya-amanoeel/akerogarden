# Opening staging for a visit

**Purpose:** staging (`staging.akerogarden.se`, `admin.staging.akerogarden.se`) is for the maintainer, not
the public. Both sites **deny every request by default**, and you open them for yourself by adding your
current IP address as a temporary rule, then remove it when you're done.

**Why it is split this way:** the lock is in code — `denyUnmatchedSiteAccess: true` in
`infra/azure/staging.parameters.json`, applied by `main.bicep` as `ipSecurityRestrictionsDefaultAction:
Deny`. Your IP rules are **not** in code, on purpose. Re-applying the template wipes them, which fails in the
safe direction: the worst case is re-adding your IP, never a staging site that is accidentally open to
everyone. Keeping IP addresses out of the repo is a side benefit. It is the same pattern as human Key Vault
access: granted for the task, removed afterwards.

Production is unaffected — its parameter is `false`.

---

## 1. Find your IP address

From the network you are on (home, work…), search "what is my IP" in a browser. Note **both** the IPv4
address and, if one is shown, the IPv6 address. On a network with IPv6 the browser often uses it, and an
IPv4-only rule will still get you `403 Forbidden`.

## 2. Open staging — both sites

Run for **both** `jmk-ag-staging-public` and `jmk-ag-staging-admin` (the admin site is a separate app with
its own rules):

```bash
for site in jmk-ag-staging-public jmk-ag-staging-admin; do
  az webapp config access-restriction add -g akerogarden-staging -n "$site" \
    --rule-name maintainer-ipv4 --action Allow --ip-address <your-ipv4>/32 --priority 100
done
```

With IPv6 as well, repeat with `--rule-name maintainer-ipv6 --ip-address <your-ipv6>/128 --priority 110`.

Takes effect within about a minute. Your phone works on the same Wi-Fi (same address), **not** on mobile data,
where the address changes constantly.

## 3. Close it again

```bash
for site in jmk-ag-staging-public jmk-ag-staging-admin; do
  az webapp config access-restriction remove -g akerogarden-staging -n "$site" --rule-name maintainer-ipv4
done
```

(and `maintainer-ipv6` if you added it). Forgetting is low-risk — the rule only admits your own address — but
a home IP can later be handed to someone else, so don't leave rules behind for months.

## See what is currently allowed

```bash
az webapp config access-restriction show -g akerogarden-staging -n jmk-ag-staging-public
```

Expect `ipSecurityRestrictionsDefaultAction: Deny`. If it says `Allow` or is empty, the lock is gone —
re-apply it (below).

---

## Things that behave differently while staging is locked

- **Deploys are unaffected.** The pipelines deploy through the `scm` (Kudu) endpoint, which is deliberately
  left open (`scmIpSecurityRestrictionsDefaultAction: Allow`): Microsoft-hosted agents have unpredictable IPs.
  It still needs the service connection's credentials.
- **Magic links and emails from staging** point at staging, so opening one needs your IP allowed, like any
  other visit.
- **`seeding.md`'s verification step** (fetching `/faciliteter`) needs your IP allowed on the public site.
- **SSL certificates are unaffected.** Free managed certificates are only blocked by IP restrictions on a
  root domain; staging's hostnames are subdomains.
- **A shared work address** admits everyone behind it, and a work VPN may present its own address instead.
  Harmless on a synthetic-data site, but it is not strictly "only you".

## Applying or restoring the lock

The lock comes from `main.bicep`, so any staging Bicep apply sets it. To set it without a full apply (which
needs the secrets passed on the CLI), this does exactly what the template declares:

```bash
for site in jmk-ag-staging-public jmk-ag-staging-admin; do
  az resource update -g akerogarden-staging -n "$site" --resource-type Microsoft.Web/sites \
    --set properties.siteConfig.ipSecurityRestrictionsDefaultAction=Deny
done
```

Verify afterwards from a network you have **not** allowed: both hostnames answer `403`.
