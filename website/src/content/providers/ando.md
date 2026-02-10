---
title: Ando
description: Globals and helpers available in all build.csando scripts.
provider: Ando
---

## Globals

These globals are available in all build.csando scripts without any prefix or import. `Env()`, `Directory()`, and `DefineProfile()` are **global functions** — call them directly, not as methods on the `Ando` object.

```csharp
// Directory reference (returns DirectoryRef)
var frontend = Directory("./frontend");

// Path construction with / operator (Root returns BuildPath)
var output = Root / "dist";
var cache = Temp / "build-cache";

// Access environment variables (global function, not Ando.Env)
var dbUrl = Env("DATABASE_URL");
var apiKey = Env("API_KEY", required: false);
var lang = Env("SITE_LANG", required: false) ?? "en";

// Use regular C# variables for custom values
var buildNumber = "123";
```

## Environment Variables

The `Env()` function retrieves environment variables. The second parameter is `required:` (a `bool`), NOT a default value string.

```csharp
// Required (default) - throws if not set
var apiKey = Env("API_KEY");

// Optional - returns null if not set
var optional = Env("OPTIONAL_VAR", required: false);

// Optional with default value - use null-coalescing (??)
var lang = Env("SITE_LANG", required: false) ?? "en";

// WRONG: second parameter is bool, not a default value
// var lang = Env("SITE_LANG", "en");  // Compile error!
```

There is no `SetEnv()` function. Set environment variables via `.env.ando` files, OS environment, or CI/CD configuration.

## BuildPath vs DirectoryRef

ANDO has two path types. Operations like `Npm.Ci()`, `Cloudflare.PagesDeploy()`, `Playwright.Test()`, and `Ando.Build()` require `DirectoryRef`, not `BuildPath`.

```csharp
// Root returns BuildPath - use for path construction
var outputPath = Root / "dist";
Dotnet.Publish(app, o => o.Output(outputPath));  // OK: accepts string/BuildPath

// Directory() returns DirectoryRef - use for operations
var frontend = Directory("./frontend");
Npm.Ci(frontend);  // OK: requires DirectoryRef

// WRONG: Npm.Ci(Root) - BuildPath is not DirectoryRef
// Use Directory(".") to reference the project root as DirectoryRef:
Npm.Ci(Directory("."));  // OK
```

## Profiles

Profiles allow conditional execution of build steps. `DefineProfile()` takes a **single argument** (the name) and returns a `Profile` with implicit `bool` conversion. There is no `HasProfile()`, `HasAnyProfile()`, or `Profile()` function.

```csharp
// Define profiles at the top of your build.csando
var release = DefineProfile("release");
var publish = DefineProfile("publish");

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

// Only run when "publish" profile is active
if (publish) {
  Nuget.EnsureAuthenticated();
  Nuget.Pack(app);
  Nuget.Push(app, o => o.SkipDuplicate());
}

// CLI usage:
// ando              # Build and test only
// ando -p release   # Build, test, publish, and tag
// ando -p publish      # Build, test, pack, and push to NuGet
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
// ando-doc-snippet-test
// Set the Docker image for the build container
Ando.UseImage("mcr.microsoft.com/dotnet/sdk:9.0");

// Copy artifacts from container to host after build
Ando.CopyArtifactsToHost("dist", "./dist");

// Copy as compressed archive (faster for many small files)
Ando.CopyZippedArtifactsToHost("dist", "./output");  // Creates ./output/artifacts.tar.gz
Ando.CopyZippedArtifactsToHost("dist", "./dist/binaries.tar.gz");  // Specific filename
Ando.CopyZippedArtifactsToHost("dist", "./dist/binaries.zip");     // Zip format

// Run a child build in a subdirectory
Ando.Build(Directory("./website"));

// Run a specific build file
Ando.Build(Directory("./website") / "deploy.csando");

// Child build with Docker-in-Docker enabled
Ando.Build(Directory("./integration-tests"), o => o.WithDind());
```

## Options Reference

### Build Options

| Option | Description |
|--------|-------------|
| `WithVerbosity(string)` | Set output verbosity level. Values: "Quiet" (errors only), "Minimal" (warnings and errors), "Normal" (default), "Detailed" (debug output). |
| `ColdStart(bool)` | Force a fresh container for this build, ignoring any warm container cache. Use when you need a clean environment or are debugging container state issues. |
| `WithDind(bool)` | Enable Docker-in-Docker mode. Required when the child build needs to run Docker commands (e.g., building images, running docker-compose). Mounts the Docker socket into the container. |
| `WithImage(string)` | Override the Docker image for this child build. Use when the child build requires different tools than the parent (e.g., Node.js build in a .NET project). |
