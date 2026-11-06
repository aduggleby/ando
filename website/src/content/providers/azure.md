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

By default, `Azure.EnsureAuthenticated()` uses service principal credentials when available and otherwise reuses an existing `az` session in the build container or runs `az login --use-device-code` inside the container. There are no host-side `az` checks or prompts. In CI/CD you often want to guarantee the build runs as the intended service principal and never as a developer's personal session.

Pass `requireServicePrincipal: true` (or set the `ANDO_REQUIRE_SERVICE_PRINCIPAL` environment variable) to fail fast when no service principal credentials are present. You can also pass credentials explicitly instead of relying on environment variables:

```csharp
// Force SP auth, reading credentials from env vars
Azure.EnsureAuthenticated(requireServicePrincipal: true);

// Force SP auth with explicit credentials (no env reliance)
Azure.EnsureAuthenticated(clientId, clientSecret, tenantId, requireServicePrincipal: true);
```

### Isolated CLI Profile

Every run gets a private `AZURE_CONFIG_DIR` inside the build container (`/tmp/ando-azure-<guid>`). It is deleted when the run ends, and leftovers from killed runs are removed at the start of the next run. The host's `~/.azure` profile is never read or modified: an ANDO build cannot overwrite a developer's personal `az login` session, and `az` steps in the build cannot fall back to it. Logins do not persist in a warm container between runs.

### Azure CLI in the container

The Azure CLI is installed automatically inside the build container before the first `az` step, if the image does not include it. This works on Debian and Ubuntu images; on other images, use an image that includes `az`.

### Redacted secrets

Values of environment variables whose names contain SECRET, PASSWORD, TOKEN, KEY and similar words (including `AZURE_CLIENT_SECRET`) are replaced with `[REDACTED]` in console output, the build log and run records. Values shorter than 6 characters are not redacted.

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

## Running az commands

`Azure.Cli(params string[] args)` runs `az` with the given arguments as the build's identity, in the build container. It returns a `CommandStep`: read `Value`, `Json(path)` or `ExitCode` at execution time, and configure it with `InDirectory`, `WithTimeout`, `WithEnv`, `AllowFailure` and `Quiet`. Add `"--output", "json"` or `"--query", "...", "-o", "tsv"` as needed.

```csharp
var show = Azure.Cli("webapp", "show", "-g", "my-rg", "-n", "my-app", "--slot", "release", "-o", "json").Quiet();
Ando.When(
  () => show.Json("siteConfig.ipSecurityRestrictionsDefaultAction") == "Deny",
  () => Log.Info("The slot restricts access"));
```

## Running a script on a VM

`Azure.Vm.RunCommand(resourceGroup, vmName, script, TimeSpan? timeout = null)` runs a shell script as root on a Linux VM with `az vm run-command invoke`. `az vm run-command` exits 0 even when the script fails, so ANDO wraps the script, reads its exit code and fails the step if the script exited non-zero. It returns a `StepOutput` whose `Value` is the script's stdout. The default timeout is 30 minutes.

```csharp
Azure.Vm.RunCommand("rg-eu", "docs-vm", "/usr/local/sbin/update-worker pkg.tar.gz", TimeSpan.FromMinutes(10));
```
