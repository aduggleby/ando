---
title: Ando
description: Globals and helpers available in all build.csando scripts.
provider: Ando
---

## Globals

These globals are available in all build.csando scripts without any prefix or import.

```csharp
// Directory reference
var frontend = Directory("./frontend");

// Path construction with / operator
var output = Root / "dist";
var cache = Temp / "build-cache";

// Access environment variables
var dbUrl = Env("DATABASE_URL");
var apiKey = Env("API_KEY", required: false);

// Use regular C# variables for custom values
var buildNumber = "123";
```

## Profiles

Profiles allow conditional execution of build steps. Define profiles at the top of your script and activate them via the `-p` CLI flag.

```csharp
// Define profiles at the top of your build.csando
var release = DefineProfile("release");
var push = DefineProfile("push");

// Project references
var app = Dotnet.Project("./src/App/App.csproj");
var tests = Dotnet.Project("./tests/App.Tests/App.Tests.csproj");

// Always run build and tests
Dotnet.Build(app);
Dotnet.Test(tests);

// Only run when "release" profile is active
if (release) {
  Dotnet.Publish(app, o => o.Output(Root / "dist"));
  Git.Tag("v1.0.0");
}

// Only run when "push" profile is active
if (push) {
  Nuget.EnsureAuthenticated();
  Nuget.Pack(app);
  Nuget.Push(app, o => o.SkipDuplicate());
}

// CLI usage:
// ando              # Build and test only
// ando -p release   # Build, test, publish, and tag
// ando -p push      # Build, test, pack, and push to NuGet
```

## Log

Logging operations for outputting messages during the build. Visibility depends on verbosity level.

```csharp
// Informational messages (visible at Normal verbosity)
Log.Info("Starting build process...");

// Warnings (visible at Minimal verbosity and above)
Log.Warning("Cache directory not found, rebuilding from scratch");

// Errors (always visible)
Log.Error("Build failed: missing dependency");

// Debug messages (only visible at Detailed verbosity)
Log.Debug($"Processing file: {filePath}");
```

## Build Configuration

Build configuration, artifact copying, and nested builds. Use these to set the Docker image, copy outputs to host, and run child build scripts.

```csharp
// Set the Docker image for the build container
UseImage("mcr.microsoft.com/dotnet/sdk:9.0");

// Copy artifacts from container to host after build
CopyArtifactsToHost("dist", "./dist");

// Copy as compressed archive (faster for many small files)
CopyZippedArtifactsToHost("dist", "./output");  // Creates ./output/artifacts.tar.gz
CopyZippedArtifactsToHost("dist", "./dist/binaries.tar.gz");  // Specific filename
CopyZippedArtifactsToHost("dist", "./dist/binaries.zip");     // Zip format

// Run a child build in a subdirectory
Build(Directory("./website"));

// Run a specific build file
Build(Directory("./website") / "deploy.csando");

// Child build with Docker-in-Docker enabled
Build(Directory("./integration-tests"), o => o.WithDind());
```
