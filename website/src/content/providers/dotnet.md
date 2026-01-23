---
title: Dotnet
description: Build, test, and publish .NET projects using the dotnet CLI.
provider: Dotnet
---

## Example

Build and publish a .NET application.

```csharp
// Define project references
var App = Dotnet.Project("./src/App/App.csproj");
var Tests = Dotnet.Project("./tests/App.Tests/App.Tests.csproj");

// Restore, build, and test
Dotnet.Restore(App);
Dotnet.Build(App, o => o.Configuration = Configuration.Release);
Dotnet.Test(Tests);

// Publish as self-contained single file
Dotnet.Publish(App, o => o
  .Output(Root / "dist")
  .WithConfiguration(Configuration.Release)
  .WithRuntime("linux-x64")
  .AsSelfContained()
  .AsSingleFile());
```

## Options Reference

### Dotnet.Restore Options

| Option | Description |
|--------|-------------|
| `WithRuntime(string)` | Set target runtime identifier (e.g., "linux-x64", "win-x64", "osx-arm64"). Downloads runtime-specific packages. |
| `NoCache` | Bypass the NuGet cache, forcing fresh downloads of all packages. Useful when debugging package resolution issues. |

### Dotnet.Build Options

| Option | Description |
|--------|-------------|
| `Configuration` | Build configuration: `Configuration.Debug` or `Configuration.Release`. Debug includes symbols and disables optimizations. Release enables optimizations for production. |
| `NoRestore` | Skip the implicit restore before building. Use when you've already run `Dotnet.Restore()` separately. |

### Dotnet.Test Options

| Option | Description |
|--------|-------------|
| `Configuration` | Build configuration for running tests. Tests typically run in Debug for better diagnostics. |
| `NoRestore` | Skip restore before testing. Use when dependencies are already restored. |
| `NoBuild` | Skip build before testing. Use when the project is already built. Implies `NoRestore`. |
| `Filter` | Test filter expression to run specific tests. Examples: `"Category=Unit"`, `"FullyQualifiedName~MyNamespace"`, `"Name=MyTest"`. |

### Dotnet.Publish Options

| Option | Description |
|--------|-------------|
| `Output(path)` | Output directory for published artifacts. Accepts `BuildPath` or `string`. |
| `WithConfiguration(Configuration)` | Build configuration. Use `Configuration.Release` for production deployments. |
| `WithRuntime(string)` | Target runtime identifier. Required for self-contained deployments. Common values: `"linux-x64"`, `"linux-arm64"`, `"win-x64"`, `"osx-x64"`, `"osx-arm64"`. |
| `AsSelfContained(bool)` | Include the .NET runtime with the application. Creates a standalone deployment that doesn't require .NET to be installed. Defaults to `true` when called. |
| `AsSingleFile(bool)` | Bundle the entire application into a single executable. Makes deployment simpler but increases startup time slightly. |
| `SkipRestore()` | Skip restore before publishing. Use when dependencies are already restored. |
| `SkipBuild()` | Skip build before publishing. Use when the project is already built. |
