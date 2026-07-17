// Åkerögården — Managed SSL certificates (two-app topology, Phase 10b)
// Run AFTER main.bicep AND after DNS records for all hostnames point to their site.
// Deploy: az deployment group create --resource-group <rg> --template-file ssl.bicep --parameters @<env>.ssl-parameters.json

@description('App Service Plan resource ID (output from main.bicep as appServicePlanId)')
param appServicePlanId string

@description('''Site+hostname pairs to issue managed certificates for and SSL-bind, e.g.
[ { siteName: "jmk-ag-staging-public", hostname: "staging.akerogarden.se" },
  { siteName: "jmk-ag-staging-admin",  hostname: "admin.staging.akerogarden.se" } ]''')
param bindings array

// One managed certificate per site+hostname pair. The certificate is issued against the hostname,
// which must already be a bound, DNS-verified custom hostname on that site (from main.bicep).
resource managedCerts 'Microsoft.Web/certificates@2023-12-01' = [for b in bindings: {
  name: '${b.siteName}-${replace(b.hostname, '.', '-')}'
  location: resourceGroup().location
  properties: {
    serverFarmId: appServicePlanId
    canonicalName: b.hostname
  }
}]

// Upgrade each existing hostname binding to SNI-enabled SSL with its freshly-issued cert. Addressed
// by full resource name (site/hostname) rather than a `parent` symbol so each entry can target a
// different site within the one loop.
resource sslBindings 'Microsoft.Web/sites/hostNameBindings@2023-12-01' = [for (b, i) in bindings: {
  name: '${b.siteName}/${b.hostname}'
  dependsOn: [managedCerts]
  properties: {
    sslState: 'SniEnabled'
    thumbprint: managedCerts[i].properties.thumbprint
  }
}]
