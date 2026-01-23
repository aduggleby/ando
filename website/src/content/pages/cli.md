---
title: ANDO CLI Reference
description: Command-line interface reference for the ANDO build tool.
toc: true
---

## Commands

The CLI provides a small, focused surface area.

| Command | Description |
|---------|-------------|
| `ando` | Run the build script (same as 'ando run'). |
| `ando run` | Run the build script in a Docker container. |
| `ando commit` | Commit all changes with AI-generated message using Claude. |
| `ando bump` | Bump version across all projects in build.csando. |
| `ando release` | Interactive release workflow (commit, docs, bump, push, publish). |
| `ando verify` | Check build script for errors without executing. |
| `ando clean` | Remove artifacts, temp files, and containers. |
| `ando help` | Show available commands and options. |

## Run Options

Options for the `ando run` command.

| Flag | Description |
|------|-------------|
| `-f, --file <file>` | Use a specific build file instead of build.csando. |
| `-p, --profile <profiles>` | Activate build profiles (comma-separated). |
| `--read-env` | Load environment file without prompting (also applies to sub-builds). Checks for `.env.ando` first, falls back to `.env`. |
| `--verbosity <level>` | Set output verbosity (quiet\|minimal\|normal\|detailed). |
| `--no-color` | Disable colored output. |
| `--cold` | Always create a fresh container (ignore warm cache). |
| `--image <image>` | Use a custom Docker image. |
| `--dind` | Mount Docker socket for Docker-in-Docker builds. |

## Commit Command

The `ando commit` command commits all staged and unstaged changes with an AI-generated commit message using Claude.

**Requirements:**
- Claude CLI must be installed (`npm install -g @anthropic-ai/claude-code`)
- Must be in a git repository

**How it works:**
1. Checks for uncommitted changes
2. Shows the list of changed files
3. Sends the git diff to Claude to generate a conventional commit message
4. Displays the generated message and asks for confirmation
5. Stages all changes and commits with the approved message

**Example output:**
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

Committed: docs: add options reference to GitHub provider
```

## Bump Command

The `ando bump` command bumps the version of all projects detected in your `build.csando` file.

**Usage:**
```bash
ando bump [patch|minor|major]
```

**Default:** `patch` - bumps the patch version (1.0.0 → 1.0.1)

**How it works:**
1. Detects projects from `build.csando` (Dotnet.Project and Npm directories)
2. Checks for uncommitted changes (offers to run `ando commit` first)
3. Reads current versions from all detected projects
4. Validates all versions match (prompts to select base if mismatched)
5. Calculates the new version based on bump type
6. Updates all project files (.csproj, package.json)
7. Updates documentation (changelog, version badges)
8. Commits the changes automatically

**Supported projects:**
- `.csproj` files referenced via `Dotnet.Project("path")`
- `package.json` files in directories used with `Npm.*` operations

**Example output:**
```
$ ando bump minor
Detecting projects...

Detected projects:
  ./src/App/App.csproj                     1.2.3
  ./website/package.json                   1.2.3

Bumping minor: 1.2.3 → 1.3.0

Updating project versions:
  ✓ ./src/App/App.csproj
  ✓ ./website/package.json

Updating documentation:
  ✓ CHANGELOG.md
  ✓ README.md

Committed: Bump version to 1.3.0
```

## Release Command

The `ando release` command provides an interactive release workflow that orchestrates multiple steps: commit, documentation update, version bump, push, and publish.

**Usage:**
```bash
ando release           # Interactive checklist (all steps selected by default)
ando release --all     # Skip checklist, run all applicable steps
ando release --dry-run # Show what would happen without executing
```

**Steps:**
1. **Commit** - Commit uncommitted changes (uses `ando commit`)
2. **Update Documentation** - Uses Claude to review and update docs based on changes
3. **Bump Version** - Bump version across all projects (uses `ando bump`)
4. **Push** - Push to remote repository
5. **Publish** - Run the publish build (`ando run -p push --dind`)

**Smart Defaults:**
- Steps are contextually enabled/disabled based on repository state
- Commit is disabled when there are no uncommitted changes
- Push is disabled when there's no remote tracking branch
- Publish is disabled when build.csando has no `push` profile

**Example output:**
```
$ ando release

ANDO Release Workflow
─────────────────────

Current state:
  Branch: main
  Version: 0.9.23
  Uncommitted changes: 3 files
  Website folder: yes

Select steps to run:
  [x] Commit uncommitted changes
  [x] Update documentation (Claude)
  [x] Bump version (0.9.23)
  [x] Push to remote (origin/main)
  [x] Run publish build (ando run -p push --dind)

Bump type (current: 0.9.23):
> patch
  minor
  major

Starting release...

Step 1/5: Commit uncommitted changes
────────────────────────────────────────
...

Release complete!
  Version: 0.9.24
  Commit: abc1234
  Branch: main
```

## Hooks

ANDO hooks are `.csando` scripts that run automatically before and after CLI commands. They use the same Roslyn scripting as `build.csando`.

### Hook Types

| Hook | When it runs |
|------|--------------|
| `ando-pre.csando` | Before ANY command |
| `ando-pre-{cmd}.csando` | Before specific command |
| `ando-post-{cmd}.csando` | After specific command |
| `ando-post.csando` | After ANY command |

Commands with hooks: `bump`, `commit`

### Search Locations

Hooks are searched in this order (first found wins):

1. `./scripts/ando-{hook}.csando`
2. `./ando-{hook}.csando`

### Execution Order

For `ando bump`:

```
1. scripts/ando-pre.csando        (general pre-hook)
2. scripts/ando-pre-bump.csando   (command-specific pre-hook)
3. [ando bump executes]
4. scripts/ando-post-bump.csando  (command-specific post-hook)
5. scripts/ando-post.csando       (general post-hook)
```

### Available APIs

Hooks have access to these globals:

| Global | Description |
|--------|-------------|
| `Log.Info()`, `Log.Warning()`, `Log.Error()` | Logging |
| `Env(name)` | Environment variables |
| `Root` | Project root path |
| `Directory(path)` | Directory reference |
| `Shell.RunAsync(cmd, args)` | Run shell commands |

### Environment Variables

Hooks receive context via environment variables:

| Variable | Description | Available in |
|----------|-------------|--------------|
| `ANDO_COMMAND` | Current command | All hooks |
| `ANDO_OLD_VERSION` | Version before bump | bump hooks |
| `ANDO_NEW_VERSION` | Version after bump | post-bump |
| `ANDO_BUMP_TYPE` | patch, minor, or major | bump hooks |

### Hook Behavior

- **Pre-hooks:** Non-zero exit or exception aborts the command
- **Post-hooks:** Failures only warn (command already completed)
- **Missing hooks:** Silently skipped (no error or warning)
- **Execution:** Hooks run on the host machine (not in Docker)

### Example: Run Tests Before Bump

```csharp
// scripts/ando-pre-bump.csando
Log.Info("Running tests before bump...");

var result = await Shell.RunAsync("dotnet", "test", "--no-build");

if (result.ExitCode != 0)
{
    Log.Error("Tests failed. Aborting bump.");
    throw new Exception("Tests failed");
}
```

### Example: Clean Syncthing Conflicts

```csharp
// scripts/ando-pre.csando
var conflicts = Directory.GetFiles(".", "*.sync-conflict-*", SearchOption.AllDirectories);

foreach (var file in conflicts)
{
    Log.Info($"Removing: {file}");
    File.Delete(file);
}
```

## Clean Options

Options for the `ando clean` command.

| Flag | Description |
|------|-------------|
| `--artifacts` | Remove the artifacts directory. |
| `--temp` | Remove temp directory. |
| `--cache` | Remove NuGet and npm caches. |
| `--container` | Remove the project's warm container. |
| `--all` | Remove all of the above. |

## Examples

Common usage patterns.

```bash
# Run the build with default settings
ando

# Commit all changes with AI-generated message
ando commit

# Bump version (patch by default)
ando bump

# Bump minor version
ando bump minor

# Interactive release workflow
ando release

# Release without interactive prompts
ando release --all

# Preview what release would do
ando release --dry-run

# Verify build script without executing
ando verify

# Run with detailed output
ando run --verbosity detailed

# Force a fresh container (cold start)
ando run --cold

# Use a specific Docker image
ando run --image mcr.microsoft.com/dotnet/sdk:9.0

# Run with a build profile
ando -p release

# Clean everything
ando clean --all

# Only remove the warm container
ando clean --container
```
