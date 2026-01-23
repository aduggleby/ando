---
title: Azure
description: Manage Azure resources and authentication using the Azure CLI.
provider: Azure
---

## Authentication

Azure operations require authentication via the Azure CLI. Choose the method that fits your environment.

### Environment Variables (Service Principal)

| Variable | Description |
|----------|-------------|
| `AZURE_CLIENT_ID` | Service principal application ID |
| `AZURE_CLIENT_SECRET` | Service principal secret |
| `AZURE_TENANT_ID` | Azure AD tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Target subscription ID |

## Example

Authenticate and create a resource group.

```csharp
// For CI/CD: login with service principal
Azure.LoginWithServicePrincipal();
Azure.SetSubscription();

// For local dev: ensure already logged in
Azure.EnsureLoggedIn();

// Create resource group
Azure.CreateResourceGroup("my-app-rg", "eastus");
```
