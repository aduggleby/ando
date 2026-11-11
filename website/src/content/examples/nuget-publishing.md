---
title: NuGet.org Publishing
description: Build and publish .NET packages to the public NuGet.org gallery.
category: .NET
tags:
  - dotnet
  - nuget
  - packages
  - publishing
---

## Overview

NuGet.org is the primary package repository for .NET libraries. This example shows how to build, pack, and publish NuGet packages as part of your ANDO build pipeline.

`Nuget.Pack` and `Nuget.Push` read the API key from `NUGET_API_KEY` (call `Nuget.EnsureAuthenticated()` early to be prompted when it is not set). `Nuget.Push` skips packages that already exist on the feed.

## Basic Package Publishing

Build and publish a package to NuGet.org:

```csharp
var publish = DefineProfile("publish");

var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");
var tests = Dotnet.Project("./tests/MyLibrary.Tests/MyLibrary.Tests.csproj");

// Build and test
Dotnet.SdkInstall();
Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(tests);

if (publish)
{
    Nuget.EnsureAuthenticated();

    // Pack with Release configuration (the default)
    Nuget.Pack(project, o => o.Output(Root / "packages"));

    // Push to NuGet.org (the default source)
    Nuget.Push(Root / "packages" / "*.nupkg");

    Log.Info("Package published to NuGet.org");
}
```

## Versioned Publishing

Use Git tags for package versioning. `Nuget.Pack` sets the package version; `Dotnet.Build` has no option for MSBuild properties, so the assembly version still comes from the project file.

```csharp
var publish = DefineProfile("publish");

var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");
var version = Git.LastTag("v*")?.TrimStart('v') ?? "0.0.0";

Log.Info($"Building version {version}");

Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(Dotnet.Project("./tests/MyLibrary.Tests/MyLibrary.Tests.csproj"));

if (publish)
{
    Nuget.EnsureAuthenticated();

    Nuget.Pack(project, o => o
        .WithVersion(version)
        .Output(Root / "packages"));

    Nuget.Push(Root / "packages" / $"MyLibrary.{version}.nupkg");

    Log.Info($"Published MyLibrary {version} to NuGet.org");
}
```

## Pre-release Packages

Publish pre-release versions for testing:

```csharp
var publish = DefineProfile("publish");
var prerelease = DefineProfile("prerelease");

var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");
var baseVersion = Git.LastTag("v*")?.TrimStart('v') ?? "0.0.0";

// Pre-release: 1.2.3-beta.45
var buildNumber = Env("BUILD_NUMBER", required: false) ?? "0";
var version = prerelease ? $"{baseVersion}-beta.{buildNumber}" : baseVersion;

Log.Info($"Building version {version}");

Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(Dotnet.Project("./tests/MyLibrary.Tests/MyLibrary.Tests.csproj"));

if (publish)
{
    Nuget.EnsureAuthenticated();

    Nuget.Pack(project, o => o
        .WithVersion(version)
        .Output(Root / "packages"));

    Nuget.Push(Root / "packages" / $"MyLibrary.{version}.nupkg");

    Log.Info($"Published {version}");
}
```

`WithVersionSuffix("beta.45")` is an alternative that appends a suffix to the version in the project file.

Usage:

```bash
# Publish stable release
ando -p publish

# Publish pre-release
BUILD_NUMBER=45 ando -p publish,prerelease
```

## Multi-Package Solution

Publish multiple packages from a solution:

```csharp
var publish = DefineProfile("publish");

var packages = new[]
{
    Dotnet.Project("./src/MyLibrary.Core/MyLibrary.Core.csproj"),
    Dotnet.Project("./src/MyLibrary.Extensions/MyLibrary.Extensions.csproj"),
    Dotnet.Project("./src/MyLibrary.AspNetCore/MyLibrary.AspNetCore.csproj"),
};

var version = Git.LastTag("v*")?.TrimStart('v') ?? "0.0.0";

// Build all packages
Dotnet.SdkInstall();
foreach (var project in packages)
{
    Dotnet.Restore(project);
    Dotnet.Build(project);
}

// Run all tests
Dotnet.Test(Dotnet.Project("./tests/MyLibrary.Tests/MyLibrary.Tests.csproj"));

if (publish)
{
    Nuget.EnsureAuthenticated();

    // Pack all
    foreach (var project in packages)
    {
        Nuget.Pack(project, o => o
            .WithVersion(version)
            .Output(Root / "packages"));
    }

    // Push all packages
    Nuget.Push(Root / "packages" / "*.nupkg");

    Log.Info($"Published {packages.Length} packages at version {version}");
}
```

## With Symbol Packages

Include debug symbols for better debugging:

```csharp
var publish = DefineProfile("publish");

var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");
var version = Git.LastTag("v*")?.TrimStart('v') ?? "0.0.0";

Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(Dotnet.Project("./tests/MyLibrary.Tests/MyLibrary.Tests.csproj"));

if (publish)
{
    Nuget.EnsureAuthenticated();

    // Pack with a symbol package
    Nuget.Pack(project, o => o
        .WithVersion(version)
        .WithSymbols()
        .Output(Root / "packages"));

    // Pushes the .nupkg and its .snupkg
    Nuget.Push(Root / "packages" / "*.nupkg");

    Log.Info($"Published {version} with symbols");
}
```

Use `WithoutSymbols()` on `Nuget.Push` to skip symbol packages.

## Source Link Integration

Source Link is configured in the project file, not in the build script. Your `.csproj` should include:

```xml
<PropertyGroup>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
  <EmbedUntrackedSources>true</EmbedUntrackedSources>
  <IncludeSymbols>true</IncludeSymbols>
  <SymbolPackageFormat>snupkg</SymbolPackageFormat>
  <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.SourceLink.GitHub" Version="8.0.0" PrivateAssets="All"/>
</ItemGroup>
```

The build script stays the same as in "With Symbol Packages". `Dotnet.Build` and `Nuget.Pack` cannot set MSBuild properties, so properties like these belong in the project file or `Directory.Build.props`.

## Validate Before Publishing

Pack on every run and publish only with the `publish` profile:

```csharp
var publish = DefineProfile("publish");

var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");
var version = Git.LastTag("v*")?.TrimStart('v') ?? "0.0.0";

Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(Dotnet.Project("./tests/MyLibrary.Tests/MyLibrary.Tests.csproj"));

// Always pack: a packaging error fails the build before anything is published
Nuget.Pack(project, o => o
    .WithVersion(version)
    .Output(Root / "packages"));

if (publish)
{
    Nuget.EnsureAuthenticated();
    Nuget.Push(Root / "packages" / $"MyLibrary.{version}.nupkg");

    Log.Info($"Published {version}");
}
```

Usage:

```bash
# Pack only
ando

# Pack and publish
ando -p publish
```

## Key Operations

| Operation                                                           | Purpose                         |
| ------------------------------------------------------------------- | ------------------------------- |
| [Nuget.Pack()](/providers/nuget#pack)                               | Create NuGet package            |
| [Nuget.Push()](/providers/nuget#push)                               | Push to NuGet feed              |
| [Nuget.EnsureAuthenticated()](/providers/nuget#ensureauthenticated) | Ensure the API key is available |
| [Git.LastTag()](/providers/git#lasttag)                             | Latest tag, used as the version |

## Tips

- **API key security** - Store `NUGET_API_KEY` as a secret, never in code
- **Semantic versioning** - Follow semver for predictable updates
- **Test before publish** - Always run tests before packaging
- **Symbol packages** - Include `.snupkg` for better debugging
- **Source Link** - Enable in the project file for debugging into source code

## See Also

- [NuGet Provider](/providers/nuget) - Full API reference
- [Git Versioning](/examples/git-versioning) - Version management
- [GitHub Packages](/examples/nuget-github-packages) - Private package hosting
