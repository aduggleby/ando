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
    // Pack with Release configuration
    Dotnet.Pack(project, o => o
        .WithConfiguration(Configuration.Release)
        .Output(Root / "packages"));

    // Push to NuGet.org
    Nuget.Push(
        Root / "packages" / "*.nupkg",
        Env("NUGET_API_KEY"),
        "https://api.nuget.org/v3/index.json"
    );

    Log.Info("Package published to NuGet.org");
}
```

## Versioned Publishing

Use Git tags for package versioning:

```csharp
var publish = DefineProfile("publish");

var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");
var version = Git.GetVersion();

Log.Info($"Building version {version}");

Dotnet.Restore(project);
Dotnet.Build(project, o => o.WithProperty("Version", version));
Dotnet.Test(Dotnet.Project("./tests/MyLibrary.Tests/MyLibrary.Tests.csproj"));

if (publish)
{
    // Pack with explicit version
    Dotnet.Pack(project, o => o
        .WithConfiguration(Configuration.Release)
        .WithProperty("Version", version)
        .Output(Root / "packages"));

    // Push versioned package
    Nuget.Push(
        Root / "packages" / $"MyLibrary.{version}.nupkg",
        Env("NUGET_API_KEY")
    );

    Log.Info($"Published MyLibrary {version} to NuGet.org");
}
```

## Pre-release Packages

Publish pre-release versions for testing:

```csharp
var publish = DefineProfile("publish");
var prerelease = DefineProfile("prerelease");

var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");
var baseVersion = Git.GetVersion();

string version;
if (prerelease)
{
    // Pre-release: 1.2.3-beta.45
    var buildNumber = Env("BUILD_NUMBER", "0");
    version = $"{baseVersion}-beta.{buildNumber}";
}
else
{
    version = baseVersion;
}

Log.Info($"Building version {version}");

Dotnet.Restore(project);
Dotnet.Build(project, o => o.WithProperty("Version", version));
Dotnet.Test(Dotnet.Project("./tests/MyLibrary.Tests/MyLibrary.Tests.csproj"));

if (publish)
{
    Dotnet.Pack(project, o => o
        .WithConfiguration(Configuration.Release)
        .WithProperty("Version", version)
        .Output(Root / "packages"));

    Nuget.Push(
        Root / "packages" / $"MyLibrary.{version}.nupkg",
        Env("NUGET_API_KEY")
    );

    Log.Info($"Published {version}");
}
```

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

var version = Git.GetVersion();

// Build all packages
Dotnet.SdkInstall();
foreach (var project in packages)
{
    Dotnet.Restore(project);
    Dotnet.Build(project, o => o.WithProperty("Version", version));
}

// Run all tests
Dotnet.Test(Dotnet.Project("./tests/MyLibrary.Tests/MyLibrary.Tests.csproj"));

if (publish)
{
    // Pack all
    foreach (var project in packages)
    {
        Dotnet.Pack(project, o => o
            .WithConfiguration(Configuration.Release)
            .WithProperty("Version", version)
            .Output(Root / "packages"));
    }

    // Push all packages
    Nuget.Push(
        Root / "packages" / "*.nupkg",
        Env("NUGET_API_KEY")
    );

    Log.Info($"Published {packages.Length} packages at version {version}");
}
```

## With Symbol Packages

Include debug symbols for better debugging:

```csharp
var publish = DefineProfile("publish");

var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");
var version = Git.GetVersion();

Dotnet.Restore(project);
Dotnet.Build(project, o => o.WithProperty("Version", version));
Dotnet.Test(Dotnet.Project("./tests/MyLibrary.Tests/MyLibrary.Tests.csproj"));

if (publish)
{
    // Pack with symbol package
    Dotnet.Pack(project, o => o
        .WithConfiguration(Configuration.Release)
        .WithProperty("Version", version)
        .WithProperty("IncludeSymbols", "true")
        .WithProperty("SymbolPackageFormat", "snupkg")
        .Output(Root / "packages"));

    // Push both .nupkg and .snupkg
    Nuget.Push(
        Root / "packages" / $"MyLibrary.{version}.nupkg",
        Env("NUGET_API_KEY")
    );

    Log.Info($"Published {version} with symbols");
}
```

## Source Link Integration

Enable source link for debugging into source:

```csharp
var publish = DefineProfile("publish");

var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");
var version = Git.GetVersion();

Dotnet.Restore(project);
Dotnet.Build(project, o => o
    .WithProperty("Version", version)
    .WithProperty("ContinuousIntegrationBuild", "true"));

Dotnet.Test(Dotnet.Project("./tests/MyLibrary.Tests/MyLibrary.Tests.csproj"));

if (publish)
{
    Dotnet.Pack(project, o => o
        .WithConfiguration(Configuration.Release)
        .WithProperty("Version", version)
        .WithProperty("ContinuousIntegrationBuild", "true")
        .WithProperty("EmbedUntrackedSources", "true")
        .WithProperty("IncludeSymbols", "true")
        .WithProperty("SymbolPackageFormat", "snupkg")
        .Output(Root / "packages"));

    Nuget.Push(
        Root / "packages" / $"MyLibrary.{version}.nupkg",
        Env("NUGET_API_KEY")
    );

    Log.Info($"Published {version} with Source Link");
}
```

Your `.csproj` should include:
```xml
<PropertyGroup>
  <PublishRepositoryUrl>true</PublishRepositoryUrl>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.SourceLink.GitHub" Version="8.0.0" PrivateAssets="All"/>
</ItemGroup>
```

## Validate Before Publishing

Dry-run and validate package:

```csharp
var publish = DefineProfile("publish");
var validate = DefineProfile("validate");

var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");
var version = Git.GetVersion();

Dotnet.Restore(project);
Dotnet.Build(project, o => o.WithProperty("Version", version));
Dotnet.Test(Dotnet.Project("./tests/MyLibrary.Tests/MyLibrary.Tests.csproj"));

// Always pack for validation
Dotnet.Pack(project, o => o
    .WithConfiguration(Configuration.Release)
    .WithProperty("Version", version)
    .Output(Root / "packages"));

if (validate || publish)
{
    // Validate package metadata
    Nuget.Verify(Root / "packages" / $"MyLibrary.{version}.nupkg");
    Log.Info("Package validation passed");
}

if (publish)
{
    Nuget.Push(
        Root / "packages" / $"MyLibrary.{version}.nupkg",
        Env("NUGET_API_KEY")
    );

    Log.Info($"Published {version}");
}
```

Usage:
```bash
# Validate only
ando -p validate

# Validate and publish
ando -p publish
```

## Key Operations

| Operation | Purpose |
|-----------|---------|
| [Dotnet.Pack()](/providers/dotnet#pack) | Create NuGet package |
| [Nuget.Push()](/providers/nuget#push) | Push to NuGet feed |
| [Nuget.Verify()](/providers/nuget#verify) | Validate package |
| [Git.GetVersion()](/providers/git#getversion) | Get version from tags |

## Tips

- **API key security** - Store `NUGET_API_KEY` as a secret, never in code
- **Semantic versioning** - Follow semver for predictable updates
- **Test before publish** - Always run tests before packaging
- **Symbol packages** - Include `.snupkg` for better debugging
- **Source Link** - Enable for debugging into source code

## See Also

- [NuGet Provider](/providers/nuget) - Full API reference
- [Git Versioning](/examples/git-versioning) - Version management
- [GitHub Packages](/examples/nuget-github-packages) - Private package hosting
