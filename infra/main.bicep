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
@description('SMTP username')
param smtpUsername string

@secure()
@description('SMTP password')
param smtpPassword string

// ── Naming ────────────────────────────────────────────────────────────────────
var prefix = 'akerogarden-${environmentName}'

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
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: false // B1 doesn't guarantee AlwaysOn; acceptable for this tier
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
          name: 'ConnectionStrings__Redis'
          value: '${redisCache.properties.hostName}:${redisCache.properties.sslPort},password=${redisCache.listKeys().primaryKey},ssl=True,abortConnect=False'
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
          name: 'Smtp__Username'
          value: smtpUsername
        }
        {
          name: 'Smtp__Password'
          value: smtpPassword
        }
        {
          name: 'Blob__ConnectionString'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=core.windows.net'
        }
      ]
    }
    virtualNetworkSubnetId: appSubnet.id
  }
}

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

// ── Azure Cache for Redis ─────────────────────────────────────────────────────
resource redisCache 'Microsoft.Cache/redis@2023-08-01' = {
  name: '${prefix}-redis'
  location: location
  properties: {
    sku: {
      name: 'Basic'
      family: 'C'
      capacity: 0
    }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
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
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────
output appServiceName string = appService.name
output appServiceHostname string = appService.properties.defaultHostName
output postgresHostname string = postgresServer.properties.fullyQualifiedDomainName
