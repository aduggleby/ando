# ando hooks - Implementation Plan

## Overview

ANDO hooks are `.csando` scripts that run automatically before and after CLI commands. They use the same Roslyn scripting infrastructure as `build.csando`, making them cross-platform and consistent with the rest of ANDO.

## Hook Types

| Hook | When it runs |
|------|--------------|
| `ando-pre.csando` | Before ANY command |
| `ando-pre-{cmd}.csando` | Before specific command |
| `ando-post-{cmd}.csando` | After specific command |
| `ando-post.csando` | After ANY command |

Commands with hooks: `run`, `bump`, `commit`, `clean`, `verify`

## Search Locations

Hooks are searched in this order (first found wins):

1. `./scripts/ando-{hook}.csando`
2. `./ando-{hook}.csando`

## Execution Order

For `ando bump`:

```
1. scripts/ando-pre.csando        (general pre-hook)
2. scripts/ando-pre-bump.csando   (command-specific pre-hook)
3. [ando bump executes]
4. scripts/ando-post-bump.csando  (command-specific post-hook)
5. scripts/ando-post.csando       (general post-hook)
```

## Example Setup

```
my-project/
├── scripts/
│   ├── ando-pre.csando           # Runs before any command
│   ├── ando-pre-bump.csando      # Runs before bump only
│   └── ando-post-bump.csando     # Runs after bump only
├── build.csando
└── src/
```

## Example Hooks

### Clean Syncthing Conflicts (ando-pre.csando)

```csharp
// scripts/ando-pre.csando
// Removes Syncthing conflict files before any ANDO command

var conflicts = Directory.GetFiles(".", "*.sync-conflict-*", SearchOption.AllDirectories);

foreach (var file in conflicts)
{
    Log.Info($"Removing conflict file: {file}");
    File.Delete(file);
}

if (conflicts.Length > 0)
{
    Log.Info($"Cleaned {conflicts.Length} conflict file(s)");
}
```

### Run Tests Before Bump (ando-pre-bump.csando)

```csharp
// scripts/ando-pre-bump.csando
// Ensure tests pass before allowing version bump

Log.Info("Running tests before bump...");

var result = await Shell.RunAsync("dotnet", "test", "--no-build");

if (result.ExitCode != 0)
{
    Log.Error("Tests failed. Aborting bump.");
    Environment.Exit(1);
}
```

### Notify After Bump (ando-post-bump.csando)

```csharp
// scripts/ando-post-bump.csando
// Send notification after successful bump

var version = Env("ANDO_NEW_VERSION", required: false);

if (version != null)
{
    Log.Info($"Version bumped to {version}");

    // Could send Slack notification, update issue tracker, etc.
}
```

### Build Verification Before Commit (ando-pre-commit.csando)

```csharp
// scripts/ando-pre-commit.csando
// Verify build passes before committing

Log.Info("Verifying build...");

var result = await Shell.RunAsync("dotnet", "build", "--nologo", "-v", "q");

if (result.ExitCode != 0)
{
    Log.Error("Build failed. Fix errors before committing.");
    Environment.Exit(1);
}
```

## Hook Behavior

### Exit Codes

| Hook Type | Non-zero exit | Effect |
|-----------|---------------|--------|
| Pre-hook | Aborts command | Command does not run |
| Post-hook | Warning only | Command already completed |

### Environment Variables

Hooks receive context via environment variables:

| Variable | Description | Available in |
|----------|-------------|--------------|
| `ANDO_COMMAND` | Current command (bump, commit, etc.) | All hooks |
| `ANDO_OLD_VERSION` | Version before bump | bump hooks |
| `ANDO_NEW_VERSION` | Version after bump | post-bump |
| `ANDO_BUMP_TYPE` | patch, minor, or major | bump hooks |

### Available APIs

Hooks have access to the same globals as `build.csando`:

| Global | Description |
|--------|-------------|
| `Log.Info()`, `Log.Warning()`, `Log.Error()` | Logging |
| `Env(name)` | Environment variables |
| `Root` | Project root path |
| `Directory(path)` | Directory reference |
| `Shell.RunAsync(cmd, args)` | Run shell commands |

### Missing Hooks

Missing hooks are silently skipped. No error or warning.

### Hook Execution Context

- Hooks run on the **host machine** (not in Docker)
- Working directory is the project root
- Same .NET runtime as ANDO CLI

## File Structure

```
src/Ando/
├── Cli/
│   └── Commands/
│       └── ... (existing commands)
│
├── Hooks/                         # NEW directory
│   ├── HookRunner.cs              # Discovers and executes hooks
│   ├── HookContext.cs             # Environment variables for hooks
│   └── HookScriptHost.cs          # Roslyn host for hook scripts
│
└── Scripting/
    └── ScriptHost.cs              # Existing - may need refactoring
```

## Implementation Details

### HookRunner.cs

```csharp
// =============================================================================
// HookRunner.cs
//
// Discovers and executes hook scripts. Searches for .csando files in
// scripts/ and root directories, runs them with Roslyn scripting.
// =============================================================================

public class HookRunner
{
    private readonly string _projectRoot;
    private readonly HookScriptHost _scriptHost;
    private readonly IConsole _console;

    public enum HookType { Pre, Post }

    public async Task<bool> RunHooksAsync(HookType type, string command, HookContext context)
    {
        // Run general hook first (ando-pre or ando-post)
        var generalHook = FindHook($"ando-{type.ToString().ToLower()}");
        if (generalHook != null)
        {
            if (!await ExecuteHookAsync(generalHook, context))
                return false;
        }

        // Run command-specific hook (ando-pre-bump, ando-post-commit, etc.)
        var specificHook = FindHook($"ando-{type.ToString().ToLower()}-{command}");
        if (specificHook != null)
        {
            if (!await ExecuteHookAsync(specificHook, context))
                return false;
        }

        return true;
    }

    private string? FindHook(string hookName)
    {
        var searchPaths = new[]
        {
            Path.Combine(_projectRoot, "scripts", $"{hookName}.csando"),
            Path.Combine(_projectRoot, $"{hookName}.csando")
        };

        return searchPaths.FirstOrDefault(File.Exists);
    }

    private async Task<bool> ExecuteHookAsync(string hookPath, HookContext context)
    {
        try
        {
            _console.WriteLine($"Running hook: {Path.GetFileName(hookPath)}");

            await _scriptHost.ExecuteAsync(hookPath, context.ToEnvironment());

            return true;
        }
        catch (HookAbortException ex)
        {
            _console.WriteLine($"Hook aborted: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            _console.WriteLine($"Hook failed: {ex.Message}");
            return false;
        }
    }
}
```

### HookContext.cs

```csharp
// =============================================================================
// HookContext.cs
//
// Provides context information to hooks via environment variables.
// =============================================================================

public class HookContext
{
    public string Command { get; init; } = "";
    public string? OldVersion { get; init; }
    public string? NewVersion { get; init; }
    public string? BumpType { get; init; }

    public Dictionary<string, string> ToEnvironment()
    {
        var env = new Dictionary<string, string>
        {
            ["ANDO_COMMAND"] = Command
        };

        if (OldVersion != null)
            env["ANDO_OLD_VERSION"] = OldVersion;

        if (NewVersion != null)
            env["ANDO_NEW_VERSION"] = NewVersion;

        if (BumpType != null)
            env["ANDO_BUMP_TYPE"] = BumpType;

        return env;
    }
}
```

### Integration with Commands

Each command integrates hooks in its execute method:

```csharp
// In BumpCommand.ExecuteAsync
public async Task<int> ExecuteAsync(BumpType type)
{
    var hookRunner = new HookRunner(_projectRoot);
    var context = new HookContext
    {
        Command = "bump",
        BumpType = type.ToString().ToLower()
    };

    // Run pre-hooks
    if (!await hookRunner.RunHooksAsync(HookType.Pre, "bump", context))
    {
        _console.WriteLine("Bump aborted by pre-hook.");
        return 1;
    }

    // ... do bump logic ...

    // Update context with results
    context = context with
    {
        OldVersion = oldVersion,
        NewVersion = newVersion
    };

    // Run post-hooks (don't abort on failure, just warn)
    await hookRunner.RunHooksAsync(HookType.Post, "bump", context);

    return 0;
}
```

## CLI Output

### Hook runs successfully

```
$ ando bump
Running hook: ando-pre.csando
  Cleaned 2 conflict file(s)
Running hook: ando-pre-bump.csando
  Running tests before bump...
  Tests passed.

Detected projects:
  src/Ando/Ando.csproj                    0.9.23
  ...

Bumping patch: 0.9.23 → 0.9.24

...

Running hook: ando-post-bump.csando
  Version bumped to 0.9.24

Committed: Bump version to 0.9.24
```

### Pre-hook aborts command

```
$ ando bump
Running hook: ando-pre-bump.csando
  Running tests before bump...
  Tests failed. Aborting bump.

Bump aborted by pre-hook.
```

### No hooks found

```
$ ando bump
Detected projects:
  ...
```

(No hook messages - silently skipped)

## Testing Strategy

### Unit Tests

| Test | Description |
|------|-------------|
| `HookRunner_FindsScriptsDirectory` | Finds hooks in scripts/ |
| `HookRunner_FindsRootDirectory` | Finds hooks in root |
| `HookRunner_PrefersScriptsOverRoot` | scripts/ takes precedence |
| `HookRunner_SkipsMissingHooks` | No error when hook doesn't exist |
| `HookRunner_RunsGeneralBeforeSpecific` | ando-pre runs before ando-pre-bump |
| `HookRunner_AbortsOnPreHookFailure` | Non-zero exit stops command |
| `HookRunner_ContinuesOnPostHookFailure` | Post-hook failure is warning only |
| `HookContext_SetsEnvironmentVariables` | Context populates env vars |

### Integration Tests

| Test | Description |
|------|-------------|
| `Bump_RunsPreAndPostHooks` | Full hook lifecycle |
| `Commit_RunsPreAndPostHooks` | Hooks work with commit |
| `PreHook_CanAbortCommand` | Exit 1 prevents command |
| `Hook_HasAccessToGlobals` | Log, Env, Root work in hooks |

## Error Handling

| Error | Behavior |
|-------|----------|
| Hook file not found | Silently skip |
| Hook syntax error | Abort with error message |
| Pre-hook exits non-zero | Abort command |
| Post-hook exits non-zero | Warning, command already done |
| Hook throws exception | Abort (pre) or warn (post) |

## Future Enhancements

Out of scope for initial implementation:

- `--no-hooks` flag to skip all hooks
- `--skip-hook <name>` to skip specific hook
- Hook timeout configuration
- Async/parallel hook execution
- Hook for `ando run` that runs inside the container
