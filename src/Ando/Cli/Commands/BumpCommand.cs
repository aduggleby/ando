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
    /// <returns>Exit code: 0 for success, 1 for errors.</returns>
    public async Task<int> ExecuteAsync(BumpType type = BumpType.Patch)
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
                _logger.Error("Error: You have uncommitted changes.");
                Console.WriteLine();

                var changedFiles = await _git.GetChangedFilesAsync();
                foreach (var file in changedFiles)
                {
                    Console.WriteLine($"  {file}");
                }
                Console.WriteLine();

                Console.Write("Run 'ando commit' to commit them first? [Y/n] ");
                var response = Console.ReadLine()?.Trim().ToLowerInvariant();

                if (response == "n" || response == "no")
                {
                    return 1;
                }

                // Run ando commit.
                var commitCommand = new CommitCommand(_runner, _logger);
                var commitResult = await commitCommand.ExecuteAsync();
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

            // Step 8: Generate changelog entry using Claude.
            var changelogEntry = await GenerateChangelogEntryAsync(baseVersion, newVersion);

            // Step 9: Update documentation (changelog, version badges).
            Console.WriteLine();
            _logger.Info("Updating documentation:");

            var docUpdater = new DocumentationUpdater(repoRoot);
            var docResults = docUpdater.UpdateDocumentation(baseVersion, newVersion, changelogEntry);

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
    /// Generates a changelog entry using Claude based on commit messages and changed files.
    /// Tries to find a git tag matching the version (vX.Y.Z or X.Y.Z format).
    /// If no tag is found, prompts the user for a changelog entry.
    /// </summary>
    private async Task<IReadOnlyList<string>?> GenerateChangelogEntryAsync(string currentVersion, string newVersion)
    {
        // Try both "vX.Y.Z" and "X.Y.Z" tag formats.
        var tagFormats = new[] { $"v{currentVersion}", currentVersion };

        foreach (var tag in tagFormats)
        {
            if (await _git.TagExistsAsync(tag))
            {
                var messages = await _git.GetCommitMessagesSinceTagAsync(tag);
                var changedFiles = await _git.GetChangedFilesSinceTagAsync(tag);

                if (messages.Count > 0 || changedFiles.Count > 0)
                {
                    _logger.Info($"Changes since tag {tag}:");
                    Console.WriteLine($"  Commits: {messages.Count}");
                    Console.WriteLine($"  Files changed: {changedFiles.Count}");
                    Console.WriteLine();

                    return await GenerateChangelogWithClaudeAsync(messages, changedFiles, newVersion);
                }
            }
        }

        // No tag found or no commits since tag - ask user for changelog entry.
        Console.WriteLine();
        _logger.Warning($"No git tag found for version {currentVersion}.");
        Console.Write("Enter changelog entry (or press Enter for 'Version bump'): ");
        var userEntry = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(userEntry))
            return null;

        return [userEntry];
    }

    /// <summary>
    /// Calls Claude to generate a concise, non-technical changelog entry.
    /// </summary>
    private async Task<IReadOnlyList<string>?> GenerateChangelogWithClaudeAsync(
        List<string> commitMessages,
        List<string> changedFiles,
        string newVersion)
    {
        var commitList = string.Join("\n", commitMessages.Select(m => $"- {m}"));
        var fileList = string.Join("\n", changedFiles.Select(f => $"- {f}"));

        var prompt = $"""
            Generate a concise changelog entry for version {newVersion}.

            ## Commit Messages
            {commitList}

            ## Changed Files
            {fileList}

            ## Instructions
            - Write 1-3 bullet points describing the changes
            - Use simple, non-technical language that end users can understand
            - Focus on what changed from a user's perspective, not implementation details
            - Start each bullet with a verb (Add, Fix, Improve, Update, etc.)
            - Do NOT include version numbers in the bullet points
            - Do NOT include file names or technical jargon
            - If there are only internal/maintenance changes, summarize as "Internal improvements"

            ## Output Format
            Return ONLY the bullet points, one per line, starting with "- ".
            Example:
            - Add support for custom build profiles
            - Fix issue with version detection on Windows
            - Improve error messages for missing dependencies
            """;

        _logger.Info("Generating changelog with Claude...");

        try
        {
            var result = await _runner.RunClaudeAsync(prompt, timeoutMs: 60000, streamOutput: false);

            if (string.IsNullOrWhiteSpace(result))
            {
                _logger.Warning("Claude returned empty response, using default.");
                return null;
            }

            // Parse the response - extract lines starting with "-".
            var entries = result
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.TrimStart().StartsWith("-"))
                .Select(line => line.TrimStart().TrimStart('-').Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (entries.Count == 0)
            {
                _logger.Warning("Could not parse Claude response, using default.");
                return null;
            }

            Console.WriteLine();
            _logger.Info("Generated changelog entry:");
            foreach (var entry in entries)
            {
                Console.WriteLine($"  • {entry}");
            }
            Console.WriteLine();

            return entries;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to generate changelog with Claude: {ex.Message}");
            return null;
        }
    }
}
