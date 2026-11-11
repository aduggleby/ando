---
title: Globals
description: Bare globals available in every build.csando script, such as Root, Temp, Env, Directory, DefineProfile and Log.
provider: Globals
---

## Overview

These names are available in every `build.csando` script without a prefix or import. They are **not** members of the `Ando` object: write `Env("API_KEY")`, not `Ando.Env("API_KEY")`. The operations of the `Ando` object (`Ando.UseImage`, `Ando.Block` and so on) are on the [Ando](/providers/ando) page.

| Global            | Kind              | Description                                                        |
| ----------------- | ----------------- | ------------------------------------------------------------------ |
| `Root`            | `BuildPath`       | Project root (where `build.csando` is). Supports the `/` operator. |
| `Temp`            | `BuildPath`       | Temporary directory (`.ando/tmp`). Supports the `/` operator.      |
| `Directory()`     | function          | Creates a `DirectoryRef`.                                          |
| `Env()`           | function          | Reads an environment variable.                                     |
| `DefineProfile()` | function          | Defines a build profile and returns a `Profile`.                   |
| `Log`             | operations object | `Log.Info`, `Log.Warning`, `Log.Error`, `Log.Debug`.               |

The other globals are the operation objects, one per provider: `Ando`, `AppService`, `Archive`, `Azure`, `Bicep`, `Bun`, `Cloudflare`, `Docfx`, `Docker`, `Dotnet`, `Ef`, `Files`, `Functions`, `Git`, `GitHub`, `Http`, `Node`, `Npm`, `Nuget`, `Playwright` and `Shell`.

Globals are evaluated while the script loads. `Env()`, `Directory()`, `DefineProfile()`, `Root` and `Temp` do not register steps. `Log.*` calls are steps: they print when their turn in the step order comes.

## Examples

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
  Nuget.Push(app); // skips packages that already exist by default
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
