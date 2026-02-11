---
title: Changelog
description: Release history and changelog for ANDO.
---

## 0.9.106

**2026-02-11**

- Add dark mode styling to all remaining UI components and pages

## 0.9.105

**2026-02-11**

- Add dark mode with Light/Dark/System theme toggle to the server UI
- Default notification email to account email when left blank and notifications are enabled
- Improve container path resolution reliability during builds (uses cgroup in addition to HOSTNAME)
- Add validation for project settings updates

## 0.9.104

**2026-02-11**

- Add logo branding to the server login page and layout
- Rename "Ando CI" to "Ando Server" throughout the interface

## 0.9.103

**2026-02-11**

- Add real-time project list refresh via SignalR when builds are queued
- Add manual profile override option in project settings for cases where auto-detection is stale
- Scan branch filter branches (not just default branch) for profile detection
- Add inline secret value update in project settings with password manager suppression
- Add version badge sync to `ando docs` command (automatically updates version badges before running Claude)
- Show app version and attribution in the layout footer

## 0.9.102

**2026-02-11**

- Fix authentication redirects and cookie handling when running behind a reverse proxy

## 0.9.101

**2026-02-11**

- Simplify the project list layout
- Improve `Git.PushTags()` to push only new tags that don't already exist on the remote, avoiding unnecessary pushes and potential conflicts

## 0.9.100

**2026-02-11**

- Enhance build failure emails with build logs and metadata
- Only run post-hooks for `release` and `ship` commands when the command succeeds (exit code 0)
- Improve Ctrl+C handling: first press requests graceful cancellation, second press forces immediate exit

## 0.9.99

**2026-02-11**

- Add deployment status indicators and sorting to the project list page
- Improve release asset resolution for Docker-in-Docker build environments

## 0.9.98

**2026-02-11**

- Fix login redirect to use HTTP 303 See Other, preventing form resubmission on page refresh
- Add global flash messages for login success feedback

## 0.9.97

**2026-02-10**

- Add build log connection status indicator showing SignalR/polling state
- Add SignalR retry with automatic reconnection when WebSockets are blocked
- Detect `GITHUB_TOKEN` requirement for `GitHub.PushImage()` and `Docker.Build()` with `WithPush()` targeting ghcr.io
- Add auth and SignalR diagnostics logging for troubleshooting session issues

## 0.9.96

**2026-02-10**

- Internal improvements

## 0.9.95

**2026-02-10**

- Add `ship` command for running the release workflow without publishing
- Fix remember-me defaulting and extend session idle timeout

## 0.9.94

**2026-02-10**

- Fix login and registration forms resubmitting when refreshing the page

## 0.9.91

**2026-02-10**

- Automatically resolve GitHub App identity via API instead of manual configuration

## 0.9.90

**2026-02-09**

- Persist login sessions so users stay signed in across server restarts
- Default to remember-me login for a smoother authentication experience
- Add session status endpoint for improved client connectivity
- Improve project and script handling in the server

## 0.9.80

**2026-02-07**

- Automatically install Docker CLI in build containers when Docker-in-Docker mode is enabled

## 0.9.79

**2026-02-07**

- Automatically resolve host paths from container bind mounts during builds

## 0.9.78

**2026-02-07**

- Fix build commands running in the wrong directory inside containers

## 0.9.77

**2026-02-07**

- Fix API token authentication not being evaluated correctly

## 0.9.76

**2026-02-07**

- Add personal API token management for programmatic access to the server

## 0.9.75

**2026-02-07**

- Install Ando CLI from NuGet in build containers for improved reliability
- Fix URL generation in the server

## 0.9.74

**2026-02-07**

- Improve email delivery failure logging

## 0.9.73

**2026-02-07**

- Fix file permissions across build scripts and documentation

## 0.9.72

**2026-02-06**

- Fix email sender address validation and improve email service reliability

## 0.9.71

**2026-02-06**

- Fix email service registration for improved reliability
- Normalize file permissions across the project

## 0.9.70

**2026-02-06**

- Redirect to dashboard after registration instead of showing a separate success page

## 0.9.69

**2026-02-06**

- Improve Docker availability checks with specific error messages

## 0.9.68

**2026-02-06**

- Switch email delivery to Resend for improved reliability
- Improve authentication error handling

## 0.9.66

**2026-02-03**

- Add option to bypass rootless Docker validation for TrueNAS deployments

## 0.9.65

**2026-02-03**

- Add ASCII art startup banner with version info to server

## 0.9.64

**2026-02-01**

- Improve documentation for atomic Docker build and push operations

## 0.9.63

**2026-01-31**

- Add exit code to post-hook context for release command

## 0.9.62

**2026-01-31**

- Add dedicated hooks documentation page

## 0.9.61

**2026-01-31**

- Simplify email service configuration in Ando.Server
- Internal improvements

## 0.9.60

**2026-01-30**

- Add proper database migrations for Ando.Server instead of auto-creating schema
- Add TrueNAS deployment documentation for self-hosted installations

## 0.9.59

**2026-01-29**

- Add API documentation links to the LLM reference
- Internal improvements

## 0.9.58

**2026-01-29**

- Add DocFX operations for generating API documentation
- Internal improvements and test coverage enhancements

## 0.9.57

**2026-01-29**

- Simplify Docker operations by consolidating Build and Buildx into a single Build operation
- Add source code links to operation documentation on the website

## 0.9.56

**2026-01-28**

- Improve container registry operations to extract owner from image tags directly

## 0.9.55

**2026-01-28**

- Improve documentation generation to analyze recent commits for better context

## 0.9.54

**2026-01-28**

- Add support for building multi-architecture Docker images

## 0.9.53

**2026-01-28**

- Improve reliability of global tool update after publishing releases

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
