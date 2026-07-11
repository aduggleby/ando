---
title: Azure
description: Manage Azure resources and authentication using the Azure CLI.
provider: Azure
---

## Authentication

Azure operations require authentication via the Azure CLI. Choose the method that fits your environment.

### Environment Variables (Service Principal)

| Variable                         | Description                                                                                                                                             |
| -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AZURE_CLIENT_ID`                | Service principal application ID                                                                                                                        |
| `AZURE_CLIENT_SECRET`            | Service principal secret                                                                                                                                |
| `AZURE_TENANT_ID`                | Azure AD tenant ID                                                                                                                                      |
| `AZURE_SUBSCRIPTION_ID`          | Target subscription ID                                                                                                                                  |
| `ANDO_REQUIRE_SERVICE_PRINCIPAL` | When truthy (`1`, `true`, `yes`, `on`), forces service principal auth and fails instead of falling back to an existing CLI session or interactive login |

### Requiring Service Principal Authentication

By default, `Azure.EnsureAuthenticated()` uses service principal credentials when available and otherwise falls back to an existing `az login` session or interactive login. In CI/CD you often want to guarantee the build runs as the intended service principal and never as a developer's personal session.

Pass `requireServicePrincipal: true` (or set the `ANDO_REQUIRE_SERVICE_PRINCIPAL` environment variable) to fail fast when no service principal credentials are present. You can also pass credentials explicitly instead of relying on environment variables:

```csharp
// Force SP auth, reading credentials from env vars
Azure.EnsureAuthenticated(requireServicePrincipal: true);

// Force SP auth with explicit credentials (no env reliance)
Azure.EnsureAuthenticated(clientId, clientSecret, tenantId, requireServicePrincipal: true);
```

### Isolated CLI Profile

Service principal and managed identity logins always run against an isolated, per-build `AZURE_CONFIG_DIR` created in the system temp directory. The user's `~/.azure` profile is never read or modified: an ANDO build cannot overwrite a developer's personal `az login` session, and `az` steps in the build cannot silently fall back to it. The isolated profile (which holds the service principal's access tokens) is deleted when the build process exits.

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
