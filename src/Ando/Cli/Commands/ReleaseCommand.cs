// =============================================================================
// ReleaseCommand.cs
//
// Summary: CLI command handler for 'ando release' and 'ando ship'.
//
// Orchestrates the release/ship workflow: commit changes, update documentation,
// bump version, push to remote, and optionally run the publish build. Presents
// an interactive checklist where users can select which steps to run.
// The 'ship' command reuses this handler with includePublish=false (4 steps).
//
// Design Decisions:
// - Uses Spectre.Console for interactive checklist UI
// - Steps are contextually enabled/disabled based on repository state
// - Documentation updates use Claude to analyze changes and update docs
// - Failure in any step stops the workflow (except docs which is non-fatal)
// - Supports --all flag to skip checklist and --dry-run to preview
// - Parameterized to power both 'release' (5 steps) and 'ship' (4 steps)
// =============================================================================

using Ando.Hooks;
using Ando.Logging;
using Ando.Release;
using Ando.Utilities;
using Ando.Versioning;
using Spectre.Console;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ando.Cli.Commands;

/// <summary>
/// CLI command handler for 'ando release' and 'ando ship'. Orchestrates the
/// release/ship workflow with an interactive checklist for step selection.
/// The 'ship' command is a release without the publish step.
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
    /// Executes the release or ship command.
    /// </summary>
    /// <param name="all">Skip checklist and run all applicable steps.</param>
    /// <param name="dryRun">Show what would happen without executing.</param>
    /// <param name="bumpType">Version bump type (patch, minor, major). Defaults to patch.</param>
    /// <param name="includePublish">Whether to include the publish step. False for 'ship' command.</param>
    /// <param name="commandName">Command name for display and hooks ("release" or "ship").</param>
    /// <returns>Exit code: 0 for success, 1 for errors.</returns>
    public async Task<int> ExecuteAsync(bool all = false, bool dryRun = false, BumpType bumpType = BumpType.Patch,
        bool includePublish = true, string commandName = "release")
    {
        var startedAt = DateTimeOffset.Now;
        var sw = Stopwatch.StartNew();

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
            var hasPublishProfile = includePublish && HasPublishProfile(repoRoot);
            var (hasChangesSinceTag, lastTag, commitCount) = await _git.GetChangesSinceLastTagAsync();

            // 3. Display current state.
            var title = char.ToUpper(commandName[0]) + commandName[1..];
            AnsiConsole.MarkupLine($"[bold]ANDO {title} Workflow[/]");
            AnsiConsole.MarkupLine(new string('─', $"ANDO {title} Workflow".Length));
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Current state:[/]");
            AnsiConsole.MarkupLine($"  Branch: [cyan]{branch}[/]");
            AnsiConsole.MarkupLine($"  Version: [cyan]{version}[/]");
            AnsiConsole.MarkupLine($"  Uncommitted changes: [cyan]{(hasChanges ? $"{changedFiles.Count} files" : "none")}[/]");
            var changesSinceTagText = lastTag == null
                ? "first release"
                : hasChangesSinceTag ? $"{commitCount} commits since {lastTag}" : $"none since {lastTag}";
            AnsiConsole.MarkupLine($"  Changes since last tag: [cyan]{changesSinceTagText}[/]");
            AnsiConsole.MarkupLine($"  Website folder: [cyan]{(hasWebsite ? "yes" : "no")}[/]");
            AnsiConsole.WriteLine();

            // Initialize hook runner.
            var hookRunner = new HookRunner(repoRoot, _logger);
            var hookContext = new HookContext { Command = commandName };

            // Run pre-hooks.
            if (!await hookRunner.RunHooksAsync(HookRunner.HookType.Pre, commandName, hookContext))
            {
                _logger.Error($"{title} aborted by pre-hook.");
                return 1;
            }

            // 4. Build steps.
            var steps = BuildSteps(hasChanges, hasWebsite, hasPublishProfile, hasRemote, remoteBranch, version, hasChangesSinceTag, lastTag, includePublish);

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
                    AnsiConsole.MarkupLine($"[yellow]{title} cancelled.[/]");
                    return 0;
                }
                selectedSteps = result;
            }

            if (selectedSteps.Count == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]No steps selected. {title} cancelled.[/]");
                return 0;
            }

            // 6. Dry run or execute.
            if (dryRun)
            {
                ShowDryRun(selectedSteps, version, bumpType, commandName);
                return 0;
            }

            // 7. Run mandatory build verification before release steps.
            var buildResult = await ExecuteBuildVerificationAsync();
            if (buildResult != 0)
            {
                AnsiConsole.MarkupLine("[red]Build verification failed. Release aborted.[/]");
                return buildResult;
            }

            var exitCode = await ExecuteStepsAsync(selectedSteps, bumpType, repoRoot, version, bumpType, startedAt, sw, commandName);

            // Run post-hooks with exit code so they can check success/failure.
            var postHookContext = hookContext with { ExitCode = exitCode };
            await hookRunner.RunHooksAsync(HookRunner.HookType.Post, commandName, postHookContext);

            return exitCode;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            sw.Stop();
        }
    }

    private List<ReleaseStep> BuildSteps(
        bool hasChanges, bool hasWebsite, bool hasPublishProfile,
        bool hasRemote, string? remoteBranch, string version,
        bool hasChangesSinceTag, string? lastTag, bool includePublish = true)
    {
        var mdFiles = Directory.GetFiles(".", "*.md", SearchOption.AllDirectories)
            .Where(f => !f.Contains("node_modules") && !f.Contains("bin") && !f.Contains("obj"))
            .Count();
        var hasDocsToUpdate = hasWebsite || mdFiles > 0;

        // Bump is enabled if there are changes since the last tag OR uncommitted changes
        // (which will become a commit). We note "if any changes" since the commit step runs first.
        var canBump = hasChangesSinceTag || hasChanges;
        var bumpDisabledReason = canBump ? null : $"no changes since {lastTag}";

        var steps = new List<ReleaseStep>
        {
            new("commit", "Commit uncommitted changes", hasChanges, hasChanges ? null : "no changes"),
            new("bump", $"Bump version ({version}) if any changes", canBump, bumpDisabledReason),
            new("docs", hasWebsite
                ? "Update documentation (Claude)"
                : "Update documentation (Claude - markdown only)",
                hasDocsToUpdate, hasDocsToUpdate ? null : "no documentation files"),
            new("push", $"Push to remote ({remoteBranch ?? "no remote"})", hasRemote, hasRemote ? null : "no remote tracking"),
        };

        // Only include the publish step for the 'release' command.
        if (includePublish)
        {
            steps.Add(new("publish", "Run publish build (ando run -p publish --dind --read-env)", hasPublishProfile, hasPublishProfile ? null : "no publish profile"));
        }

        return steps;
    }

    private List<ReleaseStep>? ShowChecklist(List<ReleaseStep> steps, string currentVersion, BumpType bumpType)
    {
        // Show what version bump will be applied.
        var newVersion = CalculateNextVersion(currentVersion, bumpType);
        AnsiConsole.MarkupLine($"[bold]Version bump:[/] {currentVersion} → {newVersion} [dim]({bumpType.ToString().ToLower()})[/]");
        AnsiConsole.WriteLine();

        // Use custom selection UI that supports Escape to cancel.
        return ShowCustomSelection(steps);
    }

    /// <summary>
    /// Custom multi-selection UI that supports Escape key to cancel.
    /// Spectre.Console's MultiSelectionPrompt doesn't support Escape natively.
    /// </summary>
    private List<ReleaseStep>? ShowCustomSelection(List<ReleaseStep> steps)
    {
        var selections = steps.Select(s => s.Enabled).ToArray();
        var currentIndex = 0;

        // Find first enabled step to start on.
        for (int i = 0; i < steps.Count; i++)
        {
            if (steps[i].Enabled)
            {
                currentIndex = i;
                break;
            }
        }

        Console.CursorVisible = false;
        try
        {
            while (true)
            {
                // Render the selection UI.
                RenderSelection(steps, selections, currentIndex);

                var key = Console.ReadKey(intercept: true);
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                    case ConsoleKey.K: // vim-style
                        currentIndex = Math.Max(0, currentIndex - 1);
                        break;

                    case ConsoleKey.DownArrow:
                    case ConsoleKey.J: // vim-style
                        currentIndex = Math.Min(steps.Count - 1, currentIndex + 1);
                        break;

                    case ConsoleKey.Spacebar:
                        if (steps[currentIndex].Enabled)
                            selections[currentIndex] = !selections[currentIndex];
                        break;

                    case ConsoleKey.A: // Toggle all
                        var allSelected = steps.Select((s, i) => (s, i))
                            .Where(x => x.s.Enabled)
                            .All(x => selections[x.i]);
                        for (int i = 0; i < steps.Count; i++)
                        {
                            if (steps[i].Enabled)
                                selections[i] = !allSelected;
                        }
                        break;

                    case ConsoleKey.Enter:
                        ClearSelection(steps.Count);
                        var selected = steps.Where((s, i) => selections[i] && s.Enabled).ToList();
                        return selected.Count > 0 ? selected : null;

                    case ConsoleKey.Escape:
                        ClearSelection(steps.Count);
                        return null;
                }
            }
        }
        finally
        {
            Console.CursorVisible = true;
        }
    }

    private void RenderSelection(List<ReleaseStep> steps, bool[] selections, int currentIndex)
    {
        // Move cursor to start of selection area and clear.
        var startLine = Console.CursorTop;
        Console.SetCursorPosition(0, startLine);

        // Calculate box width based on longest step label.
        var maxLabelLength = steps.Max(s => s.Label.Length + (s.DisabledReason != null ? s.DisabledReason.Length + 3 : 0));
        var boxWidth = Math.Max(maxLabelLength + 8, 70); // 8 for "  ● " prefix + padding

        // Draw top border.
        AnsiConsole.MarkupLine($"[blue]┌{"".PadRight(boxWidth, '─')}┐[/]");

        // Title line.
        var title = " Select steps to run: ";
        var titlePadding = boxWidth - title.Length;
        AnsiConsole.MarkupLine($"[blue]│[/][bold]{title}[/]{new string(' ', titlePadding)}[blue]│[/]");

        // Help line.
        var help = " (↑↓ navigate, Space toggle, A all, Enter accept, Esc cancel) ";
        var helpPadding = boxWidth - help.Length;
        AnsiConsole.MarkupLine($"[blue]│[/][grey]{help}[/]{new string(' ', Math.Max(0, helpPadding))}[blue]│[/]");

        // Separator.
        AnsiConsole.MarkupLine($"[blue]├{"".PadRight(boxWidth, '─')}┤[/]");

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var isSelected = selections[i];
            var isCurrent = i == currentIndex;

            var pointer = isCurrent ? "[cyan]>[/] " : "  ";
            var checkbox = isSelected ? "[green]●[/]" : "[grey]○[/]";

            string content;
            int contentLength;

            if (!step.Enabled)
            {
                var label = $"{step.Label} ({step.DisabledReason})";
                content = $"{pointer}{checkbox} [dim strikethrough]{step.Label}[/] [dim]({step.DisabledReason})[/]";
                contentLength = 4 + label.Length; // "  ● " + label
            }
            else if (isCurrent)
            {
                content = $"{pointer}{checkbox} [cyan]{step.Label}[/]";
                contentLength = 4 + step.Label.Length;
            }
            else
            {
                content = $"{pointer}{checkbox} {step.Label}";
                contentLength = 4 + step.Label.Length;
            }

            var padding = boxWidth - contentLength;
            AnsiConsole.MarkupLine($"[blue]│[/]{content}{new string(' ', Math.Max(0, padding))}[blue]│[/]");
        }

        // Draw bottom border.
        AnsiConsole.MarkupLine($"[blue]└{"".PadRight(boxWidth, '─')}┘[/]");

        // Move cursor back to start for next render.
        Console.SetCursorPosition(0, startLine);
    }

    private void ClearSelection(int stepCount)
    {
        // Clear the selection UI (5 box lines: top, title, help, separator, bottom + step lines).
        var linesToClear = 5 + stepCount;
        for (int i = 0; i < linesToClear; i++)
        {
            Console.WriteLine(new string(' ', Console.WindowWidth - 1));
        }
        Console.SetCursorPosition(0, Console.CursorTop - linesToClear);
    }

    private async Task<int> ExecuteStepsAsync(
        List<ReleaseStep> steps,
        BumpType bumpType,
        string repoRoot,
        string originalVersion,
        BumpType requestedBumpType,
        DateTimeOffset startedAt,
        Stopwatch sw,
        string commandName = "release")
    {
        var title = char.ToUpper(commandName[0]) + commandName[1..];
        var stepNumber = 0;
        var totalSteps = steps.Count;

        AnsiConsole.MarkupLine($"[bold]Starting {commandName}...[/]");
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
                // Docs updates rely on Claude CLI and are helpful but non-essential to publishing.
                // Treat failures as non-fatal so releases can proceed in environments without Claude.
                if (step.Id == "docs")
                {
                    AnsiConsole.MarkupLine($"[yellow]Step '{step.Label}' failed - continuing.[/]");
                    AnsiConsole.WriteLine();
                    continue;
                }

                AnsiConsole.MarkupLine($"[red]Step '{step.Label}' failed.[/]");
                return result;
            }

            AnsiConsole.WriteLine();
        }

        // Show summary.
        static string Esc(string value) => Markup.Escape(value);

        var finishedAt = DateTimeOffset.Now;
        var newVersion = GetCurrentVersion(repoRoot);
        var commitHash = await _git.GetCurrentCommitShortAsync();
        var branch = await _git.GetCurrentBranchAsync();

        var stepList = string.Join(", ", steps.Select(s => s.Id));
        var bumpApplied = steps.Any(s => s.Id == "bump");
        var bumpText = bumpApplied
            ? $"{originalVersion} -> {newVersion} ({requestedBumpType.ToString().ToLowerInvariant()})"
            : $"skipped (requested: {requestedBumpType.ToString().ToLowerInvariant()})";

        var publishRan = steps.Any(s => s.Id == "publish");
        var profilesUsed = publishRan ? "publish" : "none";

        AnsiConsole.MarkupLine($"[green bold]{title} completed.[/]");
        AnsiConsole.MarkupLine($"  Repo: [cyan]{Esc(repoRoot)}[/]");
        AnsiConsole.MarkupLine($"  Steps: [cyan]{Esc(stepList)}[/]");
        AnsiConsole.MarkupLine($"  Bump: [cyan]{Esc(bumpText)}[/]");
        AnsiConsole.MarkupLine($"  Profiles used: [cyan]{Esc(profilesUsed)}[/]");
        AnsiConsole.MarkupLine($"  Version: [cyan]{Esc(newVersion)}[/]");
        AnsiConsole.MarkupLine($"  Commit: [cyan]{Esc(commitHash)}[/]");
        AnsiConsole.MarkupLine($"  Branch: [cyan]{Esc(branch)}[/]");
        AnsiConsole.MarkupLine($"  Duration: [cyan]{Esc(sw.Elapsed.ToString())}[/]");
        AnsiConsole.MarkupLine($"  Started: [cyan]{Esc(startedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"))}[/]");
        AnsiConsole.MarkupLine($"  Finished: [cyan]{Esc(finishedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"))}[/]");
        AnsiConsole.MarkupLine($"  Runtime: [cyan]{Esc(RuntimeInformation.FrameworkDescription)}[/] on [cyan]{Esc(RuntimeInformation.OSDescription.Trim())}[/] ([cyan]{Esc(RuntimeInformation.ProcessArchitecture.ToString())}[/])");

        return 0;
    }

    private async Task<int> ExecuteCommitAsync(string repoRoot)
    {
        var commitCommand = new CommitCommand(_runner, _logger);
        return await commitCommand.ExecuteAsync(autoConfirm: true);
    }

    private async Task<int> ExecuteDocsUpdateAsync(string repoRoot)
    {
        var docsCommand = new DocsCommand(_runner, _logger);
        return await docsCommand.ExecuteAsync(autoCommit: true);
    }

    private async Task<int> ExecuteBumpAsync(BumpType bumpType, string repoRoot)
    {
        var bumpCommand = new BumpCommand(_runner, _logger);
        return await bumpCommand.ExecuteAsync(bumpType, autoConfirm: true);
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
        AnsiConsole.MarkupLine("Running: ando run -p publish --dind --read-env");
        AnsiConsole.WriteLine();

        var result = await _runner.RunAsync("ando", "run -p publish --dind --read-env", timeoutMs: 600000, streamOutput: true);
        return result.ExitCode;
    }

    private async Task<int> ExecuteBuildVerificationAsync()
    {
        AnsiConsole.MarkupLine("[bold]Build Verification[/]");
        AnsiConsole.MarkupLine("──────────────────");
        AnsiConsole.MarkupLine("Running: ando run --dind --read-env");
        AnsiConsole.WriteLine();

        var result = await _runner.RunAsync("ando", "run --dind --read-env", timeoutMs: 600000, streamOutput: true);

        if (result.ExitCode == 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[green]Build verification passed.[/]");
            AnsiConsole.WriteLine();
        }

        return result.ExitCode;
    }

    private static bool HasPublishProfile(string repoRoot)
    {
        var buildScript = Path.Combine(repoRoot, "build.csando");
        if (!File.Exists(buildScript))
            return false;

        var content = File.ReadAllText(buildScript);
        return content.Contains("DefineProfile(\"publish\"") ||
               content.Contains("DefineProfile('publish'");
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

    private void ShowDryRun(List<ReleaseStep> steps, string version, BumpType bumpType, string commandName = "release")
    {
        var title = char.ToUpper(commandName[0]) + commandName[1..];
        AnsiConsole.MarkupLine($"[bold]ANDO {title} Workflow (dry run)[/]");
        AnsiConsole.MarkupLine(new string('─', $"ANDO {title} Workflow (dry run)".Length));
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
