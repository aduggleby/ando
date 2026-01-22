---
title: Changelog
description: Release history and changelog for ANDO.
---

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
