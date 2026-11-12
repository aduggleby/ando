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

## Exact commands

Each operation runs one `dotnet` command. Parts in brackets are only added when the matching option is set. Nothing is added for the configuration by default, so a build or publish without `WithConfiguration` uses the .NET default (Debug): **Release is not the default**.

| Operation        | Command                                                                                                                                                                          |
| ---------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Dotnet.Restore` | `dotnet restore <proj> [--no-cache] [-r <rid>]`                                                                                                                                  |
| `Dotnet.Build`   | `dotnet build <proj> [-c <cfg>] [--no-restore]`                                                                                                                                  |
| `Dotnet.Test`    | `dotnet test <proj> [-c <cfg>] [--no-restore] [--no-build] [--filter <f>]`                                                                                                       |
| `Dotnet.Publish` | `dotnet publish <proj> [-c <cfg>] [-o /workspace/<out>] [--no-restore] [--no-build] [--self-contained \| --no-self-contained] [-r <rid>] [-p:PublishSingleFile=true] [--nologo]` |

A relative `Output("out/web")` string resolves against `/workspace` in the build container (normal and `--ref` runs), so the command gets `-o /workspace/out/web` and later steps can read the relative `out/web`. A container-absolute path such as `/workspace/out/web` and a `Root / "web"` path are kept. For example, `dotnet publish p -c Release -o out --nologo` is:

```csharp
Dotnet.Publish(p, o => o.WithConfiguration(Configuration.Release).Output("out").WithNoLogo());
```

## Reading a version: ReadVersion

`Dotnet.ReadVersion(path = "Directory.Build.props")` is **not a step**. It is evaluated when the script loads, on the host, relative to the source root (the `--ref` worktree when set). The file needs exactly one `<Version>` directly in a `<PropertyGroup>`. It reads the literal property: no MSBuild evaluation, imports, conditions or `$(...)`. It throws on a missing file or an empty or ambiguous version. For the version of one project file, use `Dotnet.Project(...).Version`.

```csharp
var version = Dotnet.ReadVersion();
Log.Info($"Building {version}");
```

## Options Reference

### Dotnet.Restore Options

| Option                | Description                                                                                                       |
| --------------------- | ----------------------------------------------------------------------------------------------------------------- |
| `WithRuntime(string)` | Set target runtime identifier (e.g., "linux-x64", "win-x64", "osx-arm64"). Downloads runtime-specific packages.   |
| `NoCache`             | Bypass the NuGet cache, forcing fresh downloads of all packages. Useful when debugging package resolution issues. |

### Dotnet.Build Options

| Option                       | Description                                                                                                                                                                        |
| ---------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Configuration`              | Build configuration: `Configuration.Debug` or `Configuration.Release`. Debug includes symbols and disables optimizations. Release enables optimizations for production.            |
| `WithSourceRevision(string)` | Pass `-p:SourceRevisionId=<sha>`. ANDO adds the full commit being built automatically (the container has no `.git`; nothing is added outside a git repository); this overrides it. |
| `WithoutSourceRevision()`    | Suppress the automatic `-p:SourceRevisionId`. Without it, ANDO's value overrides a `<SourceRevisionId>` set in the project file.                                                   |
| `NoRestore`                  | Skip the implicit restore before building. Use when you've already run `Dotnet.Restore()` separately.                                                                              |

### Dotnet.Test Options

| Option                       | Description                                                                                                                                                            |
| ---------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Configuration`              | Build configuration for running tests. Tests typically run in Debug for better diagnostics.                                                                            |
| `NoRestore`                  | Skip restore before testing. Use when dependencies are already restored.                                                                                               |
| `NoBuild`                    | Skip build before testing. Use when the project is already built. Implies `NoRestore`.                                                                                 |
| `Filter`                     | Test filter expression to run specific tests. Examples: `"Category=Unit"`, `"FullyQualifiedName~MyNamespace"`, `"Name=MyTest"`.                                        |
| `WithSourceRevision(string)` | Pass `-p:SourceRevisionId=<sha>` when the test builds. ANDO adds the full commit being built automatically (nothing outside a git repository) unless `NoBuild` is set. |
| `WithoutSourceRevision()`    | Suppress the automatic `-p:SourceRevisionId`. Without it, ANDO's value overrides a `<SourceRevisionId>` set in the project file.                                       |

### Dotnet.Publish Options

| Option                             | Description                                                                                                                                                                                   |
| ---------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Output(path)`                     | Output directory for published artifacts. A `string` is a container path: relative paths resolve against `/workspace`. A `BuildPath` is used as given (`Root / "dist"` is `/workspace/dist`). |
| `WithConfiguration(Configuration)` | Build configuration. Use `Configuration.Release` for production deployments.                                                                                                                  |
| `WithRuntime(string)`              | Target runtime identifier. Required for self-contained deployments. Common values: `"linux-x64"`, `"linux-arm64"`, `"win-x64"`, `"osx-x64"`, `"osx-arm64"`.                                   |
| `AsSelfContained(bool)`            | Include the .NET runtime with the application. Creates a standalone deployment that doesn't require .NET to be installed. Defaults to `true` when called.                                     |
| `AsSingleFile(bool)`               | Bundle the entire application into a single executable. Makes deployment simpler but increases startup time slightly.                                                                         |
| `SkipRestore()`                    | Skip restore before publishing. Use when dependencies are already restored.                                                                                                                   |
| `SkipBuild()`                      | Skip build before publishing. Use when the project is already built.                                                                                                                          |
| `WithSourceRevision(string)`       | Pass `-p:SourceRevisionId=<sha>`. ANDO adds the full commit being built automatically (nothing outside a git repository) unless this is set or `SkipBuild()` is used.                         |
| `WithoutSourceRevision()`          | Suppress the automatic `-p:SourceRevisionId`. Without it, ANDO's value overrides a `<SourceRevisionId>` set in the project file.                                                              |
| `WithNoLogo()`                     | Pass `--nologo` to suppress the startup banner.                                                                                                                                               |
