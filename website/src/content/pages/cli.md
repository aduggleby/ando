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
| [`ando verify`](#run-options) | Check build script for errors without executing. |
| [`ando clean`](#clean-options) | Remove artifacts, temp files, and containers. |

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

## Docker-in-Docker (DIND)

ANDO automatically detects when your build script uses operations that require Docker-in-Docker mode (such as `Docker.Build`, `Docker.Push`, `Docker.Install`, `GitHub.PushImage`, or `Playwright.Test`). This detection also scans child builds invoked via `Ando.Build()`. If DIND is needed but not enabled, ANDO prompts you:

- **(Y)es** - Enable DIND for this run only
- **(A)lways** - Enable DIND and save the setting to `ando.config`
- **Esc** - Cancel the build

To skip the prompt, use the `--dind` flag, add `dind: true` to your `ando.config` file, or set the `ANDO_DIND=1` environment variable.

**Child build inheritance:** When a parent build enables DIND, child builds invoked via `Ando.Build()` automatically inherit the DIND setting. The parent passes `ANDO_DIND=1` to both the container environment and the host process environment, so child builds won't prompt again for DIND mode. (The host process environment is needed because `Ando.Build` spawns child `ando` processes on the host.)

## Configuration File

ANDO supports an optional `ando.config` file in the project root for persisting settings.

```yaml
# ando.config
dind: true
readEnv: true
allowClaude: true
```

| Setting | Description |
|---------|-------------|
| `dind` | Enable Docker-in-Docker mode by default. |
| `readEnv` | Automatically load environment files without prompting. |
| `allowClaude` | Allow Claude CLI to run with elevated permissions without prompting. |

## Environment Files

ANDO looks for environment files in the project root: `.env.ando` (preferred) or `.env` (fallback).

**Security check:** If `.env.ando` exists but is not in `.gitignore`, ANDO warns that the file may contain secrets and prompts you:

- **(A)dd to .gitignore** (default) - Automatically appends `.env.ando` to your `.gitignore` file
- **(C)ontinue anyway** - Proceed without adding to `.gitignore`
- **Esc** - Abort the build

**Loading prompt:** When environment variables are found, ANDO prompts:

- **(Y)es** - Load for this run only
- **(n)o** - Skip loading
- **for this (r)un** - Load for this run and all sub-builds
- **(a)lways** - Load and save `readEnv: true` to `ando.config`

Use `--read-env` or set `readEnv: true` in `ando.config` to skip the prompt and load automatically.

## Claude Integration

Several ANDO commands use Claude CLI for AI-powered features:

| Command | Claude Usage |
|---------|-------------|
| `ando commit` | Generates commit messages from diffs |
| `ando bump` | Generates changelog entries and updates changelog files |
| `ando docs` | Reviews code changes and updates documentation |

**Requirements:** Claude CLI must be installed (`npm install -g @anthropic-ai/claude-code`)

**Permissions:** Claude runs with `--dangerously-skip-permissions` to allow file edits without interactive prompts. On first use, ANDO prompts you to confirm:

- **(Y)es** - Allow Claude for this run only
- **(n)o** - Cancel the command
- **(A)lways** - Allow Claude and save `allowClaude: true` to `ando.config`

To skip the prompt, add `allowClaude: true` to your `ando.config` file.

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
1. Gets commits since last git tag (or recent commits if no tag)
2. Analyzes each commit to understand what changed
3. Finds and updates relevant documentation files
4. Changes are left uncommitted for you to review

### Files Searched and Updated

Claude searches for and updates these file types:

| Pattern | Location | Description |
|---------|----------|-------------|
| `*.md` | Anywhere | Markdown documentation files |
| `*.astro` | `website/` | Astro page components |
| `*.js` | `website/` | JavaScript data files (operations, providers) |
| `llms.txt` | `public/` | LLM-friendly documentation (must stay in sync) |

### Files Skipped

| File | Reason |
|------|--------|
| `CHANGELOG.md` | Handled separately by `ando bump` |
| Internal refactoring | Only user-facing changes need docs |

### What Triggers Updates

Claude looks for commits that affect:

- **New features** - Operations, commands, or capabilities
- **Changed behavior** - Modified functionality that affects users
- **New options** - Parameters, flags, or configuration settings
- **Examples** - New use cases or updated patterns
- **CLI help** - Command descriptions or usage

### Example Output

```
$ ando docs
Analyzing 5 commit(s) since v0.9.58...
Claude is reviewing documentation...

Reading website/src/data/operations.js...
Updating Docker.Build options documentation...
Reading website/public/llms.txt...
Adding new --platform flag to llms.txt...

Documentation updated. Review changes and commit when ready.
```

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
2. **Commit** - Commit uncommitted changes (uses `ando commit`) - *skipped if no uncommitted changes*
3. **Docs** - Update documentation (uses `ando docs`)
4. **Bump** - Bump version across all projects (uses `ando bump`) - *skipped if no changes since last tag*
5. **Push** - Push to remote repository - *skipped if no remote tracking*
6. **Publish** - Run `ando run -p publish --dind --read-env`

Steps are contextually enabled/disabled based on repository state.

## Hooks

ANDO supports pre-build and post-build hooks that run before and after CLI commands. Hooks are `.csando` scripts that execute on the host machine.

| Hook | When it runs |
|------|--------------|
| `ando-pre.csando` | Before ANY command |
| `ando-pre-{cmd}.csando` | Before specific command |
| `ando-post-{cmd}.csando` | After specific command |
| `ando-post.csando` | After ANY command |

**Quick example:**
```csharp
// scripts/ando-pre-bump.csando
var result = await Shell.RunAsync("dotnet", "test", "--no-build");
if (result.ExitCode != 0) throw new Exception("Tests failed");
```

👉 **[See full Hooks documentation](/hooks)** for file locations, available APIs, environment variables, and more examples.

## Clean Options

| Flag | Description |
|------|-------------|
| `--artifacts` | Remove the artifacts directory. |
| `--temp` | Remove temp directory. |
| `--container` | Remove the project's warm container (clears package caches). |
| `--all` | Remove all of the above. |

## Project Files

ANDO uses and creates various files in your project directory:

### Environment Files

| File | Description |
|------|-------------|
| `.env.ando` | **Preferred** - Project-specific environment variables. Takes priority over `.env`. |
| `.env` | **Fallback** - Standard environment file, used if `.env.ando` doesn't exist. |

ANDO prompts before loading environment files (unless `--read-env` flag or `readEnv: true` in config).

### Configuration Files

| File | Description |
|------|-------------|
| `ando.config` | YAML configuration file for persistent settings (`dind`, `readEnv`). |
| `build.csando` | Build script (C# with Roslyn scripting). |

### Generated Files & Directories

| Path | Description |
|------|-------------|
| `build.csando.log` | Plain-text log of the last build run. Overwrites on each run. |
| `.ando/cache/` | Package caches persist inside warm containers (not on host). |
| `.ando/tmp/` | Temporary files used during builds. |
| `artifacts/` | Default output directory for build artifacts. |

### Hook Scripts

| File | Description |
|------|-------------|
| `ando-pre.csando` | Runs before any command. |
| `ando-pre-{cmd}.csando` | Runs before a specific command. |
| `ando-post-{cmd}.csando` | Runs after a specific command. |
| `ando-post.csando` | Runs after any command. |

Hook scripts are searched in `./scripts/` first, then `./`.

## Logging

ANDO writes a plain-text log to `build.csando.log` in the project root. This file:

- Is **overwritten** on each build run (not appended)
- Contains the same output as the console (without ANSI color codes)
- Includes timestamps, step names, and command output
- Is useful for debugging failed builds or reviewing build history

## Suggested .gitignore

Add these patterns to your `.gitignore` to exclude ANDO-generated files:

```gitignore
# ANDO build system
.env.ando
build.csando.log
.ando/
artifacts/
```

**Explanation:**

| Pattern | Why ignore |
|---------|------------|
| `.env.ando` | Contains secrets (API keys, tokens, passwords). ANDO warns if not gitignored. |
| `build.csando.log` | Build output log, regenerated on each run. |
| `.ando/` | Cache directories (NuGet, npm) and temp files. Large and machine-specific. |
| `artifacts/` | Build outputs. Should be regenerated, not committed. |

**Optional:**

```gitignore
# Optional: exclude config if it contains machine-specific settings
# ando.config
```

Most teams commit `ando.config` since it contains project-wide settings like `dind: true`.
