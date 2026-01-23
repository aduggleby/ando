# ando release - Implementation Plan

## Overview

`ando release` is a CLI command that orchestrates the full release workflow: commit changes, update documentation, bump version, push to remote, and run the publish build. It presents an interactive checklist where users can select which steps to run.

## Usage

```bash
ando release           # Interactive checklist (all steps selected by default)
ando release --all     # Skip checklist, run all steps
ando release --dry-run # Show what would happen without executing
```

## Command Flow

```
┌─────────────────────────────────────┐
│ 1. Check prerequisites              │
│    - Is git repo?                   │
│    - Has build.csando with push?    │
└───────────────┬─────────────────────┘
                │
                ▼
┌─────────────────────────────────────┐
│ 2. Analyze current state            │
│    - Uncommitted changes?           │
│    - Current version?               │
│    - Remote tracking branch?        │
│    - Website folder?                │
└───────────────┬─────────────────────┘
                │
                ▼
┌─────────────────────────────────────┐
│ 3. Show interactive checklist       │
│    [x] Commit changes               │
│    [x] Update documentation         │
│    [x] Bump version (patch)         │
│    [x] Push to remote               │
│    [x] Run publish build            │
└───────────────┬─────────────────────┘
                │
                ▼
┌─────────────────────────────────────┐
│ 4. Execute selected steps           │
│    in order                         │
└─────────────────────────────────────┘
```

## Interactive Checklist

The checklist uses terminal UI with arrow keys and space to toggle:

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
  [x] Commit uncommitted changes (ando commit)
  [x] Update documentation (Claude)
  [x] Bump version (patch → 0.9.24)
  [x] Push to remote (origin/main)
  [x] Run publish build (ando run -p push --dind)

  [Enter] Start  [Space] Toggle  [↑↓] Navigate  [q] Quit
```

### Smart Defaults

The checklist intelligently pre-selects based on state:

| Condition | Default Selection |
|-----------|-------------------|
| No uncommitted changes | Commit unchecked and disabled |
| No website/ folder and no .md files | Update docs unchecked and disabled |
| No build.csando or no push profile | Publish unchecked and disabled |
| No remote tracking | Push unchecked and disabled |

### Bump Type Selection

When bump is selected, user can choose bump type:

```
Select steps to run:
  [x] Commit uncommitted changes (ando commit)
  [x] Update documentation (Claude)
  [x] Bump version: (•) patch  ( ) minor  ( ) major
  [x] Push to remote (origin/main)
  [x] Run publish build (ando run -p push --dind)
```

### Documentation Update Step

The documentation step uses Claude to review and update documentation based on the changes being released:

**What it checks:**
1. If `website/` folder exists:
   - Scans all files in website for relevance to changes
   - Updates documentation pages that reference changed functionality
   - Ensures changelog reflects the changes
   - Checks examples are up to date
2. Always (regardless of website):
   - Scans all `.md` files in the repository
   - Updates README if affected by changes
   - Updates any other markdown documentation

**How it works:**
1. Gather the git diff of changes since last release tag (or all commits if commit step ran)
2. Send to Claude with list of documentation files
3. Claude identifies which files need updates and what changes
4. Show proposed updates to user for confirmation
5. Apply changes and auto-commit with message like "docs: update documentation for release"

**Example output:**
```
Step 2/5: Update Documentation
──────────────────────────────
Analyzing changes for documentation impact...

Found documentation files:
  website/src/content/providers/github.md
  website/src/content/recipes/github-releases.md
  README.md
  CHANGELOG.md

Claude is reviewing...

Proposed documentation updates:
────────────────────────────────────────
1. website/src/content/providers/github.md
   + Add WithFileUpload option to CreateRelease section

2. README.md
   (no changes needed)

3. CHANGELOG.md
   (will be updated by bump step)
────────────────────────────────────────

Apply these documentation updates? [Y/n] y

Updated 1 file(s).
Committed: docs: update documentation for release
```

## Output Examples

### Full release workflow

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
  [x] Commit uncommitted changes (ando commit)
  [x] Update documentation (Claude)
  [x] Bump version: (•) patch  ( ) minor  ( ) major
  [x] Push to remote (origin/main)
  [x] Run publish build (ando run -p push --dind)

Starting release...

Step 1/5: Commit
────────────────
Analyzing changes...

  M src/Ando/Operations/GitHubOperations.cs
  A src/Ando/Operations/FileUploadOptions.cs

Generated commit message:
────────────────────────────────────────
feat: add file upload support to GitHub releases
────────────────────────────────────────

Commit with this message? [Y/n] y

Committed: feat: add file upload support to GitHub releases

Step 2/5: Update Documentation
──────────────────────────────
Analyzing changes for documentation impact...

Found documentation files:
  website/src/content/providers/github.md
  website/src/content/recipes/github-releases.md
  README.md

Claude is reviewing...

Proposed documentation updates:
────────────────────────────────────────
1. website/src/content/providers/github.md
   + Add WithFiles option to CreateRelease section
   + Add example showing file upload usage

2. website/src/content/recipes/github-releases.md
   + Add section on uploading release assets
────────────────────────────────────────

Apply these documentation updates? [Y/n] y

Updated 2 file(s).
Committed: docs: update documentation for release

Step 3/5: Bump Version
──────────────────────
Detected projects:
  src/Ando/Ando.csproj                    0.9.23
  src/Ando.Server/Ando.Server.csproj      0.9.23

Bumping patch: 0.9.23 → 0.9.24

Updated:
  ✓ src/Ando/Ando.csproj
  ✓ src/Ando.Server/Ando.Server.csproj
  ✓ CHANGELOG.md
  ✓ website/src/components/VersionBadge.astro

Committed: Bump version to 0.9.24

Step 4/5: Push
──────────────
Pushing to origin/main...
Done.

Step 5/5: Publish Build
───────────────────────
Running: ando run -p push --dind

[... build output ...]

Release complete!
  Version: 0.9.24
  Commit: abc1234
  Branch: main
```

### Skip commit (no changes)

```
$ ando release

ANDO Release Workflow
─────────────────────

Current state:
  Branch: main
  Version: 0.9.23
  Uncommitted changes: none
  Website folder: yes

Select steps to run:
  [ ] Commit uncommitted changes (no changes)
  [x] Update documentation (Claude)
  [x] Bump version: (•) patch  ( ) minor  ( ) major
  [x] Push to remote (origin/main)
  [x] Run publish build (ando run -p push --dind)
```

### Partial release (bump only)

```
$ ando release

Select steps to run:
  [ ] Commit uncommitted changes (ando commit)
  [ ] Update documentation (Claude)
  [x] Bump version: (•) patch  ( ) minor  ( ) major
  [ ] Push to remote (origin/main)
  [ ] Run publish build (ando run -p push --dind)

Starting release...

Step 1/1: Bump Version
──────────────────────
...

Release complete!
  Version: 0.9.24
```

### No website folder

```
$ ando release

ANDO Release Workflow
─────────────────────

Current state:
  Branch: main
  Version: 1.2.3
  Uncommitted changes: 2 files
  Website folder: no (checking .md files only)

Select steps to run:
  [x] Commit uncommitted changes (ando commit)
  [x] Update documentation (Claude - markdown only)
  [x] Bump version: (•) patch  ( ) minor  ( ) major
  [x] Push to remote (origin/main)
  [ ] Run publish build (no push profile)
```

### With --all flag

```
$ ando release --all

ANDO Release Workflow
─────────────────────

Current state:
  Branch: main
  Version: 0.9.23
  Uncommitted changes: 3 files

Running all steps...

Step 1/5: Commit
────────────────
...
```

### Dry run

```
$ ando release --dry-run

ANDO Release Workflow (dry run)
───────────────────────────────

Would execute:
  1. Commit 3 uncommitted files
  2. Update documentation (website + 3 markdown files)
  3. Bump version: 0.9.23 → 0.9.24 (patch)
  4. Push to origin/main
  5. Run: ando run -p push --dind

No changes made.
```

## File Structure

```
src/Ando/
├── Cli/
│   └── Commands/
│       ├── RunCommand.cs
│       ├── BumpCommand.cs         # From ando-bump
│       ├── CommitCommand.cs       # From ando-commit
│       └── ReleaseCommand.cs      # NEW - orchestrates release workflow
│
├── Release/                       # NEW directory
│   └── ReleaseStep.cs             # Step definition (simple record)
│
└── Utilities/                     # Shared (see ando-bump.md)
    ├── ProcessRunner.cs           # Process execution with timeout
    └── GitOperations.cs           # Git operations
```

Note: Uses [Spectre.Console](https://spectreconsole.net/) for the interactive checklist UI instead of a custom implementation.

## Implementation Details

### ReleaseCommand.cs

```csharp
// =============================================================================
// ReleaseCommand.cs
//
// CLI command handler for 'ando release'. Orchestrates the full release
// workflow with an interactive checklist for step selection.
// =============================================================================

public class ReleaseCommand
{
    private readonly ProcessRunner _runner;
    private readonly GitOperations _git;
    private readonly CommitCommand _commitCommand;
    private readonly BumpCommand _bumpCommand;

    public ReleaseCommand(ProcessRunner runner)
    {
        _runner = runner;
        _git = new GitOperations(runner);
        _commitCommand = new CommitCommand(runner, AnsiConsole.Console);
        _bumpCommand = new BumpCommand(runner);
    }

    public async Task<int> ExecuteAsync(bool all = false, bool dryRun = false)
    {
        // 1. Check prerequisites
        if (!Directory.Exists(".git"))
        {
            AnsiConsole.MarkupLine("[red]Error: Not a git repository.[/]");
            return 1;
        }

        // 2. Analyze current state
        var branch = await _git.GetCurrentBranchAsync();
        var version = await GetCurrentVersionAsync();
        var hasChanges = await _git.HasUncommittedChangesAsync();
        var changedFiles = hasChanges ? await _git.GetChangedFilesAsync() : new List<string>();
        var hasRemote = await _git.HasRemoteTrackingAsync();
        var remoteBranch = hasRemote ? await _git.GetRemoteTrackingBranchAsync() : null;
        var hasWebsite = Directory.Exists("website");
        var hasPushProfile = HasPushProfile();

        // 3. Display current state
        AnsiConsole.MarkupLine("[bold]ANDO Release Workflow[/]");
        AnsiConsole.MarkupLine("─────────────────────");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]Current state:[/]");
        AnsiConsole.MarkupLine($"  Branch: [cyan]{branch}[/]");
        AnsiConsole.MarkupLine($"  Version: [cyan]{version}[/]");
        AnsiConsole.MarkupLine($"  Uncommitted changes: [cyan]{changedFiles.Count} files[/]");
        AnsiConsole.MarkupLine($"  Website folder: [cyan]{(hasWebsite ? "yes" : "no")}[/]");
        AnsiConsole.WriteLine();

        // 4. Build steps
        var steps = BuildSteps(hasChanges, hasWebsite, hasPushProfile, hasRemote, remoteBranch, version);

        // 5. Show checklist or run all
        List<ReleaseStep> selectedSteps;
        BumpType bumpType = BumpType.Patch;

        if (all)
        {
            selectedSteps = steps.Where(s => s.Enabled).ToList();
        }
        else
        {
            var result = ShowChecklist(steps, version);
            if (result == null)
            {
                AnsiConsole.MarkupLine("[yellow]Release cancelled.[/]");
                return 0;
            }
            (selectedSteps, bumpType) = result.Value;
        }

        // 6. Dry run or execute
        if (dryRun)
        {
            ShowDryRun(selectedSteps, version, bumpType);
            return 0;
        }

        return await ExecuteStepsAsync(selectedSteps, bumpType);
    }

    private List<ReleaseStep> BuildSteps(
        bool hasChanges, bool hasWebsite, bool hasPushProfile,
        bool hasRemote, string? remoteBranch, string version)
    {
        return new List<ReleaseStep>
        {
            new("commit", "Commit uncommitted changes", hasChanges, hasChanges ? null : "no changes"),
            new("docs", hasWebsite ? "Update documentation (Claude)" : "Update documentation (Claude - markdown only)", true, null),
            new("bump", "Bump version", true, null),
            new("push", $"Push to remote ({remoteBranch ?? "no remote"})", hasRemote, hasRemote ? null : "no remote tracking"),
            new("publish", "Run publish build (ando run -p push --dind)", hasPushProfile, hasPushProfile ? null : "no push profile")
        };
    }

    private (List<ReleaseStep> steps, BumpType bumpType)? ShowChecklist(List<ReleaseStep> steps, string currentVersion)
    {
        // Use Spectre.Console MultiSelectionPrompt
        var choices = steps.Select(s => s.Enabled
            ? s.Label
            : $"[dim]{s.Label} ({s.DisabledReason})[/]").ToList();

        var selected = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("Select steps to run:")
                .NotRequired()
                .PageSize(10)
                .AddChoices(choices)
                .Select(choices.Where((c, i) => steps[i].Enabled)));

        if (selected.Count == 0)
            return null;

        var selectedSteps = steps.Where((s, i) => selected.Contains(choices[i])).ToList();

        // Ask for bump type if bump is selected
        var bumpType = BumpType.Patch;
        if (selectedSteps.Any(s => s.Id == "bump"))
        {
            var bumpChoice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"Bump type (current: [cyan]{currentVersion}[/]):")
                    .AddChoices("patch", "minor", "major"));

            bumpType = bumpChoice switch
            {
                "minor" => BumpType.Minor,
                "major" => BumpType.Major,
                _ => BumpType.Patch
            };

            var newVersion = CalculateNextVersion(currentVersion, bumpType);
            AnsiConsole.MarkupLine($"  [dim]→ {currentVersion} → {newVersion}[/]");
        }

        return (selectedSteps, bumpType);
    }

    private async Task<int> ExecuteStepsAsync(List<ReleaseStep> steps, BumpType bumpType)
    {
        var stepNumber = 0;
        var totalSteps = steps.Count;

        AnsiConsole.MarkupLine("[bold]Starting release...[/]");
        AnsiConsole.WriteLine();

        foreach (var step in steps)
        {
            stepNumber++;
            AnsiConsole.MarkupLine($"[bold]Step {stepNumber}/{totalSteps}: {step.Label}[/]");
            AnsiConsole.MarkupLine(new string('─', 40));

            var result = step.Id switch
            {
                "commit" => await _commitCommand.ExecuteAsync(),
                "docs" => await ExecuteDocsUpdateAsync(),
                "bump" => await _bumpCommand.ExecuteAsync(bumpType),
                "push" => await ExecutePushAsync(),
                "publish" => await ExecutePublishAsync(),
                _ => throw new InvalidOperationException($"Unknown step: {step.Id}")
            };

            if (result != 0)
            {
                AnsiConsole.MarkupLine($"[red]Step '{step.Label}' failed.[/]");
                return result;
            }

            AnsiConsole.WriteLine();
        }

        // Show summary
        var newVersion = await GetCurrentVersionAsync();
        var commitHash = await _git.GetCurrentCommitShortAsync();
        var branch = await _git.GetCurrentBranchAsync();

        AnsiConsole.MarkupLine("[green bold]Release complete![/]");
        AnsiConsole.MarkupLine($"  Version: [cyan]{newVersion}[/]");
        AnsiConsole.MarkupLine($"  Commit: [cyan]{commitHash}[/]");
        AnsiConsole.MarkupLine($"  Branch: [cyan]{branch}[/]");

        return 0;
    }

    private async Task<int> ExecuteDocsUpdateAsync()
    {
        var diff = await _git.GetLastTagAsync() is string tag
            ? await _git.GetDiffSinceTagAsync(tag)
            : await _git.GetDiffAsync();

        if (string.IsNullOrWhiteSpace(diff))
        {
            AnsiConsole.MarkupLine("[dim]No changes to analyze.[/]");
            return 0;
        }

        var hasWebsite = Directory.Exists("website");

        var prompt = $"""
            Review and update documentation based on these code changes.

            ## Code Changes
            ```
            {(diff.Length > 8000 ? diff[..8000] + "\n\n[Diff truncated]" : diff)}
            ```

            ## Instructions

            1. Find all markdown files (*.md) anywhere in the repository
            2. If a website/ folder exists, also check documentation pages there (*.mdx, *.astro)
            3. For each documentation file, determine if it needs updates based on the code changes
            4. Skip CHANGELOG.md (handled separately by version bump)

            ## What to Update

            **For website pages and README.md:**
            - New features that need documentation
            - Changed behavior that affects users
            - New options, parameters, or configuration
            - Examples that need updating
            - Ignore internal refactoring that doesn't affect public API or user-facing behavior

            **For internal documentation (architecture docs, design docs, developer guides):**
            - Update to reflect any code changes including internal refactoring
            - Keep architecture documentation in sync with actual implementation
            - Update design decisions if the code has diverged

            ## Process

            For each file that needs changes:
            - Read the current content
            - Make the necessary updates
            - Show me what you're changing before writing

            If no documentation updates are needed, just say so.
            """;

        AnsiConsole.MarkupLine("[dim]Claude is reviewing documentation...[/]");

        try
        {
            await _runner.RunClaudeAsync(prompt, timeoutMs: 300000); // 5 min timeout
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Documentation update skipped: {ex.Message}[/]");
            // Non-fatal - continue with release
        }

        return 0;
    }

    private async Task<int> ExecutePushAsync()
    {
        AnsiConsole.MarkupLine("Pushing to remote...");

        try
        {
            await _git.PushAsync();
            AnsiConsole.MarkupLine("[green]Done.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Git push failed: {ex.Message}[/]");
            return 1;
        }
    }

    private async Task<int> ExecutePublishAsync()
    {
        AnsiConsole.MarkupLine("Running: ando run -p push --dind");
        AnsiConsole.WriteLine();

        var result = await _runner.RunAsync("ando", "run -p push --dind", timeoutMs: 600000);
        return result.ExitCode;
    }

    private bool HasPushProfile()
    {
        var buildScript = Path.Combine(Directory.GetCurrentDirectory(), "build.csando");
        if (!File.Exists(buildScript))
            return false;

        var content = File.ReadAllText(buildScript);
        return content.Contains("Profile(\"push\"") || content.Contains("Profile(\"push\",");
    }

    private async Task<string> GetCurrentVersionAsync()
    {
        var detector = new ProjectDetector();
        var buildScript = Path.Combine(Directory.GetCurrentDirectory(), "build.csando");

        if (!File.Exists(buildScript))
            return "0.0.0";

        var projects = detector.DetectProjects(buildScript);
        if (projects.Count == 0)
            return "0.0.0";

        var reader = new VersionReader();
        return reader.ReadVersion(projects[0].Path, projects[0].Type) ?? "0.0.0";
    }

    private string CalculateNextVersion(string current, BumpType type)
    {
        if (!SemVer.TryParse(current, out var semver))
            return "0.0.1";

        return semver.Bump(type).ToString();
    }

    private void ShowDryRun(List<ReleaseStep> steps, string version, BumpType bumpType)
    {
        AnsiConsole.MarkupLine("[bold]ANDO Release Workflow (dry run)[/]");
        AnsiConsole.MarkupLine("───────────────────────────────");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Would execute:[/]");

        var stepNum = 0;
        foreach (var step in steps)
        {
            stepNum++;
            var detail = step.Id == "bump"
                ? $" ({version} → {CalculateNextVersion(version, bumpType)})"
                : "";
            AnsiConsole.MarkupLine($"  {stepNum}. {step.Label}{detail}");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]No changes made.[/]");
    }
}
```

### ReleaseStep.cs

```csharp
// =============================================================================
// ReleaseStep.cs
//
// Simple record representing a step in the release workflow.
// =============================================================================

public record ReleaseStep(
    string Id,
    string Label,
    bool Enabled,
    string? DisabledReason
);
```

## CLI Registration

```csharp
var releaseCommand = new Command("release", "Run the full release workflow");

var allOption = new Option<bool>("--all", "Skip checklist and run all applicable steps");
var dryRunOption = new Option<bool>("--dry-run", "Show what would happen without executing");

releaseCommand.AddOption(allOption);
releaseCommand.AddOption(dryRunOption);

releaseCommand.SetHandler(async (bool all, bool dryRun) =>
{
    var runner = new ProcessRunner();
    var command = new ReleaseCommand(runner);
    return await command.ExecuteAsync(all, dryRun);
},
allOption, dryRunOption);

rootCommand.AddCommand(releaseCommand);
```

## Hook Integration

`ando release` triggers hooks for each sub-command:

```
ando release
│
├── [ando-pre.csando]           # General pre-hook
├── [ando-pre-release.csando]   # Release-specific pre-hook
│
├── Step: Commit
│   ├── [ando-pre-commit.csando]
│   ├── (commit execution)
│   └── [ando-post-commit.csando]
│
├── Step: Update Docs
│   └── (Claude reviews and updates docs - no sub-hooks)
│
├── Step: Bump
│   ├── [ando-pre-bump.csando]
│   ├── (bump execution)
│   └── [ando-post-bump.csando]
│
├── Step: Push
│   └── (git push - no hooks)
│
├── Step: Publish
│   ├── [ando-pre-run.csando]
│   ├── (ando run execution)
│   └── [ando-post-run.csando]   # ← NuGet wait + tool update goes here
│
├── [ando-post-release.csando]  # Release-specific post-hook
└── [ando-post.csando]          # General post-hook
```

## Error Handling

| Error | Behavior |
|-------|----------|
| Not a git repo | Exit with error message |
| No steps selected | Exit cleanly (user cancelled) |
| Commit step fails | Stop workflow, show error |
| Docs step - Claude unavailable | Warn and skip (non-fatal) |
| Docs step - Claude timeout | Warn and skip (non-fatal) |
| Bump step fails | Stop workflow, show error |
| Push fails | Stop workflow, show error |
| Publish fails | Stop workflow, show build errors |
| User cancels checklist | Exit cleanly (cancelled) |

### Partial Failure Recovery

If a step fails mid-workflow, the user can:
1. Fix the issue manually
2. Run `ando release` again with earlier steps unchecked
3. Or run individual commands (`ando commit`, `ando bump`, etc.)

## Testing Strategy

### Unit Tests

| Test | Description |
|------|-------------|
| `ReleaseCommand_DetectsPushProfile` | Finds push profile in build.csando |
| `ReleaseCommand_DetectsWebsite` | Finds website/ folder |
| `ReleaseCommand_BuildsSteps` | Correctly builds step list based on state |
| `ReleaseStep_DisabledWhenNoChanges` | Commit disabled when clean |
| `ReleaseStep_DisabledWhenNoRemote` | Push disabled without remote |
| `ReleaseStep_DisabledWhenNoPushProfile` | Publish disabled without push profile |
| `CalculateNextVersion_Patch` | 1.2.3 → 1.2.4 |
| `CalculateNextVersion_Minor` | 1.2.3 → 1.3.0 |
| `CalculateNextVersion_Major` | 1.2.3 → 2.0.0 |

### Integration Tests

| Test | Description |
|------|-------------|
| `Release_FullWorkflow` | All steps execute in order |
| `Release_PartialSelection` | Only selected steps run |
| `Release_DryRun` | Shows plan without executing |
| `Release_StopsOnFailure` | Subsequent steps don't run after failure |
| `Release_DocsStep_CallsClaude` | Docs step invokes Claude with correct prompt |
| `Release_DocsStep_NonFatalOnError` | Continues if Claude fails |

## Future Enhancements

Out of scope for initial implementation:

- `--skip-commit` / `--skip-bump` etc. flags for scripted use
- Rollback support (undo on failure)
- Parallel step execution where possible
- Release notes generation
- Tag creation option
- Multi-repo release coordination
