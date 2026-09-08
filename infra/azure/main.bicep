// Åkerögården — Azure infrastructure
// Deploy: az deployment group create --resource-group <rg> --template-file main.bicep --parameters @<env>.parameters.json
//
// ═══════════════════════════════════════════════════════════════════════════════
//  ⛔ DO NOT RE-APPLY THIS TEMPLATE TO **PRODUCTION** WITHOUT READING THIS FIRST
// ═══════════════════════════════════════════════════════════════════════════════
//
// Production went live 2026-09-07 and now holds real guest data. On 2026-09-08 a
// `what-if` against akerogarden-prod reported 17 resources to modify, and three of
// those predictions would be serious if they are real rather than what-if noise:
//
//   • Microsoft.DBforPostgreSQL/flexibleServers/jmk-ag-prod-pg
//       - properties.network.publicNetworkAccess: "Disabled"
//     If applied, the database becomes reachable from the public internet —
//     a direct violation of NFR-06/24/25, on a server holding guest PII.
//
//   • Microsoft.KeyVault/vaults/jmk-ag-prod-kv
//       - properties.accessPolicies: [both entries]
//     Those are the public and admin managed identities (get/wrapKey/unwrapKey on
//     keys, get/list on secrets). Removing them breaks DataProtection and takes
//     BOTH sites down. The template adds them back via a separate
//     `accessPolicies/add` resource, which what-if explicitly cannot predict
//     ("Unsupported"), so the net effect and its ordering are unverified.
//
//   • Microsoft.Cache/redisEnterprise/jmk-ag-prod-redis
//       - highAvailability: "Enabled", redundancyMode: "ZR"
//     If applied, high availability is lost.
//
// They are PROBABLY noise — Azure warns that what-if produces false positives, and
// these are all "template does not mention the property" cases. But "probably" is
// not the standard for a live database, and nobody has confirmed which.
//
// This does NOT mean the template is wrong for a fresh deployment: production and
// staging were both built from it, and staging can be re-applied freely (synthetic
// data, rebuildable). It means the template has drifted from what production
// actually is, and closing that gap is unfinished work — not a step to take
// casually because "if it's not in code, it doesn't exist".
//
// Before any production re-apply:
//   1. az deployment group what-if -g akerogarden-prod --template-file main.bicep \
//        --parameters @prod.parameters.json
//   2. Resolve every `-` line above to noise-or-real, and declare the real ones in
//      this template so what-if comes back clean.
//   3. Pass postgresAdminPassword, smtpUsername and smtpPassword as THE VALUES
//      ALREADY IN USE (from jmk-ag-prod-kv). Omitting them resets the Postgres
//      admin password and overwrites the Key Vault SMTP secrets. See
//      prod.parameters.json. seedAdminPassword is now OPTIONAL and must be left
//      EMPTY for production — supplying it re-creates a readable admin password
//      in App Service configuration (finding #3).
//
// Amended 2026-09-08 by the post-launch security review. Since it was written:
//   • No storage account key is produced by this template at all. Both Data
//     Protection key rings and the facility-image store authenticate with each
//     site's managed identity (finding #9), so listKeys() is gone and the sites
//     hold Storage Blob Data Contributor instead.
//   • ConnectionStrings__Postgres is a Key Vault reference, not a plaintext app
//     setting, backed by a new `postgres-connection` secret (finding #3).
//   • clientAffinityEnabled, healthCheckPath and http20Enabled are now declared
//     rather than left as undeclared live state (findings #10, #20).
// These CLOSE drift rather than adding it — but they are new resources and new
// role assignments, so the what-if above must still be re-read before applying.
//
// Note that the deploy pipelines do NOT apply Bicep — they run AzureWebApp@1 only,
// application code. Nothing reconciles this automatically, so nothing will trip
// this by accident; it takes a human running the command above.
// ═══════════════════════════════════════════════════════════════════════════════

@description('Environment name — used as a suffix on all resource names')
param environmentName string

@description('Azure region for all resources (must be EU — NFR-06/24)')
param location string = 'northeurope'

@description('App Service Plan SKU')
param appServiceSkuName string = 'B1'

@description('PostgreSQL admin login')
param postgresAdminLogin string

@secure()
@description('PostgreSQL admin password')
param postgresAdminPassword string

@description('SMTP host (One.com)')
param smtpHost string = 'send.one.com'

@description('SMTP port')
param smtpPort int = 587

@description('SMTP sender address')
param smtpFromAddress string

@secure()
@description('SMTP username — stored in Key Vault; App Service reads via managed identity')
param smtpUsername string

@secure()
@description('SMTP password — stored in Key Vault; App Service reads via managed identity')
param smtpPassword string

@description('Custom hostnames bound to the PUBLIC site (e.g. ["akerogarden.se"] / ["staging.akerogarden.se"])')
param publicHostnames array = []

@description('Custom hostnames bound to the ADMIN site (e.g. ["admin.akerogarden.se"] / ["admin.staging.akerogarden.se"])')
param adminHostnames array = []

@description('Bootstrap admin email — seeds the first admin account on startup if none exists')
param seedAdminEmail string

@secure()
@description('''Bootstrap admin password — seeds the first admin account on startup if none exists.
Supply this ONLY when standing up a new environment or recovering one into an empty user table.
Leave it empty afterwards: AdminSeeder ignores it once any user exists, and the setting is then just a
live admin password sitting readable in App Service configuration (post-launch security review,
finding #3). When empty, the app setting is not emitted at all.''')
param seedAdminPassword string = ''

@description('Public site base URL used in booking emails (magic link, privacy link) — §9')
param publicBaseUrl string

@description('Admin panel base URL used in the admin notification email — FR-06')
param adminBaseUrl string

@description('Address that receives admin notification emails (new inquiry, etc.)')
param adminNotificationEmail string

@description('Contact email shown in every booking email footer — §9')
param contactEmail string

// ── Naming ────────────────────────────────────────────────────────────────────
var prefix = 'jmk-ag-${environmentName}'

// ── Two-app topology (Phase 10b / FR-70) ──────────────────────────────────────
// The public and admin surfaces are two independent App Service *sites* on the one shared plan —
// genuine process isolation (the internet-facing public process never loads Identity/SignalR/admin
// code). Everything stateful below (Postgres, Redis, Blob, Key Vault, VNet, telemetry) is shared.
// This array is the single source of per-site divergence; the identity, site, host-binding, DP
// container and KV-access resources all loop over it. Order matters: index 0 = public, 1 = admin
// (the host-binding resources reference sites[0]/sites[1] by that convention).
var apps = [
  {
    role: 'public'
    hostnames: publicHostnames
  }
  {
    role: 'admin'
    hostnames: adminHostnames
  }
]

// The PostgreSQL connection string is built here and stored as a KEY VAULT SECRET below; each site's
// app setting is a @Microsoft.KeyVault(...) reference to it rather than the value (post-launch
// security review, finding #3 — it carries the database administrator password).
var postgresConnectionString = 'Host=${postgresServer.properties.fullyQualifiedDomainName};Database=akerogarden;Username=${postgresAdminLogin};Password=${postgresAdminPassword};SslMode=Require'
// NOTE: there is deliberately no blob connection string here any more (post-launch security review,
// finding #9). Both the Data Protection key rings and the facility-image store now authenticate with
// each site's managed identity, so this template never calls storageAccount.listKeys() and no storage
// account key is written into App Service configuration. Re-introducing one would undo that.

// ── User-Assigned Managed Identities (one per site) ──────────────────────────
// Created before Key Vault and the sites so each principalId is available for the Key Vault access
// policy in the same deployment pass. Two identities (not one shared) so each site runs as its own
// principal — process isolation extends to the identity boundary (Phase 10b).
resource appIdentities 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = [for app in apps: {
  name: '${prefix}-${app.role}-identity'
  location: location
}]

// ── Key Vault ─────────────────────────────────────────────────────────────────
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${prefix}-kv'
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: false
    // With RBAC disabled, Azure requires an accessPolicies array to be present.
    // Start empty; the kvAccessPolicy resource below adds the app's policy.
    accessPolicies: []
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// Key wrapping the Data Protection master key (Phase 7 — NFR-30). The app wraps/unwraps via its
// managed identity (see the keys permissions in kvAccessPolicy). RSA 2048, wrap/unwrap only.
resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: keyVault
  name: 'dataprotection-key'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: ['wrapKey', 'unwrapKey']
  }
}

resource smtpUsernameSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'smtp-username'
  properties: { value: smtpUsername }
}

resource smtpPasswordSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'smtp-password'
  properties: { value: smtpPassword }
}

// Redis connection string (contains the access key) — stored as a secret, read by the app
// via managed identity like the SMTP secrets (NFR-30). Distributed rate-limit store (NFR-04).
// PostgreSQL connection string (contains the administrator password) — held as a secret and read by
// each app via managed identity, rather than sitting in plaintext App Service configuration
// (post-launch security review, finding #3).
resource postgresConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'postgres-connection'
  properties: {
    value: postgresConnectionString
  }
}

resource redisConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'redis-connection'
  properties: {
    value: '${redisEnterprise.properties.hostName}:10000,password=${redisDatabase.listKeys().primaryKey},ssl=True,abortConnect=False'
  }
}

// ── App Service Plan ──────────────────────────────────────────────────────────
resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${prefix}-plan'
  location: location
  kind: 'linux'
  sku: {
    name: appServiceSkuName
    tier: 'Basic'
  }
  properties: {
    reserved: true // required for Linux
  }
}

// ── App Service sites (public + admin, on the one shared plan) ────────────────
// One site per entry in `apps`. Settings that overlap are built once (the common block); the
// per-role divergence is a single concat: Redis → public only (only the inquiry form is rate-
// limited); seed-admin + image-store Blob → admin only (keeps the internet-facing public process
// free of the seed-admin secret and the image-upload credential). Each site runs as its own
// identity, resolves @Microsoft.KeyVault(...) references through it, and persists its Data-Protection
// ring to its own private Blob container (shared nothing — public: antiforgery; admin: auth cookie).
resource sites 'Microsoft.Web/sites@2023-12-01' = [for (app, i) in apps: {
  name: '${prefix}-${app.role}'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appIdentities[i].id}': {}
    }
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    // ARR session affinity — ON for admin, OFF for public (post-launch security review, finding #10).
    // App Service sets two cookies (ARRAffinity + ARRAffinitySameSite) when this is enabled. The public
    // site is pure Static SSR and holds no server-side session, so on that host they are cookies nobody
    // asked for on a site whose privacy policy §7 turns on there being no unnecessary ones — and whose
    // absence of a cookie banner rests on that §7 table being complete and true. The admin host keeps
    // affinity: its Blazor Interactive Server circuits are stateful and would need it the moment the
    // plan runs more than one instance, and it is disclosed in §7 accordingly.
    // Previously undeclared here, so the live value was drift rather than a decision.
    clientAffinityEnabled: app.role == 'admin'
    // App Service must use the user-assigned identity (not the absent system one)
    // to resolve @Microsoft.KeyVault(...) references in app settings.
    keyVaultReferenceIdentity: appIdentities[i].id
    virtualNetworkSubnetId: appSubnet.id
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true // Basic B1 supports Always On (only Free/Shared do not); required on the ADMIN site so the daily BackgroundService (Phase 5 — reminders, retention warning, GDPR deletion) is not unloaded on idle. Kept on for public too (free — the plan is billed whether awake or idle).
      // Liveness probe (post-launch security review, finding #20). Both apps map /health via
      // AddHealthChecks with NO dependency checks registered: App Service takes a failing instance out
      // of rotation, and on a single-worker B1 a database-backed probe would escalate a transient
      // Postgres fault into an outage. Previously unset, so App Service had no way to detect a wedged
      // instance at all.
      healthCheckPath: '/health'
      // HTTP/2 (finding #20). Off by default, and there is no reason for it to be: it reduces
      // round-trips for the several static assets each page pulls, at no cost or compatibility risk —
      // clients that do not negotiate it simply stay on HTTP/1.1.
      http20Enabled: true
      appSettings: concat([
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: environmentName == 'prod' ? 'Production' : 'Staging'
        }
        {
          // Read from Key Vault at runtime via managed identity (post-launch security review,
          // finding #3). This was a plaintext app setting containing the PostgreSQL administrator
          // password — readable with one CLI call by anyone holding Contributor, including the deploy
          // service principal. SMTP and Redis already used Key Vault references; this brings the
          // database credential in line with them.
          name: 'ConnectionStrings__Postgres'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=postgres-connection)'
        }
        {
          name: 'Smtp__Host'
          value: smtpHost
        }
        {
          name: 'Smtp__Port'
          value: '${smtpPort}'
        }
        {
          name: 'Smtp__FromAddress'
          value: smtpFromAddress
        }
        {
          // Read from Key Vault at runtime via managed identity (NFR-30)
          name: 'Smtp__Username'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=smtp-username)'
        }
        {
          // Read from Key Vault at runtime via managed identity (NFR-30)
          name: 'Smtp__Password'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=smtp-password)'
        }
        {
          // Data Protection key ring persisted to Blob (Phase 7 — NFR-30). Its presence is what
          // switches on persistence — absent in local dev, so dev keeps the default filesystem ring.
          //
          // A blob service URI, NOT a connection string (post-launch security review, finding #9).
          // Both hosts previously received the same storage ACCOUNT KEY, so the public site could read
          // the admin site's key-ring container: the "shared nothing" isolation was real at the
          // DataProtection application-name layer and absent at the credential layer. The app now
          // authenticates with its own managed identity, which holds Storage Blob Data Contributor
          // (granted below) — no shared secret exists to leak. Local dev still uses a connection
          // string against Azurite, which has no AAD.
          name: 'DataProtection__KeysBlobServiceUri'
          value: storageAccount.properties.primaryEndpoints.blob
        }
        {
          // Per-site private container so the two rings share no storage (Phase 10b — shared nothing).
          name: 'DataProtection__KeysContainer'
          value: 'dataprotection-keys-${app.role}'
        }
        {
          // Key Vault key wrapping the Data Protection master key (NFR-30)
          name: 'DataProtection__KeyVaultKeyId'
          value: dataProtectionKey.properties.keyUri
        }
        {
          // The app runs under a user-assigned identity, so DefaultAzureCredential must be told which
          // one to use when authenticating to Key Vault for key wrap/unwrap.
          name: 'DataProtection__ManagedIdentityClientId'
          value: appIdentities[i].properties.clientId
        }
        {
          name: 'BookingEmail__PublicBaseUrl'
          value: publicBaseUrl
        }
        {
          name: 'BookingEmail__AdminBaseUrl'
          value: adminBaseUrl
        }
        {
          name: 'BookingEmail__AdminNotificationEmail'
          value: adminNotificationEmail
        }
        {
          name: 'BookingEmail__ContactEmail'
          value: contactEmail
        }
        {
          // Structured logging / correlation IDs (Phase 11). Not a secret (write-only telemetry
          // ingestion key) — plain app setting is fine, no Key Vault indirection needed.
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
      ], app.role == 'public' ? [
        {
          // Distributed rate-limit store (NFR-04) — public only (the inquiry form is the only rate-
          // limited endpoint). Read from Key Vault via managed identity (NFR-30).
          name: 'Redis__ConnectionString'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=redis-connection)'
        }
      ] : concat([
        {
          // Bootstraps the first admin on startup if no users exist (AdminSeeder) — admin site only.
          // The same startup pass also creates the two FR-104 roles; no setting governs that.
          name: 'Seed__AdminEmail'
          value: seedAdminEmail
        }
        {
          // Facility image upload store (FR-27) — admin only. The public site renders images from
          // their anonymous Blob URLs and needs no storage credential.
          //
          // Managed identity, not a connection string (post-launch security review, finding #9). The
          // connection string carried the storage ACCOUNT KEY, which grants full access to the whole
          // account — including both Data Protection key-ring containers. Moving it behind a Key Vault
          // reference would have preserved a credential that no longer needs to exist; this removes it.
          name: 'Blob__ServiceUri'
          value: storageAccount.properties.primaryEndpoints.blob
        }
        {
          name: 'Blob__ManagedIdentityClientId'
          value: appIdentities[i].properties.clientId
        }
      ],
      // Seed__AdminPassword is emitted ONLY when a password is actually supplied (post-launch
      // security review, finding #3). AdminSeeder creates an account solely when the user table is
      // empty, so once an environment is bootstrapped this setting does nothing except leave a live
      // admin password readable in App Service configuration to anyone with Contributor — which it
      // did in production from 2026-07-20 until this change.
      //
      // Pass the parameter when standing up a NEW environment (or recovering one into an empty
      // database); leave it empty thereafter and App Service drops the setting, because appSettings is
      // declarative. That keeps the declared and actual configuration identical either way, rather
      // than deleting it by hand and creating drift.
      empty(seedAdminPassword) ? [] : [
        {
          name: 'Seed__AdminPassword'
          value: seedAdminPassword
        }
      ]))
    }
  }
}]

// ── Key Vault access policy — both sites' managed identities read secrets ─────
// One 'add' operation carrying a policy per site identity. The access-policy model is per-vault
// (not per-secret), so both identities can read every secret; the Redis-vs-seed split is enforced by
// which app SETTING each site carries, not by the vault ACL.
resource kvAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01' = {
  parent: keyVault
  name: 'add'
  properties: {
    accessPolicies: [for (app, i) in apps: {
      tenantId: subscription().tenantId
      objectId: appIdentities[i].properties.principalId
      permissions: {
        secrets: ['get', 'list']
        // wrap/unwrap the Data Protection master key (Phase 7 — NFR-30); get lets the DP
        // provider read the key's metadata/version.
        keys: ['get', 'wrapKey', 'unwrapKey']
      }
    }]
  }
}

// ── Custom hostname bindings (per site) ───────────────────────────────────────
// DNS must point to the App Service before running ssl.bicep to issue managed certs. Public
// hostnames bind to sites[0], admin hostnames to sites[1] (the `apps` array order).
resource publicHostBindings 'Microsoft.Web/sites/hostNameBindings@2023-12-01' = [for hostname in publicHostnames: {
  parent: sites[0]
  name: hostname
  properties: {
    siteName: sites[0].name
    hostNameType: 'Verified'
    customHostNameDnsRecordType: 'A'
  }
}]

resource adminHostBindings 'Microsoft.Web/sites/hostNameBindings@2023-12-01' = [for hostname in adminHostnames: {
  parent: sites[1]
  name: hostname
  properties: {
    siteName: sites[1].name
    hostNameType: 'Verified'
    customHostNameDnsRecordType: 'A'
  }
}]

// ── VNet + Subnets ────────────────────────────────────────────────────────────
resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = {
  name: '${prefix}-vnet'
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: ['10.0.0.0/16']
    }
    subnets: [
      {
        name: 'app-subnet'
        properties: {
          addressPrefix: '10.0.1.0/24'
          delegations: [
            {
              name: 'app-service-delegation'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
      {
        name: 'db-subnet'
        properties: {
          addressPrefix: '10.0.2.0/24'
          delegations: [
            {
              name: 'postgres-delegation'
              properties: {
                serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers'
              }
            }
          ]
        }
      }
    ]
  }
}

resource appSubnet 'Microsoft.Network/virtualNetworks/subnets@2023-11-01' existing = {
  parent: vnet
  name: 'app-subnet'
}

resource dbSubnet 'Microsoft.Network/virtualNetworks/subnets@2023-11-01' existing = {
  parent: vnet
  name: 'db-subnet'
}

// ── PostgreSQL Flexible Server (VNet-only — NFR-25) ───────────────────────────
resource postgresServer 'Microsoft.DBforPostgreSQL/flexibleServers@2023-12-01-preview' = {
  name: '${prefix}-pg'
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    version: '16'
    storage: {
      storageSizeGB: 32
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    network: {
      delegatedSubnetResourceId: dbSubnet.id
      privateDnsZoneArmResourceId: postgresDnsZone.id
    }
    highAvailability: {
      mode: 'Disabled'
    }
    availabilityZone: '1'
  }
}

resource postgresDb 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-12-01-preview' = {
  parent: postgresServer
  name: 'akerogarden'
}

resource postgresDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: '${prefix}-pg.private.postgres.database.azure.com'
  location: 'global'
}

resource postgresDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: postgresDnsZone
  name: '${prefix}-pg-dns-link'
  location: 'global'
  properties: {
    virtualNetwork: {
      id: vnet.id
    }
    registrationEnabled: false
  }
}

// ── Blob Storage ──────────────────────────────────────────────────────────────
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: replace('${prefix}blob', '-', '') // storage names: alphanumeric only, max 24 chars
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    // Facility images are non-personal public assets served on the public site (FR-81);
    // they are read anonymously, so blob-level public access must be allowed. Azure
    // defaults this to false, which would break the app's public-read upload (FR-27).
    allowBlobPublicAccess: true
  }
}

// Container for facility images (FR-27). Declared in IaC rather than left to the
// app's runtime CreateIfNotExists, so it exists by deployment, not first upload.
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storageAccount
  name: 'default'
}

resource facilityImagesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'facility-images'
  properties: {
    publicAccess: 'Blob' // anonymous read of individual blobs; no container listing
  }
}

// Holds each site's Data Protection key ring (Phase 7 — NFR-30). One PRIVATE container per site
// (Phase 10b — the public and admin rings share no storage). PRIVATE (no public access): these are
// secret keys, unlike the anonymously-read facility images. Each ring's master key is additionally
// wrapped by the Key Vault key above.
resource dataProtectionKeysContainers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [for app in apps: {
  parent: blobService
  name: 'dataprotection-keys-${app.role}'
  properties: {
    publicAccess: 'None'
  }
}]

// ── Blob data-plane access for each site's managed identity (finding #9) ──────────────────────
// Storage Blob Data Contributor on the storage account, so each app can read and write its own
// Data Protection key ring without being handed the account key. Control-plane Contributor does NOT
// grant data-plane access on Storage — it is a separate role, which is why the key was used before.
//
// Scoped to the account rather than the container because the admin site also needs the
// facility-images container (FR-27), and a per-container split would still leave each identity able
// to reach the other's container only via a role it does not hold. The account key it replaces gave
// both hosts everything, so this is strictly narrower even at account scope.
var storageBlobDataContributor = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')

resource blobDataRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (app, i) in apps: {
  name: guid(storageAccount.id, appIdentities[i].id, storageBlobDataContributor)
  scope: storageAccount
  properties: {
    roleDefinitionId: storageBlobDataContributor
    principalId: appIdentities[i].properties.principalId
    principalType: 'ServicePrincipal'
  }
}]

// ── Diagnostics (Phase 11 — structured logging, correlation IDs, failure diagnosis) ───────────
// Workspace-based Application Insights (the AI-only "classic" resource is retired for new
// deployments). dailyQuotaGb caps ingestion cost — 0.1 GB/day is a deliberately conservative
// ceiling for a low-traffic booking site; raise it if legitimate telemetry gets throttled.
// retentionInDays is set explicitly rather than left at the platform default (NFR-05/06 —
// logs must not outlive their diagnostic usefulness, and must stay in an EU region).
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: json('0.1')
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${prefix}-appinsights'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
    IngestionMode: 'LogAnalytics'
  }
}

// ── Azure Managed Redis (distributed rate limiting — NFR-04) ──────────────────
// Classic Azure Cache for Redis is retiring and can no longer be created; Azure Managed Redis
// (Microsoft.Cache/redisEnterprise) is its successor. Balanced B0 is the entry tier — ample for
// rate-limit counters. The app connects over TLS on port 10000 via the connection string held
// in Key Vault; StackExchange.Redis is unchanged. EnterpriseCluster clustering exposes a single
// logical endpoint, so the client needs no OSS-cluster configuration.
resource redisEnterprise 'Microsoft.Cache/redisEnterprise@2024-10-01' = {
  name: '${prefix}-redis'
  location: location
  sku: {
    name: 'Balanced_B0'
  }
}

resource redisDatabase 'Microsoft.Cache/redisEnterprise/databases@2024-10-01' = {
  parent: redisEnterprise
  name: 'default'
  properties: {
    clientProtocol: 'Encrypted' // TLS only
    port: 10000
    clusteringPolicy: 'EnterpriseCluster'
    evictionPolicy: 'NoEviction' // rate-limit keys always carry a TTL
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────
output publicAppName string = sites[0].name
output adminAppName string = sites[1].name
output publicAppHostname string = sites[0].properties.defaultHostName
output adminAppHostname string = sites[1].properties.defaultHostName
// ssl.bicep needs the plan id to bind managed certs to; output it so the ssl-parameters file can
// reference the deployed plan rather than hardcoding the subscription-scoped resource id.
output appServicePlanId string = appServicePlan.id
output postgresHostname string = postgresServer.properties.fullyQualifiedDomainName
output keyVaultName string = keyVault.name
output appInsightsName string = appInsights.name
