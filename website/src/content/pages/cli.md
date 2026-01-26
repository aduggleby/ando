---
title: ANDO CLI Reference
description: Command-line interface reference for the ANDO build tool.
toc: true
---

## Commands

| Command | Description |
|---------|-------------|
| [`ando`](#run-options) | Run the build script in a Docker container. |
| [`ando commit`](#commit-command) | Commit all changes with AI-generated message. |
| [`ando bump`](#bump-command) | Bump version across all projects. |
| [`ando docs`](#docs-command) | Update documentation using Claude. |
| [`ando release`](#release-command) | Interactive release workflow. |
| [`ando verify`](#examples) | Check build script for errors without executing. |
| [`ando clean`](#clean-options) | Remove artifacts, temp files, and containers. |
| [`ando help`](#examples) | Show available commands and options. |

## Run Options

| Flag | Description |
|------|-------------|
| `-f, --file <file>` | Use a specific build file instead of build.csando. |
| `-p, --profile <profiles>` | Activate build profiles (comma-separated). |
| `--read-env` | Load environment file without prompting. |
| `--verbosity <level>` | Set output verbosity (quiet\|minimal\|normal\|detailed). |
| `--no-color` | Disable colored output. |
| `--cold` | Always create a fresh container. |
| `--image <image>` | Use a custom Docker image. |
| `--dind` | Mount Docker socket for Docker-in-Docker builds. |

## Commit Command

Commits all changes with an AI-generated message using Claude.

```
$ ando commit
Analyzing changes...

  src/Ando/Operations/GitHubOperations.cs
  website/src/content/providers/github.md

Generated commit message:
────────────────────────────────────────
docs: add options reference to GitHub provider
────────────────────────────────────────

Commit with this message? [Y/n] y
```

**Requirements:** Claude CLI must be installed (`npm install -g @anthropic-ai/claude-code`)

## Bump Command

Bumps the version of all projects detected in `build.csando`.

```bash
ando bump [patch|minor|major]
```

**What it does:**
1. Detects projects from `build.csando` (Dotnet.Project, Npm directories)
2. Validates all versions match (prompts if mismatched)
3. Updates all project files (.csproj, package.json)
4. Generates changelog entry using Claude from commit history
5. Updates CHANGELOG.md and version badges
6. Commits the changes

**Supported projects:**
- `.csproj` files via `Dotnet.Project("path")`
- `package.json` in directories used with `Npm.*` operations

## Docs Command

Uses Claude to review code changes and update documentation.

```bash
ando docs
```

**What it does:**
1. Gets diff since last git tag (or all changes if no tag)
2. Sends changes to Claude with instructions to update relevant docs
3. Claude updates markdown files, website pages, and examples as needed
4. Changes are left uncommitted for you to review

**Requirements:** Claude CLI must be installed (`npm install -g @anthropic-ai/claude-code`)

## Release Command

Interactive release workflow that orchestrates: commit, documentation, bump, push, and publish.

```bash
ando release           # Interactive checklist
ando release --all     # Skip checklist, run all steps
ando release --dry-run # Preview without executing
ando release --minor   # Specify bump type (default: patch)
```

**Steps:**
1. **Build Verification** - Runs `ando run --read-env` first
2. **Commit** - Commit uncommitted changes (uses `ando commit`)
3. **Docs** - Update documentation (uses `ando docs`)
4. **Bump** - Bump version across all projects (uses `ando bump`)
5. **Push** - Push to remote repository
6. **Publish** - Run `ando run -p push --dind --read-env`

Steps are contextually enabled/disabled based on repository state.

## Hooks

Hooks are `.csando` scripts that run before/after CLI commands.

| Hook | When it runs |
|------|--------------|
| `ando-pre.csando` | Before ANY command |
| `ando-pre-{cmd}.csando` | Before specific command |
| `ando-post-{cmd}.csando` | After specific command |
| `ando-post.csando` | After ANY command |

**Search locations:** `./scripts/` then `./`

**Available APIs:** `Log.*`, `Env(name)`, `Root`, `Directory(path)`, `Shell.RunAsync(cmd, args)`

**Environment variables:**

| Variable | Available in |
|----------|--------------|
| `ANDO_COMMAND` | All hooks |
| `ANDO_OLD_VERSION` | bump hooks |
| `ANDO_NEW_VERSION` | post-bump |
| `ANDO_BUMP_TYPE` | bump hooks |

**Example:**
```csharp
// scripts/ando-pre-bump.csando
var result = await Shell.RunAsync("dotnet", "test", "--no-build");
if (result.ExitCode != 0) throw new Exception("Tests failed");
```

## Clean Options

| Flag | Description |
|------|-------------|
| `--artifacts` | Remove the artifacts directory. |
| `--temp` | Remove temp directory. |
| `--cache` | Remove NuGet and npm caches. |
| `--container` | Remove the project's warm container. |
| `--all` | Remove all of the above. |

## Examples

```bash
# Run the build
ando

# Run with a profile
ando -p release

# Commit with AI message
ando commit

# Bump version
ando bump minor

# Update documentation with Claude
ando docs

# Interactive release
ando release

# Force fresh container
ando run --cold

# Custom Docker image
ando run --image mcr.microsoft.com/dotnet/sdk:9.0

# Clean everything
ando clean --all
```
