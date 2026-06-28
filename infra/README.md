# Infrastructure

Bicep templates for Åkerögården. Both production and staging deploy from the same `main.bicep` with different parameter files.

## Prerequisites

- Azure CLI: `az login`
- Bicep CLI: `az bicep install`
- Key Vault pre-created with secrets: `postgres-admin-password`, `smtp-username`, `smtp-password`
- Replace `SUBSCRIPTION_ID` in both parameter files with the actual Azure subscription ID

## Deploy staging

```bash
az group create --name akerogarden-staging --location northeurope

az deployment group create \
  --resource-group akerogarden-staging \
  --template-file main.bicep \
  --parameters @staging.parameters.json
```

## Deploy production

```bash
az group create --name akerogarden-prod --location northeurope

az deployment group create \
  --resource-group akerogarden-prod \
  --template-file main.bicep \
  --parameters @prod.parameters.json
```

## Power staging down (save cost)

```bash
az webapp stop --resource-group akerogarden-staging --name akerogarden-staging-app
az postgres flexible-server stop --resource-group akerogarden-staging --name akerogarden-staging-pg
```
