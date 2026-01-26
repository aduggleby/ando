# ANDO

ANDO allows you to write build and deployment scripts in plain C# with full IDE support, type safety, and IntelliSense. Scripts are executed using Roslyn C# scripting in isolated Docker containers for reproducible builds.

> From the Latin "andō" - to set in motion.

[![Website](https://img.shields.io/badge/docs-andobuild.com-blue)](https://andobuild.com)
[![GitHub](https://img.shields.io/github/stars/aduggleby/ando?style=social)](https://github.com/aduggleby/ando)

- **Plain C#** - No custom syntax or DSLs. If you know C#, you know ANDO.
- **Type-safe** - Compile-time errors catch mistakes before they run.
- **Containerized** - Every build runs in Docker for isolation and reproducibility.
- **Composable** - Run nested builds with `Ando.Build()` for modular workflows.

## Projects

| Project | Description |
|---------|-------------|
| **Ando** (`src/Ando`) | CLI tool that parses and executes `build.csando` scripts. Installed as a dotnet tool. |
| **Ando.Server** (`src/Ando.Server`) | CI/CD server with GitHub webhook integration, real-time build logs via SignalR, and artifact management. |
| **Website** (`website/`) | Documentation site built with Astro. Deployed at [andobuild.com](https://andobuild.com). |

## Documentation

Full documentation is available at **[andobuild.com](https://andobuild.com)**.

## Developer Guide

### Prerequisites

- **.NET 9 SDK** - https://dotnet.microsoft.com/download
- **Node.js 20+** - For the website and CI Server frontend
- **Docker** - For running SQL Server and building containers

### Getting Started

```bash
git clone https://github.com/aduggleby/ando.git
cd ando
```

### CLI Development

```bash
# Build the CLI
dotnet build src/Ando

# Run CLI locally
dotnet run --project src/Ando -- build

# Install as global tool (from repo root)
dotnet pack src/Ando -o ./nupkg
dotnet tool install --global --add-source ./nupkg Ando

# Run tests
dotnet test tests/Ando.Tests
```

#### Version Management

Bump versions in both CLI and Server projects using the built-in bump command:

```bash
# Bump patch version (1.0.0 → 1.0.1)
ando bump

# Bump minor version (1.0.5 → 1.1.0)
ando bump minor

# Bump major version (1.5.3 → 2.0.0)
ando bump major
```

#### Development Scripts

```bash
# Run ando from source (passes all arguments)
./ando-dev [args...]

# Example: run build with push profile from source
./ando-dev -p push
```

#### Release Workflow

Use `ando release` for the full release workflow:

```bash
# Interactive release (shows checklist)
ando release

# Run all steps without prompts
ando release --all

# Preview what would happen
ando release --dry-run

# Specify version bump type (default: patch)
ando release --minor
ando release --major
```

**What `ando release` does:**

1. **Build Verification** - Runs `ando run --read-env` to verify the build passes before proceeding
2. **Interactive Checklist** - Presents steps to select:
   - **Commit** - Commit any uncommitted changes (skipped if working tree is clean)
   - **Bump** - Bump version in all project files and update CHANGELOG
   - **Docs** - Update documentation using Claude to analyze changes
   - **Push** - Push commits to remote (skipped if no remote tracking)
   - **Publish** - Run `ando run -p push --dind --read-env` for publishing

**Options:**

| Option | Description |
|--------|-------------|
| `--all` | Skip checklist and run all applicable steps |
| `--dry-run` | Preview what would happen without executing |
| `--patch` | Bump patch version (1.0.0 → 1.0.1) - default |
| `--minor` | Bump minor version (1.0.0 → 1.1.0) |
| `--major` | Bump major version (1.0.0 → 2.0.0) |

**Manual release (without `ando release`):**

```bash
# Build and test only
ando

# Push to NuGet.org, build Docker image, tag git, and deploy docs
ando -p push
```

### CI Server Development

The CI Server requires SQL Server and a GitHub App for full functionality.

```bash
# Start SQL Server via Docker
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=YourPassword123!" \
  -p 1433:1433 --name ando-sql -d mcr.microsoft.com/mssql/server:2022-latest

# Install frontend dependencies
cd src/Ando.Server/ClientApp && npm install && cd ../../..

# Run the server (database created automatically)
dotnet run --project src/Ando.Server
```

For GitHub integration, create a GitHub App and set environment variables:

```bash
export GitHub__AppId=123456
export GitHub__ClientId=Iv1.xxxxxxxx
export GitHub__ClientSecret=xxxxxxxx
export GitHub__WebhookSecret=$(openssl rand -hex 20)
export Encryption__Key=$(openssl rand -base64 32)
```

See [src/Ando.Server/README.md](src/Ando.Server/README.md) for full deployment documentation.

### Website Development

```bash
cd website
npm install
npm run dev      # Start dev server
npm run build    # Build for production
```

### Running Tests

```bash
# All .NET tests
dotnet test

# Specific categories
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"

# E2E tests (Playwright)
cd tests/Ando.Server.E2E && npm install && npm test
```

### Project Structure

```
src/
├── Ando/              # CLI tool
│   ├── Cli/           # Command-line interface
│   ├── Operations/    # Dotnet, Docker, Git, etc.
│   ├── Scripting/     # Roslyn script execution
│   └── Workflow/      # Build step orchestration
├── Ando.Server/       # CI Server
│   ├── ClientApp/     # React frontend
│   ├── Controllers/   # MVC controllers
│   ├── GitHub/        # GitHub App integration
│   └── Services/      # Build orchestration
tests/
├── Ando.Tests/        # CLI unit/integration tests
├── Ando.Server.Tests/ # Server tests
└── Ando.Server.E2E/   # Playwright E2E tests
scripts/               # Ando hook scripts (pre/post command hooks)
website/               # Astro documentation site
```

## License

[No'Saasy License](./LICENSE) (based on [O'Sassy](https://osaasy.dev/))
