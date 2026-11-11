---
title: Azure
description: Manage Azure resources and authentication using the Azure CLI.
provider: Azure
---

## Pinning the Azure CLI version

`Azure.InstallCli("2.90.0")` installs exactly that `azure-cli` version in the build container (see also [Tool installation](/tools)). The version must be exactly `X.Y.Z`.

- It skips the install when `az` already reports that version.
- Otherwise it adds Microsoft's signed apt repository (key `/etc/apt/keyrings/microsoft.gpg`, codename from `/etc/os-release`) and checks that `azure-cli=<version>-1~<codename>` exists. If it does not, the step fails and lists the 15 newest available versions.
- It installs with `--allow-downgrades`, runs `apt-mark hold azure-cli` and logs `az version`.
- The automatic `az` install never replaces it.
- Put it **before** the first Azure, AppService, Functions or Bicep step: an `az` that was installed automatically earlier in the run is replaced (downgraded) by this step.
- It needs a Debian or Ubuntu image.

```csharp
Azure.InstallCli("2.90.0");
Azure.EnsureAuthenticated();
```

## Authentication

Azure operations require authentication via the Azure CLI. Choose the method that fits your environment.

### Environment Variables (Service Principal)

| Variable                         | Description                                                                                                                                                                                                         |
| -------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `AZURE_CLIENT_ID`                | Service principal application ID                                                                                                                                                                                    |
| `AZURE_CLIENT_SECRET`            | Service principal secret                                                                                                                                                                                            |
| `AZURE_CLIENT_CERTIFICATE_PATH`  | Optional. PEM file (key and certificate) inside the build container, for example after `Ando.CopyToContainer`. When set, the service principal logs in with the certificate and `AZURE_CLIENT_SECRET` is not needed |
| `AZURE_TENANT_ID`                | Azure AD tenant ID                                                                                                                                                                                                  |
| `AZURE_SUBSCRIPTION_ID`          | Target subscription ID                                                                                                                                                                                              |
| `ANDO_REQUIRE_SERVICE_PRINCIPAL` | When truthy (`1`, `true`, `yes`, `on`), forces service principal auth and fails instead of falling back to an existing CLI session or interactive login                                                             |

Service principal credentials reach the container through environment variables, never on the `docker exec` command line (`az login --service-principal -u "$AZURE_CLIENT_ID" -p "$AZURE_CLIENT_SECRET" ...` is run by `sh -c`, which expands the variables inside the container). Environment values for all container commands are likewise passed to Docker by name only.

With a client secret, `az login` still receives the secret as an argument inside the container for the few seconds the login runs, which makes it visible to `ps` on the host during that time. Certificate login (`AZURE_CLIENT_CERTIFICATE_PATH`, which needs an `az` version with `--certificate`, for example 2.67 or newer; the automatically installed `az` is current) is the only way to keep the credential out of every process argument list.

```csharp
// Certificate login: copy the PEM into the container, then set AZURE_CLIENT_CERTIFICATE_PATH=/workspace/.run/seal.pem
Ando.CopyToContainer(".run/seal.pem", ".run/seal.pem");
Azure.EnsureAuthenticated();
```

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

Every run gets a private `AZURE_CONFIG_DIR` inside the build container (`/tmp/ando-azure-<guid>`). It is deleted when the run ends, and leftovers from killed runs are removed at the start of the next run (every `ando-azure-*` folder in the container, whatever its age, so only one run per repository container can be active at a time). After a third Ctrl+C the folder stays in a warm container until the next run starts. With `--cold` the whole container is removed when the run ends, even after a forced exit, so the profile never outlives the run. The host's `~/.azure` profile is never read or modified: an ANDO build cannot overwrite a developer's personal `az login` session, and `az` steps in the build cannot fall back to it. Logins do not persist in a warm container between runs.

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

// For local dev: Azure.EnsureAuthenticated() reuses an existing az session
// (Azure.EnsureLoggedIn() is deprecated and gives a compile warning)
Azure.EnsureAuthenticated();

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
