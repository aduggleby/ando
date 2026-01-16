# 0008-AzureFullStack: Full Stack Azure Deployment with EF Migrations

This example demonstrates a complete deployment workflow: building an application, deploying Azure infrastructure, running database migrations, and preparing the app for deployment.

## Prerequisites

1. **Azure CLI** installed (`az --version`)
2. **Logged in to Azure** (`az login`)
3. **.NET SDK 9.0** installed (`dotnet --version`)
4. **EF Core tools** (`dotnet tool install --global dotnet-ef`)

## Environment Variables

| Variable | Required | Description | Default |
|----------|----------|-------------|---------|
| `SQL_ADMIN_PASSWORD` | **Yes** | SQL Server admin password | - |
| `AZURE_SUBSCRIPTION_ID` | No | Target subscription ID | Current default |
| `AZURE_RESOURCE_GROUP` | No | Resource group name | `ando-fullstack-rg` |
| `AZURE_LOCATION` | No | Azure region | `eastus` |

## What This Example Deploys

1. **App Service Plan** (Linux, Basic tier)
2. **Web App** (.NET 9.0, configured with connection string)
3. **SQL Server** (Azure SQL logical server)
4. **SQL Database** (Basic tier, 2GB)

## Build Steps

1. Restore and build the .NET web application
2. Verify Azure CLI authentication
3. Create resource group (if needed)
4. Deploy infrastructure via Bicep
5. Capture deployment outputs (connection string, URLs)
6. Run EF Core migrations against Azure SQL
7. Publish application to `./dist`

## Running the Example

```bash
# Login to Azure
az login

# Set required environment variable
export SQL_ADMIN_PASSWORD="YourSecureP@ssword123!"

# Optional: customize deployment
export AZURE_RESOURCE_GROUP="my-app-rg"
export AZURE_LOCATION="westeurope"

# Run the build
ando
```

## Output Variables

After deployment, these values are captured in `Context.Vars` (with `azure_` prefix):

| Variable | Description |
|----------|-------------|
| `azure_webAppName` | App Service name |
| `azure_webAppUrl` | App Service URL |
| `azure_sqlServerName` | SQL Server name |
| `azure_sqlServerFqdn` | SQL Server FQDN |
| `azure_sqlDatabaseName` | Database name |
| `azure_sqlConnectionString` | Full connection string |

## Deploying the Application

After the build completes, deploy the published app to Azure:

```bash
# Using Azure CLI
az webapp deploy \
  --resource-group ando-fullstack-rg \
  --name <webAppName from output> \
  --src-path ./dist \
  --type zip
```

## Creating Initial Migration

Before running this example for the first time, create an initial migration:

```bash
cd src/WebApp
dotnet ef migrations add InitialCreate
```

## Cleanup

To delete all resources:

```bash
az group delete --name ando-fullstack-rg --yes
```

## Security Notes

- The SQL admin password should be a strong, unique password
- In production, use Azure Key Vault for secrets
- Consider using managed identities instead of SQL authentication
- The SQL firewall rule allows Azure services; restrict further for production
