---
title: Post-Release Auto-Update Hook
description: Automatically update your global .NET tool after publishing to NuGet.
category: Hooks
tags:
  - hooks
  - nuget
  - releases
  - automation
  - dotnet-tools
---

## Overview

After publishing a new version of a .NET tool to NuGet.org, you often want to update your local installation to use the new version. This recipe shows how to create a post-release hook that waits for the package to be available on NuGet and then automatically updates.

## The Problem

When you run `ando release` and publish to NuGet.org:
1. The package is uploaded
2. NuGet processes and indexes the package (can take 1-5 minutes)
3. Even after the NuGet API reports availability, CDN propagation can cause `dotnet tool update` to fail
4. You need to manually run `dotnet tool update -g <tool>` after it's fully available

## The Solution

Create a post-release hook that polls the NuGet API and updates automatically:

**scripts/ando-post-release.csando:**
```csharp
// Read the current version from the project file
var csprojPath = Root / "src/MyTool/MyTool.csproj";
var csprojContent = System.IO.File.ReadAllText(csprojPath);
var versionMatch = System.Text.RegularExpressions.Regex.Match(
    csprojContent, @"<Version>([^<]+)</Version>");

if (!versionMatch.Success)
{
    Log.Error("Could not read version from .csproj");
    return;
}

var version = versionMatch.Groups[1].Value;
Log.Info($"Waiting for v{version} to be available on NuGet.org...");

// Poll NuGet API until the version is available (max 5 minutes)
var nugetUrl = $"https://api.nuget.org/v3-flatcontainer/mytool/{version}/mytool.nuspec";
var maxAttempts = 30;
var delaySeconds = 10;
var available = false;

var httpClient = new System.Net.Http.HttpClient();

for (int i = 0; i < maxAttempts; i++)
{
    try
    {
        var response = await httpClient.GetAsync(nugetUrl);
        if (response.IsSuccessStatusCode)
        {
            available = true;
            break;
        }
    }
    catch
    {
        // Network error, keep trying
    }

    if (i < maxAttempts - 1)
    {
        Log.Info($"  Not available yet, retrying in {delaySeconds}s... ({i + 1}/{maxAttempts})");
        await System.Threading.Tasks.Task.Delay(delaySeconds * 1000);
    }
}

httpClient.Dispose();

if (!available)
{
    Log.Warning($"Timed out waiting for v{version} on NuGet.org");
    Log.Warning("You can manually update later with: dotnet tool update -g mytool");
    return;
}

Log.Info($"Version {version} is now available on NuGet.org!");
Log.Info("Updating global tool...");

// Update the global tool with retry logic
// NuGet CDN propagation can cause failures even after the API reports availability
var updateMaxAttempts = 10;
var updateDelaySeconds = 15;
var updateSuccess = false;

for (int attempt = 1; attempt <= updateMaxAttempts; attempt++)
{
    var result = await Shell.RunAsync("dotnet", "tool", "update", "-g", "mytool", "--version", version);
    if (result.ExitCode == 0)
    {
        Log.Info("Successfully updated tool!");
        updateSuccess = true;
        break;
    }

    if (attempt < updateMaxAttempts)
    {
        Log.Info($"  Update failed, retrying in {updateDelaySeconds}s... ({attempt}/{updateMaxAttempts})");
        await System.Threading.Tasks.Task.Delay(updateDelaySeconds * 1000);
    }
}

if (!updateSuccess)
{
    Log.Warning($"Failed to update tool after {updateMaxAttempts} attempts");
    Log.Warning("You can manually update with: dotnet tool update -g mytool");
}
```

## Hook File Location

Post-release hooks are named with the `ando-post-<command>` pattern:

```
my-project/
├── build.csando
├── scripts/
│   ├── ando-pre.csando           # Before every command
│   ├── ando-post.csando          # After every command
│   └── ando-post-release.csando  # After 'ando release' specifically
└── src/
    └── ...
```

## Hook Naming Convention

| Hook File | When It Runs |
|-----------|--------------|
| `ando-pre.csando` | Before every command |
| `ando-post.csando` | After every command |
| `ando-pre-<cmd>.csando` | Before specific command |
| `ando-post-<cmd>.csando` | After specific command |

Examples:
- `ando-post-release.csando` - After `ando release`
- `ando-pre-build.csando` - Before `ando build` (alias for `run`)
- `ando-post-commit.csando` - After `ando commit`

## Hook API

Post-hooks have access to these globals:

| Global | Description |
|--------|-------------|
| `Root` | Project root path (supports `/` operator) |
| `Log` | Logging operations (Info, Warning, Error, Debug) |
| `Shell` | Execute shell commands asynchronously |
| `Env(name)` | Read environment variables |
| `Directory(path)` | Create directory references |

## Extended Example: Notify on Release

Send a notification after a successful release:

```csharp
// Read version
var csprojPath = Root / "src/MyApp/MyApp.csproj";
var content = System.IO.File.ReadAllText(csprojPath);
var match = System.Text.RegularExpressions.Regex.Match(content, @"<Version>([^<]+)</Version>");
var version = match.Success ? match.Groups[1].Value : "unknown";

// Send Slack notification
var webhookUrl = Env("SLACK_WEBHOOK_URL", required: false);
if (!string.IsNullOrEmpty(webhookUrl))
{
    var httpClient = new System.Net.Http.HttpClient();
    var payload = $@"{{""text"": ""Released MyApp v{version}!""}}";
    var content = new System.Net.Http.StringContent(
        payload,
        System.Text.Encoding.UTF8,
        "application/json");

    await httpClient.PostAsync(webhookUrl, content);
    httpClient.Dispose();

    Log.Info("Sent Slack notification");
}

// Update local tool
Log.Info("Updating local tool installation...");
await Shell.RunAsync("dotnet", "tool", "update", "-g", "myapp");
```

## Error Handling

Hooks should handle errors gracefully and not block the user:

```csharp
try
{
    // Attempt the update
    var result = await Shell.RunAsync("dotnet", "tool", "update", "-g", "mytool");

    if (result.ExitCode != 0)
    {
        // Log warning but don't fail
        Log.Warning("Auto-update failed, manual update may be required");
    }
}
catch (Exception ex)
{
    // Log and continue
    Log.Warning($"Hook error: {ex.Message}");
}

// Hook completes successfully even if update failed
```

## See Also

- [Pre-Build Cleanup Hook](/recipes/pre-hook-cleanup) - Clean up files before builds
- [GitHub Release Management](/recipes/github-releases) - Create releases with ANDO
