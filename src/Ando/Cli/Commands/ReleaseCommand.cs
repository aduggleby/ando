// =============================================================================
// ReleaseCommand.cs
//
// Summary: CLI command handler for 'ando release'.
//
// Orchestrates the full release workflow: commit changes, update documentation,
// bump version, push to remote, and run the publish build. Presents an
// interactive checklist where users can select which steps to run.
//
// Design Decisions:
// - Uses Spectre.Console for interactive checklist UI
// - Steps are contextually enabled/disabled based on repository state
// - Documentation updates use Claude to analyze changes and update docs
// - Failure in any step stops the workflow (except docs which is non-fatal)
// - Supports --all flag to skip checklist and --dry-run to preview
// =============================================================================

using Ando.Hooks;
using Ando.Logging;
using Ando.Release;
using Ando.Utilities;
using Ando.Versioning;
using Spectre.Console;

namespace Ando.Cli.Commands;

/// <summary>
/// CLI command handler for 'ando release'. Orchestrates the full release
/// workflow with an interactive checklist for step selection.
/// </summary>
public class ReleaseCommand
{
    private readonly CliProcessRunner _runner;
    private readonly CliGitOperations _git;
    private readonly IBuildLogger _logger;

    public ReleaseCommand(CliProcessRunner runner, IBuildLogger logger)
    {
        _runner = runner;
        _git = new CliGitOperations(runner);
        _logger = logger;
    }

    /// <summary>
    /// Executes the release command.
    /// </summary>
    /// <param name="all">Skip checklist and run all applicable steps.</param>
    /// <param name="dryRun">Show what would happen without executing.</param>
    /// <param name="bumpType">Version bump type (patch, minor, major). Defaults to patch.</param>
    /// <returns>Exit code: 0 for success, 1 for errors.</returns>
    public async Task<int> ExecuteAsync(bool all = false, bool dryRun = false, BumpType bumpType = BumpType.Patch)
    {
        try
        {
            // 1. Check prerequisites.
            if (!await _git.IsGitRepositoryAsync())
            {
                _logger.Error("Error: Not a git repository.");
                return 1;
            }

            var repoRoot = Directory.GetCurrentDirectory();

            // 2. Analyze current state.
            var branch = await _git.GetCurrentBranchAsync();
            var version = GetCurrentVersion(repoRoot);
            var hasChanges = await _git.HasUncommittedChangesAsync();
            var changedFiles = hasChanges ? await _git.GetChangedFilesAsync() : [];
            var hasRemote = await _git.HasRemoteTrackingAsync();
            var remoteBranch = hasRemote ? await _git.GetRemoteTrackingBranchAsync() : null;
            var hasWebsite = Directory.Exists(Path.Combine(repoRoot, "website"));
            var hasPushProfile = HasPushProfile(repoRoot);

            // 3. Display current state.
            AnsiConsole.MarkupLine("[bold]ANDO Release Workflow[/]");
            AnsiConsole.MarkupLine("─────────────────────");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Current state:[/]");
            AnsiConsole.MarkupLine($"  Branch: [cyan]{branch}[/]");
            AnsiConsole.MarkupLine($"  Version: [cyan]{version}[/]");
            AnsiConsole.MarkupLine($"  Uncommitted changes: [cyan]{(hasChanges ? $"{changedFiles.Count} files" : "none")}[/]");
            AnsiConsole.MarkupLine($"  Website folder: [cyan]{(hasWebsite ? "yes" : "no")}[/]");
            AnsiConsole.WriteLine();

            // Initialize hook runner.
            var hookRunner = new HookRunner(repoRoot, _logger);
            var hookContext = new HookContext { Command = "release" };

            // Run pre-hooks.
            if (!await hookRunner.RunHooksAsync(HookRunner.HookType.Pre, "release", hookContext))
            {
                _logger.Error("Release aborted by pre-hook.");
                return 1;
            }

            // 4. Build steps.
            var steps = BuildSteps(hasChanges, hasWebsite, hasPushProfile, hasRemote, remoteBranch, version);

            // 5. Show checklist or run all.
            List<ReleaseStep> selectedSteps;

            if (all)
            {
                selectedSteps = steps.Where(s => s.Enabled).ToList();
            }
            else
            {
                var result = ShowChecklist(steps, version, bumpType);
                if (result == null)
                {
                    AnsiConsole.MarkupLine("[yellow]Release cancelled.[/]");
                    return 0;
                }
                selectedSteps = result;
            }

            if (selectedSteps.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No steps selected. Release cancelled.[/]");
                return 0;
            }

            // 6. Dry run or execute.
            if (dryRun)
            {
                ShowDryRun(selectedSteps, version, bumpType);
                return 0;
            }

            // 7. Run mandatory build verification before release steps.
            var buildResult = await ExecuteBuildVerificationAsync();
            if (buildResult != 0)
            {
                AnsiConsole.MarkupLine("[red]Build verification failed. Release aborted.[/]");
                return buildResult;
            }

            var exitCode = await ExecuteStepsAsync(selectedSteps, bumpType, repoRoot);

            // Run post-hooks.
            await hookRunner.RunHooksAsync(HookRunner.HookType.Post, "release", hookContext);

            return exitCode;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error: {ex.Message}");
            return 1;
        }
    }

    private List<ReleaseStep> BuildSteps(
        bool hasChanges, bool hasWebsite, bool hasPushProfile,
        bool hasRemote, string? remoteBranch, string version)
    {
        var mdFiles = Directory.GetFiles(".", "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains("node_modules") && !f.Contains("bin") && !f.Contains("obj"))
            .Count();
        var hasDocsToUpdate = hasWebsite || mdFiles > 0;

        return
        [
            new("commit", "Commit uncommitted changes", hasChanges, hasChanges ? null : "no changes"),
            new("bump", $"Bump version ({version})", true, null),
            new("docs", hasWebsite
                ? "Update documentation (Claude)"
                : "Update documentation (Claude - markdown only)",
                hasDocsToUpdate, hasDocsToUpdate ? null : "no documentation files"),
            new("push", $"Push to remote ({remoteBranch ?? "no remote"})", hasRemote, hasRemote ? null : "no remote tracking"),
            new("publish", "Run publish build (ando run -p push --dind --read-env)", hasPushProfile, hasPushProfile ? null : "no push profile")
        ];
    }

    private List<ReleaseStep>? ShowChecklist(List<ReleaseStep> steps, string currentVersion, BumpType bumpType)
    {
        // Show what version bump will be applied.
        var newVersion = CalculateNextVersion(currentVersion, bumpType);
        AnsiConsole.MarkupLine($"[bold]Version bump:[/] {currentVersion} → {newVersion} [dim]({bumpType.ToString().ToLower()})[/]");
        AnsiConsole.WriteLine();

        // Build choices with disabled state shown.
        var choices = steps.Select(s => s.Enabled
            ? s.Label
            : $"{s.Label} [dim]({s.DisabledReason})[/]").ToList();

        // Find which indices are enabled and should be pre-selected.
        var enabledIndices = steps.Select((s, i) => (s, i))
            .Where(x => x.s.Enabled)
            .Select(x => x.i)
            .ToList();

        var prompt = new MultiSelectionPrompt<string>()
            .Title("Select steps to run:")
            .NotRequired()
            .PageSize(10)
            .InstructionsText("[grey](Press [cyan]<space>[/] to toggle, [cyan]<enter>[/] to accept)[/]")
            .AddChoices(choices);

        // Pre-select enabled steps.
        foreach (var idx in enabledIndices)
        {
            prompt.Select(choices[idx]);
        }

        var selected = AnsiConsole.Prompt(prompt);

        if (selected.Count == 0)
            return null;

        // Map selected labels back to steps.
        var selectedSteps = new List<ReleaseStep>();
        for (int i = 0; i < steps.Count; i++)
        {
            if (selected.Contains(choices[i]) && steps[i].Enabled)
            {
                selectedSteps.Add(steps[i]);
            }
        }

        AnsiConsole.WriteLine();
        return selectedSteps;
    }

    private async Task<int> ExecuteStepsAsync(List<ReleaseStep> steps, BumpType bumpType, string repoRoot)
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
                "commit" => await ExecuteCommitAsync(repoRoot),
                "bump" => await ExecuteBumpAsync(bumpType, repoRoot),
                "docs" => await ExecuteDocsUpdateAsync(repoRoot),
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

        // Show summary.
        var newVersion = GetCurrentVersion(repoRoot);
        var commitHash = await _git.GetCurrentCommitShortAsync();
        var branch = await _git.GetCurrentBranchAsync();

        AnsiConsole.MarkupLine("[green bold]Release complete![/]");
        AnsiConsole.MarkupLine($"  Version: [cyan]{newVersion}[/]");
        AnsiConsole.MarkupLine($"  Commit: [cyan]{commitHash}[/]");
        AnsiConsole.MarkupLine($"  Branch: [cyan]{branch}[/]");

        return 0;
    }

    private async Task<int> ExecuteCommitAsync(string repoRoot)
    {
        var commitCommand = new CommitCommand(_runner, _logger);
        return await commitCommand.ExecuteAsync();
    }

    private async Task<int> ExecuteDocsUpdateAsync(string repoRoot)
    {
        // Get diff since last tag or all changes.
        var lastTag = await _git.GetLastTagAsync();
        var diff = lastTag != null
            ? await _git.GetDiffSinceTagAsync(lastTag)
            : await _git.GetDiffAsync();

        if (string.IsNullOrWhiteSpace(diff))
        {
            AnsiConsole.MarkupLine("[dim]No changes to analyze.[/]");
            return 0;
        }

        var hasWebsite = Directory.Exists(Path.Combine(repoRoot, "website"));

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
            - Ignore internal refactoring that doesn't affect public API

            ## Process

            For each file that needs changes:
            - Read the current content
            - Make the necessary updates
            - Show what you're changing before writing

            If no documentation updates are needed, just say so.
            """;

        AnsiConsole.MarkupLine("[dim]Claude is reviewing documentation...[/]");
        AnsiConsole.WriteLine();

        try
        {
            await _runner.RunClaudeAsync(prompt, timeoutMs: 300000, streamOutput: true);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow]Documentation update skipped: {ex.Message}[/]");
            // Non-fatal - continue with release.
            return 0;
        }

        // Check if Claude made any changes and commit them.
        // Version was already bumped in the previous step.
        if (await _git.HasUncommittedChangesAsync())
        {
            var version = GetCurrentVersion(repoRoot);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Committing documentation changes...[/]");

            try
            {
                await _git.StageAllAsync();
                await _git.CommitAsync($"docs: update documentation for v{version}");
                AnsiConsole.MarkupLine("[green]Documentation changes committed.[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[yellow]Failed to commit documentation changes: {ex.Message}[/]");
                // Non-fatal - continue with release.
            }
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]No documentation changes needed.[/]");
        }

        return 0;
    }

    private async Task<int> ExecuteBumpAsync(BumpType bumpType, string repoRoot)
    {
        var bumpCommand = new BumpCommand(_runner, _logger);
        return await bumpCommand.ExecuteAsync(bumpType);
    }

    private async Task<int> ExecutePushAsync()
    {
        AnsiConsole.MarkupLine("Pushing to remote...");
        AnsiConsole.WriteLine();

        try
        {
            await _git.PushAsync(streamOutput: true);
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
        AnsiConsole.MarkupLine("Running: ando run -p push --dind --read-env");
        AnsiConsole.WriteLine();

        var result = await _runner.RunAsync("ando", "run -p push --dind --read-env", timeoutMs: 600000, streamOutput: true);
        return result.ExitCode;
    }

    private async Task<int> ExecuteBuildVerificationAsync()
    {
        AnsiConsole.MarkupLine("[bold]Build Verification[/]");
        AnsiConsole.MarkupLine("──────────────────");
        AnsiConsole.MarkupLine("Running: ando run --read-env");
        AnsiConsole.WriteLine();

        var result = await _runner.RunAsync("ando", "run --read-env", timeoutMs: 600000, streamOutput: true);

        if (result.ExitCode == 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]Build verification passed.[/]");
            AnsiConsole.WriteLine();
        }

        return result.ExitCode;
    }

    private static bool HasPushProfile(string repoRoot)
    {
        var buildScript = Path.Combine(repoRoot, "build.csando");
        if (!File.Exists(buildScript))
            return false;

        var content = File.ReadAllText(buildScript);
        return content.Contains("DefineProfile(\"push\"") ||
               content.Contains("DefineProfile('push'");
    }

    private static string GetCurrentVersion(string repoRoot)
    {
        var buildScript = Path.Combine(repoRoot, "build.csando");
        if (!File.Exists(buildScript))
            return "0.0.0";

        var detector = new ProjectDetector();
        var projects = detector.DetectProjects(buildScript);
        if (projects.Count == 0)
            return "0.0.0";

        var reader = new VersionReader();
        var fullPath = Path.Combine(repoRoot, projects[0].Path);
        return reader.ReadVersion(fullPath, projects[0].Type) ?? "0.0.0";
    }

    private static string CalculateNextVersion(string current, BumpType type)
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
