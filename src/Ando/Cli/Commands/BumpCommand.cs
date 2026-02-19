// =============================================================================
// BumpCommand.cs
//
// Summary: CLI command handler for 'ando bump'.
//
// This command detects projects from build.csando, bumps their versions,
// updates documentation, and commits the changes. It handles version
// mismatches and integrates with ando commit for uncommitted changes.
//
// Design Decisions:
// - Detects projects from build.csando using pattern matching
// - Validates all versions match before bumping (or prompts to select base)
// - Updates both project files and documentation (changelog, badges)
// - Commits all changes automatically with "Bump version to X.Y.Z" message
// - Integrates with ando commit if there are uncommitted changes
// =============================================================================

using Ando.Hooks;
using Ando.Logging;
using Ando.Utilities;
using Ando.Versioning;

namespace Ando.Cli.Commands;

/// <summary>
/// CLI command handler for 'ando bump'. Orchestrates version bumping across
/// all projects detected in build.csando.
/// </summary>
public class BumpCommand
{
    private readonly CliProcessRunner _runner;
    private readonly CliGitOperations _git;
    private readonly ProjectDetector _detector;
    private readonly VersionReader _reader;
    private readonly VersionWriter _writer;
    private readonly IBuildLogger _logger;

    public BumpCommand(CliProcessRunner runner, IBuildLogger logger)
    {
        _runner = runner;
        _git = new CliGitOperations(runner);
        _detector = new ProjectDetector();
        _reader = new VersionReader();
        _writer = new VersionWriter();
        _logger = logger;
    }

    /// <summary>
    /// Executes the bump command.
    /// </summary>
    /// <param name="type">Type of version bump (Patch, Minor, Major).</param>
    /// <param name="autoConfirm">Skip confirmation prompts and proceed automatically.</param>
    /// <returns>Exit code: 0 for success, 1 for errors.</returns>
    public async Task<int> ExecuteAsync(BumpType type = BumpType.Patch, bool autoConfirm = false)
    {
        try
        {
            // Step 1: Check for build.csando.
            var buildScript = FindBuildScript();
            if (buildScript == null)
            {
                _logger.Error("Error: No build.csando found in current directory.");
                return 1;
            }

            var repoRoot = Path.GetDirectoryName(buildScript) ?? ".";

            // Check for Claude permission (this command uses Claude for changelog generation).
            var claudeChecker = new ClaudePermissionChecker(_logger);
            var claudeResult = claudeChecker.CheckAndPrompt(repoRoot, "bump");
            if (!ClaudePermissionChecker.IsAllowed(claudeResult))
            {
                return 1;
            }

            // Initialize hook runner.
            var hookRunner = new HookRunner(repoRoot, _logger);
            var hookContext = new HookContext
            {
                Command = "bump",
                BumpType = type.ToString().ToLower()
            };

            // Run pre-hooks.
            if (!await hookRunner.RunHooksAsync(HookRunner.HookType.Pre, "bump", hookContext))
            {
                _logger.Error("Bump aborted by pre-hook.");
                return 1;
            }

            // Step 2: Check for uncommitted changes.
            if (await _git.HasUncommittedChangesAsync())
            {
                _logger.Warning("You have uncommitted changes.");
                Console.WriteLine();

                var changedFiles = await _git.GetChangedFilesAsync();
                foreach (var file in changedFiles)
                {
                    Console.WriteLine($"  {file}");
                }
                Console.WriteLine();

                if (!autoConfirm)
                {
                    Console.Write("Run 'ando commit' to commit them first? [Y/n] ");
                    var response = Console.ReadLine()?.Trim().ToLowerInvariant();

                    if (response == "n" || response == "no")
                    {
                        return 1;
                    }
                }

                // Run ando commit.
                _logger.Info("Running commit...");
                var commitCommand = new CommitCommand(_runner, _logger);
                var commitResult = await commitCommand.ExecuteAsync(autoConfirm);
                if (commitResult != 0)
                {
                    return commitResult;
                }

                Console.WriteLine();
            }

            // Step 3: Detect projects.
            _logger.Info("Detecting projects...");
            var projects = _detector.DetectProjects(buildScript);

            if (projects.Count == 0)
            {
                _logger.Error("Error: No projects found in build.csando.");
                _logger.Error("");
                _logger.Error("The bump command looks for:");
                _logger.Error("  - Dotnet.Project(\"path/to/project.csproj\")");
                _logger.Error("  - Directory(\"path\") used with Npm operations");
                return 1;
            }

            // Step 4: Read current versions.
            Console.WriteLine();
            _logger.Info("Detected projects:");
            var projectVersions = new List<(ProjectDetector.DetectedProject Project, string Version)>();

            foreach (var project in projects)
            {
                var fullPath = Path.Combine(repoRoot, project.Path);
                var version = _reader.ReadVersion(fullPath, project.Type);

                if (version == null)
                {
                    _logger.Warning($"  {project.Path}: no version found");
                    continue;
                }

                projectVersions.Add((project, version));
                Console.WriteLine($"  {project.Path,-40} {version}");
            }

            if (projectVersions.Count == 0)
            {
                _logger.Error("Error: No projects with versions found.");
                return 1;
            }

            // Step 5: Validate versions match.
            var distinctVersions = projectVersions.Select(pv => pv.Version).Distinct().ToList();
            string baseVersion;

            if (distinctVersions.Count > 1)
            {
                Console.WriteLine();
                _logger.Warning("Warning: Version mismatch detected.");

                if (autoConfirm)
                {
                    // Auto-select the highest version.
                    baseVersion = distinctVersions
                        .OrderByDescending(v => SemVer.TryParse(v, out var sv) ? sv : new SemVer(0, 0, 0))
                        .First();
                    _logger.Info($"Auto-selecting highest version: {baseVersion}");
                }
                else
                {
                    Console.WriteLine();

                    // Group by version for display.
                    var byVersion = projectVersions.GroupBy(pv => pv.Version);
                    var versionOptions = byVersion.Select((g, i) => $"  {i + 1}. {g.Key} ({string.Join(", ", g.Select(pv => Path.GetFileName(pv.Project.Path)))})").ToList();

                    Console.WriteLine("Which version should be used as the base?");
                    foreach (var option in versionOptions)
                    {
                        Console.WriteLine(option);
                    }

                    Console.Write($"Select [1-{distinctVersions.Count}]: ");
                    var selection = Console.ReadLine()?.Trim();

                    if (!int.TryParse(selection, out var selectedIndex) ||
                        selectedIndex < 1 ||
                        selectedIndex > distinctVersions.Count)
                    {
                        _logger.Error("Invalid selection.");
                        return 1;
                    }

                    baseVersion = distinctVersions[selectedIndex - 1];
                }
            }
            else
            {
                baseVersion = distinctVersions[0];
            }

            // Step 6: Calculate new version.
            var semver = SemVer.Parse(baseVersion);
            var newSemver = semver.Bump(type);
            var newVersion = newSemver.ToString();

            Console.WriteLine();
            _logger.Info($"Bumping {type.ToString().ToLower()}: {baseVersion} → {newVersion}");
            Console.WriteLine();

            // Step 7: Update all project files.
            _logger.Info("Updating project versions:");
            var updatedFiles = new List<string>();

            foreach (var (project, _) in projectVersions)
            {
                var fullPath = Path.Combine(repoRoot, project.Path);
                _writer.WriteVersion(fullPath, project.Type, newVersion);
                Console.WriteLine($"  ✓ {project.Path}");
                updatedFiles.Add(project.Path);
            }

            // Step 8: Update changelog files using Claude.
            await UpdateChangelogsAsync(baseVersion, newVersion, autoConfirm);

            // Step 9: Update version badges in documentation.
            Console.WriteLine();
            _logger.Info("Updating version badges:");

            var docUpdater = new DocumentationUpdater(repoRoot);
            var docResults = docUpdater.UpdateVersionBadges(baseVersion, newVersion);

            foreach (var result in docResults)
            {
                if (result.Success)
                {
                    Console.WriteLine($"  ✓ {result.FilePath}");
                    updatedFiles.Add(result.FilePath);
                }
                else if (result.Error != null)
                {
                    Console.WriteLine($"  ⚠ {result.FilePath}: {result.Error}");
                }
            }

            // Step 10: Commit changes.
            Console.WriteLine();
            await _git.StageFilesAsync(updatedFiles.Select(f => Path.Combine(repoRoot, f)));
            await _git.CommitAsync($"Bump version to {newVersion}");

            _logger.Info($"Committed: Bump version to {newVersion}");

            // Run post-hooks with updated context.
            hookContext = hookContext with
            {
                OldVersion = baseVersion,
                NewVersion = newVersion
            };
            await hookRunner.RunHooksAsync(HookRunner.HookType.Post, "bump", hookContext);

            return 0;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Finds build.csando in the current directory.
    /// </summary>
    private static string? FindBuildScript()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "build.csando");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Updates changelog files using Claude based on commit messages and changed files.
    /// Tries to find a git tag matching the version (vX.Y.Z or X.Y.Z format).
    /// If no exact tag is found, falls back to the most recent tag.
    /// </summary>
    private async Task UpdateChangelogsAsync(string currentVersion, string newVersion, bool autoConfirm)
    {
        async Task<bool> TryUpdateFromTagAsync(string tag, bool isFallback = false)
        {
            if (!await _git.TagExistsAsync(tag))
                return false;

            var messages = await _git.GetCommitMessagesSinceTagAsync(tag);
            var changedFiles = await _git.GetChangedFilesSinceTagAsync(tag);

            if (messages.Count > 0 || changedFiles.Count > 0)
            {
                if (isFallback)
                {
                    _logger.Info($"No exact tag found for {currentVersion}. Using most recent tag {tag}.");
                }

                _logger.Info($"Changes since tag {tag}:");
                Console.WriteLine($"  Commits: {messages.Count}");
                Console.WriteLine($"  Files changed: {changedFiles.Count}");
                Console.WriteLine();

                await UpdateChangelogsWithClaudeAsync(messages, changedFiles, newVersion);
                return true;
            }

            _logger.Info($"No changes since tag {tag}, skipping changelog update.");
            return true;
        }

        // Try both "vX.Y.Z" and "X.Y.Z" tag formats.
        var tagFormats = new[] { $"v{currentVersion}", currentVersion };

        foreach (var tag in tagFormats)
        {
            if (await TryUpdateFromTagAsync(tag))
                return;
        }

        var lastTag = await _git.GetLastTagAsync();
        if (!string.IsNullOrWhiteSpace(lastTag) && !tagFormats.Contains(lastTag, StringComparer.Ordinal))
        {
            if (await TryUpdateFromTagAsync(lastTag, isFallback: true))
                return;
        }

        // No tag found.
        _logger.Warning($"No git tag found for version {currentVersion} and no fallback tag found, skipping changelog update.");
    }

    /// <summary>
    /// Calls Claude to find changelog files and update them with a new version entry.
    /// Claude will search for changelog files and edit them directly.
    /// </summary>
    private async Task UpdateChangelogsWithClaudeAsync(
        List<string> commitMessages,
        List<string> changedFiles,
        string newVersion)
    {
        var commitList = string.Join("\n", commitMessages.Select(m => $"- {m}"));
        var fileList = string.Join("\n", changedFiles.Select(f => $"- {f}"));
        var date = DateTime.Now.ToString("yyyy-MM-dd");

        var prompt = $"""
            Update all changelog files in this repository for version {newVersion}.

            ## Commit Messages
            {commitList}

            ## Changed Files
            {fileList}

            ## Instructions

            1. **Find all changelog files** in this repository:
               - Look for CHANGELOG.md in the root directory
               - Look for changelog files in website/ directory (e.g., changelog.md, changelog.mdx, changelog.astro)
               - Check common locations like docs/, website/src/content/, website/src/pages/

            2. **For each changelog file found**, add a new version entry with:
               - Version header: ## {newVersion}
               - Date: **{date}**
               - As few concise bullet points as possible describing the changes

            3. **Writing the changelog entries**:
               - Use simple, non-technical language that end users can understand
               - Focus on what changed from a user's perspective, not implementation details
               - Start each bullet with a verb (Add, Fix, Improve, Update, etc.)
               - Do NOT include version numbers in the bullet points
               - Do NOT include file names or technical jargon
               - If there are only internal/maintenance changes, summarize as "Internal improvements"
               - Use the SAME changelog entries for all files (consistency)

            4. **Placement**: Insert the new entry after any YAML frontmatter but before existing entries.

            5. **Report**: After making changes, list which files you updated.

            If no changelog files are found, say so.
            """;

        _logger.Info("Claude is updating changelog files...");
        Console.WriteLine();

        try
        {
            await _runner.RunClaudeAsync(prompt, timeoutMs: 120000, streamOutput: true);
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to update changelogs with Claude: {ex.Message}");
        }
    }
}
