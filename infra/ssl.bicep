// Åkerögården — Managed SSL certificates
// Run AFTER main.bicep AND after DNS records for all hostnames point to the App Service.
// Deploy: az deployment group create --resource-group <rg> --template-file ssl.bicep --parameters @<env>.ssl-parameters.json

@description('Name of the App Service (output from main.bicep)')
param appServiceName string

@description('App Service Plan resource ID (output from main.bicep as appServicePlanId)')
param appServicePlanId string

@description('Hostnames to issue managed certificates for')
param customHostnames array

resource appService 'Microsoft.Web/sites@2023-12-01' existing = {
  name: appServiceName
}

resource managedCerts 'Microsoft.Web/certificates@2023-12-01' = [for hostname in customHostnames: {
  name: '${appServiceName}-${replace(hostname, '.', '-')}'
  location: resourceGroup().location
  properties: {
    serverFarmId: appServicePlanId
    canonicalName: hostname
  }
}]

resource sslBindings 'Microsoft.Web/sites/hostNameBindings@2023-12-01' = [for (hostname, i) in customHostnames: {
  parent: appService
  name: hostname
  dependsOn: [managedCerts]
  properties: {
    siteName: appServiceName
    sslState: 'SniEnabled'
    thumbprint: managedCerts[i].properties.thumbprint
  }
}]
