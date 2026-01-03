# 0007-AzureBicep: Azure Bicep Deployment Example

This example demonstrates how to use ANDO's Azure and Bicep operations to deploy Azure infrastructure.

## Prerequisites

1. **Azure CLI** installed (`az --version`)
2. **Logged in to Azure** (`az login`)
3. Optional: Set environment variables for configuration

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `AZURE_SUBSCRIPTION_ID` | Target subscription ID | Current default subscription |
| `AZURE_RESOURCE_GROUP` | Resource group name | `ando-demo-rg` |
| `AZURE_LOCATION` | Azure region | `eastus` |

## What This Example Does

1. Verifies Azure CLI authentication
2. Optionally sets the target subscription
3. Creates a resource group (if it doesn't exist)
4. Deploys a storage account using Bicep
5. Captures deployment outputs to `Context.Vars`

## Running the Example

```bash
# Login to Azure first
az login

# Run with defaults
ando --local

# Or with custom configuration
export AZURE_RESOURCE_GROUP="my-custom-rg"
export AZURE_LOCATION="westeurope"
ando --local
```

## Output Capture

After deployment, the following values are available in `Context.Vars`:
- `storageAccountName` - The generated storage account name
- `storageAccountId` - The Azure resource ID
- `blobEndpoint` - The blob storage endpoint URL

These can be used in subsequent build steps (e.g., deploying an application that uses the storage account).

## Cleanup

To delete the resources:

```bash
az group delete --name ando-demo-rg --yes
```
