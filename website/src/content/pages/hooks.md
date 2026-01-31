---
title: ANDO Hooks
description: Pre-build and post-build hooks for automating tasks before and after ANDO commands.
toc: true
---

## Overview

ANDO hooks are `.csando` scripts that run automatically before and after CLI commands. They execute on the host machine (not in Docker) and are useful for:

- **Pre-build cleanup** - Remove temporary files, conflict files, or stale artifacts
- **Validation** - Run tests or checks before allowing a command to proceed
- **Notifications** - Send Slack/Discord messages after releases
- **Auto-updates** - Update local tool installations after publishing
- **Environment setup** - Configure environment before builds

## Hook Types

| Hook | When It Runs |
|------|--------------|
| `ando-pre.csando` | Before **every** command |
| `ando-post.csando` | After **every** command |
| `ando-pre-{cmd}.csando` | Before a **specific** command |
| `ando-post-{cmd}.csando` | After a **specific** command |

**Execution order:**
1. `ando-pre.csando` (general pre-hook)
2. `ando-pre-{cmd}.csando` (command-specific pre-hook)
3. *Command executes*
4. `ando-post-{cmd}.csando` (command-specific post-hook)
5. `ando-post.csando` (general post-hook)

## File Locations

ANDO searches for hooks in these locations (first found wins):

1. `./scripts/ando-{hook}.csando`
2. `./ando-{hook}.csando`

**Recommended structure:**
```
my-project/
├── build.csando
├── scripts/
│   ├── ando-pre.csando           # Before every command
│   ├── ando-post.csando          # After every command
│   ├── ando-pre-run.csando       # Before 'ando run'
│   ├── ando-post-release.csando  # After 'ando release'
│   └── ando-pre-bump.csando      # Before 'ando bump'
└── src/
    └── ...
```

## Hook API

Hooks have access to a subset of the build script API:

| Global | Type | Description |
|--------|------|-------------|
| `Root` | `BuildPath` | Project root path. Supports `/` operator for paths. |
| `Log` | `HookLogOperations` | Logging: `Log.Info()`, `Log.Warning()`, `Log.Error()`, `Log.Debug()` |
| `Shell` | `ShellOperations` | Execute commands: `await Shell.RunAsync("cmd", "args")` |
| `Env(name)` | `Function` | Get environment variable. Use `Env("NAME", required: false)` for optional. |
| `Directory(path)` | `Function` | Create a `DirectoryRef` from a path. |

## Environment Variables

These environment variables are available in hooks:

| Variable | Available In | Description |
|----------|--------------|-------------|
| `ANDO_COMMAND` | All hooks | The command being executed (`run`, `bump`, `commit`, etc.) |
| `ANDO_OLD_VERSION` | `bump` hooks | Version before the bump |
| `ANDO_NEW_VERSION` | `post-bump` only | Version after the bump |
| `ANDO_BUMP_TYPE` | `bump` hooks | Type of bump: `patch`, `minor`, or `major` |

## Pre-Hook Behavior

**Pre-hooks can abort the command** by:
- Throwing an exception
- Returning a non-zero exit code from `Shell.RunAsync()`

```csharp
// scripts/ando-pre-bump.csando
// Abort bump if tests fail

var result = await Shell.RunAsync("dotnet", "test", "--no-build");
if (result.ExitCode != 0)
{
    throw new Exception("Tests must pass before bumping version");
}
```

## Post-Hook Behavior

**Post-hooks cannot abort** - they only warn on failure since the command has already completed.

```csharp
// scripts/ando-post-release.csando
// Update tool after release (failures don't block)

try
{
    await Shell.RunAsync("dotnet", "tool", "update", "-g", "mytool");
    Log.Info("Tool updated successfully");
}
catch (Exception ex)
{
    Log.Warning($"Auto-update failed: {ex.Message}");
    // Hook continues - doesn't fail the release
}
```

## Command Names

Use these command names in hook filenames:

| Command | Hook Names |
|---------|------------|
| `ando` or `ando run` | `ando-pre-run.csando`, `ando-post-run.csando` |
| `ando commit` | `ando-pre-commit.csando`, `ando-post-commit.csando` |
| `ando bump` | `ando-pre-bump.csando`, `ando-post-bump.csando` |
| `ando docs` | `ando-pre-docs.csando`, `ando-post-docs.csando` |
| `ando release` | `ando-pre-release.csando`, `ando-post-release.csando` |
| `ando clean` | `ando-pre-clean.csando`, `ando-post-clean.csando` |
| `ando verify` | `ando-pre-verify.csando`, `ando-post-verify.csando` |

## Examples

### Pre-Build Cleanup

Remove temporary files before every build:

```csharp
// scripts/ando-pre.csando

var patterns = new[] { "*.sync-conflict-*", "*.orig", "*~", ".DS_Store" };
var removed = 0;

foreach (var pattern in patterns)
{
    var files = System.IO.Directory
        .EnumerateFiles(Root, pattern, System.IO.SearchOption.AllDirectories)
        .Where(f => !f.Contains("node_modules") && !f.Contains(".git"));

    foreach (var file in files)
    {
        try
        {
            System.IO.File.Delete(file);
            removed++;
        }
        catch { }
    }
}

if (removed > 0)
{
    Log.Info($"Cleaned up {removed} temporary file(s)");
}
```

### Validate Before Bump

Ensure tests pass before version bump:

```csharp
// scripts/ando-pre-bump.csando

Log.Info("Running tests before version bump...");

var result = await Shell.RunAsync("dotnet", "test", "--no-build");
if (result.ExitCode != 0)
{
    throw new Exception("All tests must pass before bumping version");
}

Log.Info("Tests passed!");
```

### Auto-Update After Release

Update the local tool installation after publishing:

```csharp
// scripts/ando-post-release.csando

var version = Env("ANDO_NEW_VERSION", required: false);
if (string.IsNullOrEmpty(version))
{
    Log.Warning("ANDO_NEW_VERSION not set, skipping auto-update");
    return;
}

Log.Info($"Updating global tool to v{version}...");

// Retry logic for NuGet CDN propagation
for (int i = 0; i < 10; i++)
{
    var result = await Shell.RunAsync("dotnet", "tool", "update", "-g", "mytool");
    if (result.ExitCode == 0)
    {
        Log.Info("Tool updated successfully!");
        return;
    }

    Log.Info($"  Waiting for NuGet... ({i + 1}/10)");
    await System.Threading.Tasks.Task.Delay(15000);
}

Log.Warning("Auto-update failed. Run manually: dotnet tool update -g mytool");
```

### Notify on Release

Send a Slack notification after release:

```csharp
// scripts/ando-post-release.csando

var webhookUrl = Env("SLACK_WEBHOOK_URL", required: false);
if (string.IsNullOrEmpty(webhookUrl)) return;

var version = Env("ANDO_NEW_VERSION", required: false) ?? "unknown";
var payload = $@"{{""text"": "":rocket: Released v{version}!""}}";

using var client = new System.Net.Http.HttpClient();
var content = new System.Net.Http.StringContent(
    payload, System.Text.Encoding.UTF8, "application/json");

await client.PostAsync(webhookUrl, content);
Log.Info("Sent Slack notification");
```

### Conditional Hook

Only run for specific commands:

```csharp
// scripts/ando-pre.csando

var command = Env("ANDO_COMMAND", required: false) ?? "run";

// Only clean before 'run', not 'verify' or 'clean'
if (command != "run") return;

// Cleanup logic here...
```

## Tips

- **Use `scripts/` directory** - Keeps hooks organized and separate from build scripts
- **Handle errors gracefully** - Post-hooks should warn, not fail
- **Check command type** - Use `ANDO_COMMAND` to run conditionally
- **Test hooks** - Run `ando verify` to test pre-hooks without executing the build
- **Timeout** - Hooks have a 5-minute timeout to prevent hangs

## See Also

- [Pre-Build Cleanup Hook](/examples/pre-hook-cleanup) - Full cleanup example
- [Post-Release Auto-Update](/examples/post-release-hook) - NuGet update example
- [CLI Reference](/cli) - All ANDO commands
