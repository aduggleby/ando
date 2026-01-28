---
title: Changelog
description: Release history and changelog for ANDO.
---

## 0.9.52

**2026-01-28**

- Add comprehensive tests for CLI commands

## 0.9.51

**2026-01-28**

- Improve build performance by persisting caches inside containers instead of mounting from host

## 0.9.50

**2026-01-28**

- Fix validation for unknown CLI commands
- Improve post-release tool update to use specific version

## 0.9.49

**2026-01-28**

- Add AI-powered automatic changelog updates during version bumps
- Add permission checks for AI-assisted operations

## 0.9.48

**2026-01-28**

- Skip version bump during release when there are no changes since the last tag

## 0.9.47

**2026-01-28**

- Fix error output capture when commands fail, showing helpful error messages instead of empty output
- Improve timeout handling to gracefully stop long-running commands

## 0.9.46

**2026-01-26**

- Add automatic DIND (Docker-in-Docker) mode inheritance so child builds no longer prompt separately when the parent build already enabled DIND

## 0.9.45

**2026-01-26**

- Add automatic Docker-in-Docker mode inheritance for nested builds, so child builds no longer prompt for DIND confirmation when the parent build already enabled it

## 0.9.44

**2026-01-26**

- Add recursive scanning of child builds when detecting Docker-in-Docker requirements
- Add profile support for child builds, allowing different build configurations
- Add `readEnv` config option to control environment variable loading and new hook recipes

## 0.9.43

**2026-01-26**

- Improve hook configuration to support path operations on the root directory

## 0.9.42

**2026-01-26**

- Internal improvements to post-release script cleanup handling

## 0.9.41

**2026-01-26**

- Add automatic detection when Docker-in-Docker mode is needed, with an interactive prompt to enable it
- Add automatic update of the global tool after publishing a new release

## 0.9.40

**2026-01-26**

- Add option to automatically add `.env.ando` to `.gitignore` when initializing a project

## 0.9.39

**2026-01-26**

- Version bump

## 0.9.38

**2026-01-26**

- Add new `ando docs` command to open documentation in the browser
- Update website tagline and improve documentation

## 0.9.37

**2026-01-26**

- Add real-time output streaming for shell operations in build scripts
- Add new `ando docs` command for AI-assisted documentation updates
- Fix Docker-in-Docker container recreation and build verification

## 0.9.36

**2026-01-26**

- Add documentation for the release command
- Fix incorrect GitHub authentication instructions

## 0.9.35

**2026-01-24**

- Add automatic confirmation option for version bumping to enable non-interactive workflows

## 0.9.34

**2026-01-23**

- Add GitHub link badge to the documentation website landing page

## 0.9.33

**2026-01-23**

- Add AI-powered changelog generation that creates user-friendly release notes from commit messages

## 0.9.32

**2026-01-23**

- feat(bump): auto-generate changelog from git commits since last tag

## 0.9.31

**2026-01-23**

- Version bump

## 0.9.30

**2026-01-23**

- Version bump

## 0.9.29

**2026-01-23**

- Version bump

## 0.9.28

**2026-01-23**

- Version bump

## Installation

Install or update ANDO using the .NET CLI:

```bash
# Install
dotnet tool install -g ando

# Update to latest
dotnet tool update -g ando

# Check version
ando --version
```

## 0.9.27

**2026-01-23**

**Improvements:**

- Improved reliability of GitHub OAuth scope checking
  - Fixed environment variable handling when checking keyring token scopes
  - Uses bash subprocess for more reliable token isolation during scope verification

---

## 0.9.26

**2026-01-23**

**Improvements:**

- Added OAuth scope validation for `GitHub.PushImage()` operations
  - Now checks for `write:packages` scope before attempting to push to ghcr.io
  - Provides clear error message with re-authentication instructions if scope is missing
  - Run `gh auth login --scopes write:packages` to fix scope issues

---

## 0.9.25

**2026-01-23**

**Maintenance:**

- Improved version bump script with GitHub authentication check
- No user-facing changes in this release

---

## 0.9.24

**2026-01-23**

**Maintenance:**

- Version bump for release infrastructure improvements
- No user-facing changes in this release

---

## 0.9.23

**2026-01-22**

**New Features:**

- Enhanced `GitHub.CreateRelease()` with optional file uploads
  - Use `WithFiles()` to attach release assets (e.g., binaries, archives)
  - Supports `path#name` syntax to rename files (e.g., `"dist/app#app-linux-x64"`)
  - Files are uploaded as release assets alongside the release creation

**Example:**

```csharp
GitHub.CreateRelease(o => o
    .WithTag("v1.0.0")
    .WithGeneratedNotes()
    .WithFiles(
        "dist/linux-x64/app#app-linux-x64",
        "dist/win-x64/app.exe#app-win-x64.exe"
    ));
```

---

## 0.9.22

**2026-01-22**

**Maintenance:**

- Reverted SDK version and target framework to .NET 9 (9.0.200 SDK, net9.0 target framework)
  - An attempted upgrade to .NET 10 was reverted to maintain compatibility and stability
  - The project continues to use .NET 9 exclusively as per the documented requirements

---

## 0.9.21

**2026-01-22**

**New Features:**

- Added `--version` / `-v` CLI command to display the installed ANDO version
  - Usage: `ando --version` or `ando -v`

**Improvements:**

- Enhanced `Playwright.Install()` to automatically install system dependencies
  - Now runs `npx playwright install --with-deps` instead of just `npx playwright install`
  - Automatically installs required system packages (libgtk, libasound, etc.) on Linux
  - Improves reliability when running E2E tests in Docker containers
- Improved Docker-in-Docker (`--dind`) support on Linux
  - Added `host.docker.internal` hostname mapping for containers
  - Containers can now reach services on the host via `host.docker.internal`
  - Useful for E2E tests that need to connect to locally running servers
- Updated all examples to include `Ando.CopyArtifactsToHost()` for copying build outputs to host

---

## 0.9.20

**2026-01-22**

**New Features:**

- Added `Docker.IsAvailable()` method to check if Docker CLI and daemon are accessible
  - Returns `true` if Docker is available, `false` otherwise
  - Executes immediately (not registered as a step) for conditional logic in build scripts

**Improvements:**

- Enhanced E2E testing support with Docker-in-Docker mode
  - Build scripts can now detect Docker availability and conditionally run E2E tests
  - Updated main build script to use `Docker.IsAvailable()` for cleaner E2E test gating
  - Improved documentation for running E2E tests with `ando --dind` flag

**Documentation:**

- Updated Playwright E2E testing recipe with Docker-in-Docker best practices
- Clarified E2E test requirements in build script comments

---

## 0.9.19

**2026-01-22**

**New Features:**

- Added Playwright E2E testing to the main build script with automatic local/server detection
  - Tests run automatically on local builds where Docker is available
  - Tests are skipped on Ando.Server (nested Docker) by default
  - Use `-p e2e` profile to force E2E tests in any environment

**Documentation:**

- Added new Recipes section to the documentation website
  - New recipe: "Running Playwright E2E Tests" with automatic local/server detection
- Updated `llms.txt` with links to new recipe documentation
- Added `.NET Version` section to CLAUDE.md documenting the .NET 9 requirement

---

## 0.9.18

**2026-01-22**

**Improvements:**

- Added support for Resend-compatible email providers with configurable base URL in Ando.Server
  - Configure `Email__BaseUrl` to use alternative email providers like Loops or self-hosted Resend instances
  - Default base URL remains `https://api.resend.com` for standard Resend usage

---

## 0.9.17

**2026-01-22**

**Improvements:**

- Enhanced `ProjectRef` to read version directly from `.csproj` files
  - Access project version via `project.Version` property in build scripts
  - Example: `Log.Info($"Building {project.Name} version {project.Version}");`
  - Version is lazily loaded and cached for performance
  - Returns "0.0.0" if no Version element is found

---

## 0.9.16

**2026-01-22**

**Improvements:**

- Expanded tilde (`~`) to home directory for `GITHUB_PEM_PATH` in `server-install.sh`

---

## 0.9.15

**2026-01-22**

**Improvements:**

- Refactored GitHub PEM handling in `server-install.sh` for improved flexibility
  - Added option to paste PEM content directly (recommended for `curl | bash` installations)
  - Added option to use a file already uploaded to the server
  - Retained local file path option for compatibility

---

## 0.9.14

**2026-01-22**

**Bug Fixes:**

- Fixed input handling in `server-install.sh` to read from `/dev/tty` for better compatibility with piped installations

---

## 0.9.13

**2026-01-22**

**New Features:**

- Added `Docker.Install()` to install Docker CLI in the container for Docker-in-Docker builds

**Improvements:**

- Git commands now always run on the host (not in the container), fixing issues with git operations in containerized builds
- Added `WithSkipIfExists()` option to `Git.Tag()` to gracefully handle existing tags instead of failing
- Improved `Nuget.Push()` duplicate handling - now properly detects and reports when a package version already exists
- Updated GitHub CLI token extraction to support newer `gh` CLI versions

---

## 0.9.12

**2026-01-21**

**Maintenance:**

- Removed `.env.ando` files from git tracking to prevent accidental commits of local environment configuration
- Added `.env.ando` pattern to `.gitignore`

---

## 0.9.11

**2026-01-21**

**Maintenance:**

- Fixed file permissions on multiple source and documentation files

---

## 0.9.5

**2026-01-17**

**Build Profiles:**

- New `DefineProfile()` for conditional build steps
- Activate via CLI: `ando -p release` or `ando -p push,release`
- Validates profiles before execution - typos are caught early
- Profiles are passed to nested builds automatically

**New Providers:**

- **Git** - `Tag()`, `Push()`, `PushTags()`, `Add()`, `Commit()`
- **GitHub** - `CreatePr()`, `CreateRelease()`, `PushImage()` (ghcr.io)
- **Docker** - `Build()` with support for build args and platforms

**GitHub Authentication:**

- Uses `GITHUB_TOKEN` env var (CI-friendly)
- Falls back to extracting token from `gh auth login` config
- Token passed to containers automatically

---

## 0.9.4

**2026-01-17**

**Auto-Install SDK/Runtime:**

- **Dotnet** operations now automatically install .NET SDK if not present
- **Npm** operations now automatically install Node.js if not present
- Version is fetched dynamically from official APIs (.NET releases, Node.js releases)
- Cached per-build to avoid repeated API calls
- `Dotnet.SdkInstall()` and `Node.Install()` disable auto-install for explicit version control

---

## 0.9.3

**2026-01-16**

First public release.

**Providers:**

- **Ando** - Core operations (Log, Artifacts, Context)
- **Dotnet** - Build, test, publish .NET projects (includes SdkInstall)
- **Ef** - Entity Framework Core migrations
- **Npm** - npm install, ci, run, build, test
- **Node** - Install Node.js in containers
- **NuGet** - Pack and push NuGet packages
- **Azure** - Azure CLI authentication and subscription management
- **Bicep** - Deploy Azure infrastructure with Bicep templates
- **AppService** - Deploy to Azure App Service with slot swapping
- **Functions** - Deploy to Azure Functions
- **Cloudflare** - Deploy to Cloudflare Pages
