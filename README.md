# ANDO

A typed C# build system using Roslyn scripting.

## Overview

ANDO allows you to write build scripts in C# with full IDE support, type safety, and IntelliSense. Scripts are executed using Roslyn C# scripting in isolated Docker containers for reproducible builds.

## Installation

### From Source

```bash
git clone https://github.com/yourname/ando.git
cd ando
dotnet publish -c Release -r linux-x64 --self-contained -o dist
```

### Prerequisites

- .NET 9.0 SDK (for building)
- Docker (for containerized execution)

## Quick Start

1. Create a `build.ando` file in your project root:

```csharp
var project = Project.From("./src/MyApp/MyApp.csproj");
var output = Root / "dist";

// Use Release configuration
Options.UseConfiguration(Configuration.Release);

Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(project);
Dotnet.Publish(project, o => o
    .Output(output)
    .WithConfiguration(Configuration.Release)
    .AsSelfContained());
```

2. Run the build:

```bash
ando run
```

## Features

### Type-Safe Build Scripts

Build scripts are real C# code with full type checking:

```csharp
var webProject = Project.From("./src/Web/Web.csproj");
var apiProject = Project.From("./src/Api/Api.csproj");

// Build both projects
Dotnet.Build(webProject, o => o.Configuration = Configuration.Release);
Dotnet.Build(apiProject, o => o.Configuration = Configuration.Release);
```

### Build Options

Configure build options with a fluent API:

```csharp
// Set configuration (Debug or Release)
Options.UseConfiguration(Configuration.Release);

// Root is a shorthand for Context.Paths.Root
var distPath = Root / "dist";
var artifactsPath = Root / "artifacts";
```

### Context and Variables

Access build context and define variables:

```csharp
// Access paths
var outputDir = Context.Paths.Artifacts / "output";

// Store/retrieve variables
Context.Vars["version"] = "1.0.0";

// Access environment variables
var apiKey = Context.Vars.Env("API_KEY");
```

### Docker Isolation

By default, builds run in isolated Docker containers for reproducibility:

```bash
# Use a custom Docker image
ando run build --image mcr.microsoft.com/dotnet/sdk:8.0

# Force fresh container
ando run build --cold

# Enable Docker-in-Docker
ando run build --dind
```

### Local Execution

For quick iterations, skip Docker:

```bash
ando run build --local
```

## Commands

| Command | Description |
|---------|-------------|
| `ando run [workflow]` | Run the build workflow (default) |
| `ando clean` | Remove artifacts, temp files, and containers |
| `ando version` | Print version info |
| `ando help` | Show help |

### Run Options

| Option | Description |
|--------|-------------|
| `--verbosity <level>` | Set output verbosity (quiet/minimal/normal/detailed) |
| `--no-color` | Disable colored output |
| `--local` | Run without Docker (use host directly) |
| `--cold` | Always create fresh container |
| `--image <image>` | Use custom Docker image |
| `--dind` | Mount Docker socket for Docker-in-Docker |

### Clean Options

| Option | Description |
|--------|-------------|
| `--artifacts` | Remove artifacts directory |
| `--temp` | Remove temp directory |
| `--cache` | Remove NuGet and npm caches |
| `--container` | Remove the project's warm container |
| `--all` | Remove all of the above |

## API Reference

### Project

```csharp
var project = Project.From("./path/to/Project.csproj");

project.Name      // "Project"
project.Path      // "./path/to/Project.csproj"
project.Directory // "./path/to"
```

### Dotnet Operations

```csharp
Dotnet.Restore(project);
Dotnet.Restore(project, o => o.NoCache = true);

Dotnet.Build(project);
Dotnet.Build(project, o => o.Configuration = Configuration.Release);

Dotnet.Test(project);
Dotnet.Test(project, o => {
    o.Configuration = Configuration.Release;
    o.Filter = "Category=Unit";
});

Dotnet.Publish(project, o => o
    .WithConfiguration(Configuration.Release)
    .WithRuntime("linux-x64")
    .WithOutput("/output/path")
    .AsSelfContained());
```

### Entity Framework Operations

```csharp
var dbContext = Ef.DbContextFrom(project, "MyDbContext");

Ef.DatabaseUpdate(dbContext);
Ef.AddMigration(dbContext, "AddUsersTable");
Ef.Script(dbContext, Context.Paths.Artifacts / "migration.sql");
```

### Context

```csharp
// Paths
Context.Paths.Root       // Project root directory
Context.Paths.Src        // ./src
Context.Paths.Artifacts  // ./artifacts
Context.Paths.Temp       // ./.ando/tmp

// Variables
Context.Vars["key"] = "value";
var value = Context.Vars["key"];
Context.Vars.Has("key");         // true/false
Context.Vars.Env("ENV_VAR");     // Get environment variable
Context.Vars.EnvRequired("VAR"); // Throws if not set
```

## Examples

See the [examples](./examples) directory:

- **0001-Simple**: Hello World - basic project build and publish
- **0002-Library**: Multi-project - console app with library dependency

### Adding a New Example

To add a new example project:

1. Create a new directory in `examples/` following the naming convention `NNNN-Name`:
   ```bash
   mkdir -p examples/0003-MyExample/src
   ```

2. Create the required files with this structure:
   ```
   examples/0003-MyExample/
   ├── build.ando       # Build script (required)
   ├── README.md        # Example documentation
   ├── test.sh          # Test script for manual verification
   └── src/             # Source code
       └── MyApp.csproj
   ```

3. Create `build.ando` with the standard pattern:
   ```csharp
   // 0003-MyExample: Brief description
   // Longer description of what this example demonstrates

   var App = Project.From("./src/MyApp.csproj");

   // Output directory
   var output = Root / "dist";

   // Use Release configuration
   Options.UseConfiguration(Configuration.Release);

   Dotnet.Restore(App);
   Dotnet.Build(App);
   Dotnet.Publish(App, o => o
       .Output(output)
       .WithConfiguration(Configuration.Release));
   ```

4. Create `test.sh` for manual verification:
   ```bash
   #!/bin/bash
   set -e
   cd "$(dirname "$0")"
   ando run --local
   echo "Build succeeded!"
   ```

5. The E2E test suite will automatically discover and test your new example.

## Development

### Building

ANDO builds itself using its own build system. Use the bootstrap scripts:

```bash
# Linux/macOS
./build.sh

# Windows
.\build.ps1
```

This runs the `build.ando` script which:
- Restores dependencies
- Builds the project
- Runs tests
- Publishes self-contained executables to `dist/`

For development iteration:

```bash
dotnet build
```

### Testing

```bash
# Run all tests
dotnet test

# Run only unit tests
dotnet test --filter "Category=Unit"

# Run integration tests (requires Docker)
dotnet test --filter "Category=Integration"
```

### Running Examples

```bash
cd examples/0001-Simple
ando run build --local
./dist/HelloWorld
```

## Architecture

ANDO uses:

- **Roslyn C# Scripting** for parsing and executing build scripts
- **Docker** for isolated, reproducible build environments
- **Step Registration Pattern** where operations register steps that are executed by the workflow runner

See [ARCHITECTURE.md](./ARCHITECTURE.md) for details.

## License

MIT License
