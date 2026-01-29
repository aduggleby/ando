---
title: "refactor: Consolidation and Bug Fixes Batch"
type: refactor
date: 2026-01-29
priority: high
---

# Consolidation and Bug Fixes Batch

## Overview

This plan addresses 10 items across two categories: 4 refactoring opportunities to reduce code duplication and improve maintainability, and 6 bug fixes/improvements to address resource leaks, configuration inconsistencies, and missing functionality.

## Problem Statement

The ANDO codebase has grown organically and accumulated several patterns that need consolidation:

1. **Code Duplication**: ScriptOptions configuration duplicated between execution and verification paths; 4 near-identical RegisterCommand overloads
2. **Brittle Patterns**: Tool availability checks rely on string-prefix matching of step names
3. **Configuration Confusion**: Docker image default defined in two places with different values
4. **Resource Leaks**: JsonDocument and HttpClient not properly disposed
5. **Missing Functionality**: Host path translation never enabled; early logs don't respect verbosity; cancellation not supported

## Proposed Solution

Implement fixes in priority order:
1. **Phase 1 (Critical Bugs)**: Fix resource leaks and path translation
2. **Phase 2 (Configuration)**: Resolve Docker image default and verbosity timing
3. **Phase 3 (Refactoring)**: Extract shared helpers and consolidate patterns
4. **Phase 4 (Enhancements)**: Improve .env parsing and add cancellation support

---

## Technical Approach

### Phase 1: Critical Bug Fixes

#### 1.1 Fix SetHostRootPath Never Called

**File**: `src/Ando/Cli/AndoCli.cs` (lines 394-396)

**Problem**: `ContainerExecutor.SetHostRootPath()` is defined but never called, so absolute host paths can't be translated to container paths.

**Fix**:
```csharp
// src/Ando/Cli/AndoCli.cs:394-396 (BEFORE)
var containerExecutor = new ContainerExecutor(container.Id, _logger);
context.SetExecutor(containerExecutor);
context.SetDockerManager(dockerManager, container.Id, hostRootPath);

// src/Ando/Cli/AndoCli.cs:394-397 (AFTER)
var containerExecutor = new ContainerExecutor(container.Id, _logger);
containerExecutor.SetHostRootPath(hostRootPath);  // Enable path translation
context.SetExecutor(containerExecutor);
context.SetDockerManager(dockerManager, container.Id, hostRootPath);
```

**Test**: Add test case with absolute host path to verify translation.

---

#### 1.2 Fix JsonDocument Not Disposed

**File**: `src/Ando/Utilities/VersionResolver.cs` (lines 72, 129, 174)

**Problem**: `JsonDocument.Parse()` returns an `IDisposable` that's never disposed, causing memory leaks.

**Fix**: Wrap each call in `using`:

```csharp
// Line 72 - GetLatestDotnetSdkVersionAsync (BEFORE)
var json = JsonDocument.Parse(response);
var channelVersion = json.RootElement...

// (AFTER)
using var json = JsonDocument.Parse(response);
var channelVersion = json.RootElement...

// Line 129 - GetLatestNodeLtsVersionAsync (BEFORE)
var json = JsonDocument.Parse(response);
var releases = json.RootElement...

// (AFTER)
using var json = JsonDocument.Parse(response);
var releases = json.RootElement...

// Line 174 - GetLatestNpmVersionAsync (BEFORE)
var json = JsonDocument.Parse(response);
var latest = json.RootElement...

// (AFTER)
using var json = JsonDocument.Parse(response);
var latest = json.RootElement...
```

---

#### 1.3 Fix HttpClient Not Disposed

**File**: `src/Ando/Scripting/BuildOperations.cs` (line 117)

**Problem**: New `HttpClient` created per build but never disposed, causing socket exhaustion.

**Option A** (Simple - make static):
```csharp
// src/Ando/Scripting/BuildOperations.cs
private static readonly HttpClient SharedHttpClient = new HttpClient();

// In CreateVersionResolver:
private VersionResolver CreateVersionResolver()
{
    return new VersionResolver(SharedHttpClient, _logger);
}
```

**Option B** (Full - implement IDisposable):
```csharp
public class BuildOperations : IDisposable
{
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public BuildOperations(...)
    {
        _httpClient = new HttpClient();
        // ...
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
    }
}
```

**Recommendation**: Option A is simpler and HttpClient is designed to be long-lived. Static is fine for CLI tool.

---

### Phase 2: Configuration Fixes

#### 2.1 Consolidate Docker Image Default

**Files**:
- `src/Ando/Cli/AndoCli.cs:654`
- `src/Ando/Execution/DockerManager.cs:48`

**Problem**: Two different defaults - `ubuntu:22.04` vs `mcr.microsoft.com/dotnet/sdk:9.0-alpine`

**Decision Required**: Which default is authoritative?

**Proposed Solution**: Keep `ubuntu:22.04` as the CLI default (more compatible), remove the default from `ContainerConfig.Image`:

```csharp
// src/Ando/Execution/DockerManager.cs:48 (BEFORE)
public string Image { get; set; } = "mcr.microsoft.com/dotnet/sdk:9.0-alpine";

// (AFTER) - Remove default, force explicit specification
public string Image { get; set; } = null!;
```

**Alternative**: Create a constants class:
```csharp
// src/Ando/BuildDefaults.cs
public static class BuildDefaults
{
    public const string DockerImage = "ubuntu:22.04";
}
```

---

#### 2.2 Fix Verbosity Set Too Late

**File**: `src/Ando/Cli/AndoCli.cs`

**Problem**: `_logger.Verbosity = GetVerbosity()` called at line ~400, after profile logs and DIND prompts.

**Fix**: Move verbosity setting to immediately after logger initialization (around line 240):

```csharp
// Near top of RunAsync() method, after logger is created
_logger.Verbosity = GetVerbosity();

// Then proceed with rest of initialization
```

---

### Phase 3: Refactoring

#### 3.1 Extract Shared ScriptOptions Configuration

**File**: `src/Ando/Scripting/ScriptHost.cs` (lines 75-94, 136-155)

**Problem**: Identical `ScriptOptions` configuration in two methods.

**Fix**: Extract to private helper:

```csharp
// src/Ando/Scripting/ScriptHost.cs

private ScriptOptions CreateScriptOptions()
{
    return ScriptOptions.Default
        .AddReferences(
            typeof(BuildContext).Assembly,
            typeof(object).Assembly,
            typeof(Console).Assembly,
            typeof(File).Assembly,
            typeof(Task).Assembly,
            typeof(Enumerable).Assembly)
        .AddImports(
            "System",
            "System.IO",
            "System.Linq",
            "System.Threading.Tasks",
            "System.Collections.Generic",
            "Ando.Context",
            "Ando.Profiles",
            "Ando.References",
            "Ando.Operations",
            "Ando.Workflow",
            "Ando.Steps");
}

// Usage in LoadScriptAsync:
var options = CreateScriptOptions();
var script = CSharpScript.Create<object>(code, options, typeof(ScriptGlobals));

// Usage in VerifyScriptAsync:
var options = CreateScriptOptions();
var script = CSharpScript.Create<object>(code, options, typeof(ScriptGlobals));
```

---

#### 3.2 Collapse RegisterCommand Overloads

**File**: `src/Ando/Operations/OperationsBase.cs` (lines 35-175)

**Problem**: 4 near-identical method bodies.

**Fix**: Extract command options building to helper, use single registration method:

```csharp
// src/Ando/Operations/OperationsBase.cs

private record CommandRegistration(
    string StepName,
    string Command,
    Func<string[]> ArgsBuilder,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    Func<Task<bool>>? Ensurer = null);

private CommandOptions BuildCommandOptions(CommandRegistration reg)
{
    var args = reg.ArgsBuilder();
    var options = new CommandOptions
    {
        WorkingDirectory = reg.WorkingDirectory,
        // ... other common configuration
    };

    if (reg.Environment != null)
    {
        foreach (var (key, value) in reg.Environment)
        {
            options.Environment[key] = value;
        }
    }

    return options;
}

protected void RegisterCommand(CommandRegistration registration)
{
    Registry.Register(registration.StepName, async () =>
    {
        if (registration.Ensurer != null)
        {
            var ensured = await registration.Ensurer();
            if (!ensured) return false;
        }

        var options = BuildCommandOptions(registration);
        var result = await ExecutorFactory().ExecuteAsync(
            registration.Command,
            registration.ArgsBuilder(),
            options);
        return result.Success;
    });
}

// Public overloads become thin wrappers:
protected void RegisterCommand(string stepName, string command, string[] args, ...)
    => RegisterCommand(new CommandRegistration(stepName, command, () => args, ...));

protected void RegisterCommand(string stepName, string command, Func<ArgumentBuilder> argsBuilder, ...)
    => RegisterCommand(new CommandRegistration(stepName, command, () => argsBuilder().Build(), ...));
```

---

#### 3.3 Centralize Tool Requirements

**File**: `src/Ando/Workflow/ToolAvailabilityCheckers.cs`

**Problem**: String-prefix matching is brittle (`Azure.`, `Cloudflare.`, `Functions.`).

**Option A** (Minimal - constants):
```csharp
// src/Ando/Workflow/ToolRequirements.cs
public static class ToolRequirements
{
    public static readonly string[] AzureCliOperations =
        ["Azure.", "Bicep."];

    public static readonly string[] CloudflareOperations =
        ["Cloudflare."];

    public static readonly string[] FunctionsOperations =
        ["Functions."];
}

// Usage in checkers:
public bool CanCheck(string stepName) =>
    ToolRequirements.AzureCliOperations.Any(prefix => stepName.StartsWith(prefix));
```

**Option B** (Full - registry pattern):
```csharp
// Operations self-declare their tool requirements
public enum ToolDependency { None, AzureCli, Cloudflare, FunctionsCore }

// In BuildStep:
public ToolDependency? RequiredTool { get; init; }

// When registering steps:
Registry.Register("Azure.Login", async () => { ... },
    new StepOptions { RequiredTool = ToolDependency.AzureCli });
```

**Recommendation**: Start with Option A for minimal change; Option B for future-proofing.

---

### Phase 4: Enhancements

#### 4.1 Improve .env Parsing

**File**: `src/Ando/Cli/AndoCli.cs` (lines 1084-1115)

**Current Limitations**:
- No `export KEY=VALUE` support
- No inline comments: `KEY=VALUE # comment`
- No escaped characters

**Proposed Enhancements**:

```csharp
// src/Ando/Cli/EnvFileParser.cs (new file)
public static class EnvFileParser
{
    public static Dictionary<string, string> Parse(string content)
    {
        var result = new Dictionary<string, string>();

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            // Handle 'export KEY=VALUE' syntax
            if (trimmed.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[7..].TrimStart();

            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0) continue;

            var key = trimmed[..eqIndex].Trim();
            var value = trimmed[(eqIndex + 1)..];

            // Remove inline comments (but not if inside quotes)
            value = RemoveInlineComment(value);

            // Handle quoted values
            value = UnquoteValue(value);

            result[key] = value;
        }

        return result;
    }

    private static string RemoveInlineComment(string value)
    {
        // Only remove # if not inside quotes
        // ... implementation
    }

    private static string UnquoteValue(string value)
    {
        value = value.Trim();
        if ((value.StartsWith('"') && value.EndsWith('"')) ||
            (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            value = value[1..^1];
            // Handle escape sequences for double quotes
            if (value.Contains("\\\""))
                value = value.Replace("\\\"", "\"");
        }
        return value;
    }
}
```

---

#### 4.2 Add CancellationToken Support

**Scope Decision Required**:
- **Option 1**: Top-level only (~5 files)
- **Option 2**: Full propagation (~50+ files)
- **Option 3**: Timeout-only (minimal)

**Recommended: Option 1** - Start with top-level cancellation:

```csharp
// src/Ando/Workflow/WorkflowRunner.cs
public async Task<bool> RunAsync(CancellationToken cancellationToken = default)
{
    foreach (var step in _registry.GetSteps())
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Execute step...
    }
}

// src/Ando/Cli/AndoCli.cs
private readonly CancellationTokenSource _cts = new();

public AndoCli(string[] args)
{
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;  // Prevent immediate termination
        _cts.Cancel();
    };
}

// Pass to workflow:
var result = await workflow.RunAsync(_cts.Token);
```

---

## Acceptance Criteria

### Phase 1: Critical Bugs
- [x] `SetHostRootPath` called after creating `ContainerExecutor`
- [x] All `JsonDocument.Parse()` wrapped in `using` statements
- [x] `HttpClient` is static/shared or properly disposed
- [x] Existing tests pass

### Phase 2: Configuration
- [x] Single source of truth for Docker image default
- [x] Verbosity set before any logging occurs
- [x] Early logs respect `--verbosity quiet`

### Phase 3: Refactoring
- [x] `ScriptHost` uses extracted `CreateScriptOptions()` helper
- [x] `OperationsBase` has single internal registration helper
- [x] Tool requirements centralized (at minimum, constants file)
- [x] All existing tests pass after refactoring

### Phase 4: Enhancements
- [x] `.env` parser supports `export KEY=VALUE`
- [x] `.env` parser handles inline comments correctly
- [x] Ctrl+C during build triggers graceful shutdown
- [x] CancellationToken flows to WorkflowRunner

---

## Risk Analysis & Mitigation

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| SetHostRootPath fix changes behavior | Medium | High | Add tests before fixing; log translated paths |
| Docker image default change breaks builds | Medium | High | Document change; consider deprecation warning |
| OperationsBase refactor breaks operations | Low | Medium | Extensive existing test coverage |
| .env parsing changes break existing files | Low | Medium | Additive changes only; maintain backward compat |

---

## Implementation Order

1. **Phase 1.2**: JsonDocument disposal (lowest risk, clear fix)
2. **Phase 1.3**: HttpClient disposal (low risk)
3. **Phase 2.2**: Verbosity timing (low risk, simple move)
4. **Phase 1.1**: SetHostRootPath (add tests first)
5. **Phase 3.1**: ScriptOptions extraction (pure refactor)
6. **Phase 2.1**: Docker image consolidation (needs decision)
7. **Phase 3.2**: OperationsBase consolidation (larger refactor)
8. **Phase 3.3**: Tool requirements (after 3.2)
9. **Phase 4.1**: .env parsing improvements
10. **Phase 4.2**: CancellationToken support

---

## Testing Strategy

### Unit Tests
- `ContainerExecutorTests.cs`: Add path translation tests
- `VersionResolverTests.cs`: Verify disposal (memory profiling optional)
- `EnvFileParserTests.cs`: New test file for parsing edge cases
- `OperationsBaseTests.cs`: Verify refactored helpers

### Integration Tests
- Verify absolute paths work in container
- Verify tool availability detection
- Verify .env loading with various formats

### E2E Tests
- Build with `--verbosity quiet` produces no output
- Ctrl+C during long build exits cleanly

---

## References

### Internal Files
- `src/Ando/Scripting/ScriptHost.cs:75-94, 136-155` - ScriptOptions duplication
- `src/Ando/Operations/OperationsBase.cs:35-175` - RegisterCommand overloads
- `src/Ando/Workflow/ToolAvailabilityCheckers.cs:26-58` - String-prefix matching
- `src/Ando/Cli/AndoCli.cs:654` - Docker image default
- `src/Ando/Execution/DockerManager.cs:48` - ContainerConfig default
- `src/Ando/Execution/ContainerExecutor.cs:58-62` - SetHostRootPath
- `src/Ando/Utilities/VersionResolver.cs:72,129,174` - JsonDocument
- `src/Ando/Scripting/BuildOperations.cs:117` - HttpClient

### Patterns Used
- [IDisposable pattern](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-dispose)
- [HttpClient guidelines](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines)
- [CancellationToken patterns](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads)
