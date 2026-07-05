// Åkerögården — Azure infrastructure
// Deploy: az deployment group create --resource-group <rg> --template-file main.bicep --parameters @<env>.parameters.json

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

@description('Custom hostnames to bind to the App Service (e.g. ["akerogarden.se","admin.akerogarden.se"])')
param customHostnames array = []

@description('Bootstrap admin email — seeds the first admin account on startup if none exists')
param seedAdminEmail string

@secure()
@description('Bootstrap admin password — seeds the first admin account on startup if none exists')
param seedAdminPassword string

@description('Public site base URL used in booking emails (magic link, privacy link) — §9')
param publicBaseUrl string

@description('Admin panel base URL used in the admin notification email — FR-06')
param adminBaseUrl string

@description('Address that receives admin notification emails (new inquiry, etc.)')
param adminNotificationEmail string

@description('Contact email shown in every booking email footer — §9')
param contactEmail string

// ── Naming ────────────────────────────────────────────────────────────────────
var prefix = 'akerogarden-${environmentName}'

// ── User-Assigned Managed Identity ───────────────────────────────────────────
// Created before Key Vault and App Service so the principalId is available for
// the Key Vault access policy in the same deployment pass.
resource appIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${prefix}-identity'
  location: location
}

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

// ── App Service ───────────────────────────────────────────────────────────────
resource appService 'Microsoft.Web/sites@2023-12-01' = {
  name: '${prefix}-app'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appIdentity.id}': {}
    }
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    // App Service must use the user-assigned identity (not the absent system one)
    // to resolve @Microsoft.KeyVault(...) references in app settings.
    keyVaultReferenceIdentity: appIdentity.id
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true // Basic B1 supports Always On (only Free/Shared do not); required so the daily BackgroundService (Phase 5 — reminders, retention warning, GDPR deletion) is not unloaded on idle
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: environmentName == 'prod' ? 'Production' : 'Staging'
        }
        {
          name: 'ConnectionStrings__Postgres'
          value: 'Host=${postgresServer.properties.fullyQualifiedDomainName};Database=akerogarden;Username=${postgresAdminLogin};Password=${postgresAdminPassword};SslMode=Require'
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
          name: 'Blob__ConnectionString'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=core.windows.net'
        }
        {
          // Bootstraps the first admin on startup if no users exist (AdminSeeder)
          name: 'Seed__AdminEmail'
          value: seedAdminEmail
        }
        {
          name: 'Seed__AdminPassword'
          value: seedAdminPassword
        }
        {
          // Distributed rate-limit store (NFR-04) — read from Key Vault via managed identity (NFR-30)
          name: 'Redis__ConnectionString'
          value: '@Microsoft.KeyVault(VaultName=${keyVault.name};SecretName=redis-connection)'
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
      ]
    }
    virtualNetworkSubnetId: appSubnet.id
  }
}

// ── Key Vault access policy — App Service managed identity reads secrets ──────
resource kvAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01' = {
  parent: keyVault
  name: 'add'
  properties: {
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: appIdentity.properties.principalId
        permissions: {
          secrets: ['get', 'list']
        }
      }
    ]
  }
}

// ── Custom hostname bindings ──────────────────────────────────────────────────
// DNS must point to the App Service before running ssl.bicep to issue managed certs.
resource hostNameBindings 'Microsoft.Web/sites/hostNameBindings@2023-12-01' = [for hostname in customHostnames: {
  parent: appService
  name: hostname
  properties: {
    siteName: appService.name
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
output appServiceName string = appService.name
output appServiceHostname string = appService.properties.defaultHostName
output postgresHostname string = postgresServer.properties.fullyQualifiedDomainName
output keyVaultName string = keyVault.name
