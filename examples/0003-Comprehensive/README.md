# 0003-Comprehensive Example

This example demonstrates a comprehensive Dotnet workflow using ANDO.

## Features Demonstrated

- **Multiple projects**: WebApp and WebApp.Tests
- **BuildPath syntax**: `Root / "dist"` for path combination
- **Context paths**: `Context.Paths.Artifacts` for output
- **Context variables**: `Context.Vars["BUILD_NUMBER"]` for custom data
- **All Dotnet operations**:
  - `Dotnet.Restore()` - Restore NuGet packages
  - `Dotnet.Build()` - Build with configuration
  - `Dotnet.Test()` - Run unit tests
  - `Dotnet.Publish()` - Create deployable artifacts

## Publish Options

The publish step demonstrates all available options:
- `.Output()` - Output directory
- `.WithConfiguration()` - Build configuration
- `.WithRuntime()` - Target runtime identifier
- `.AsSelfContained()` - Include .NET runtime
- `.AsSingleFile()` - Create single executable
- `.SkipRestore()` - Skip restore (already done)

## Running

```bash
ando
```
