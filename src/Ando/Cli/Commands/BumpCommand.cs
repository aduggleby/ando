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

            // Step 8: Get commit messages for changelog.
            var commitMessages = await GetCommitMessagesSinceVersionAsync(baseVersion);

            // Step 9: Update documentation (changelog, version badges).
            Console.WriteLine();
            _logger.Info("Updating documentation:");

            var docUpdater = new DocumentationUpdater(repoRoot);
            var docResults = docUpdater.UpdateDocumentation(baseVersion, newVersion, commitMessages);

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
    /// Gets commit messages since the specified version.
    /// Tries to find a git tag matching the version (vX.Y.Z or X.Y.Z format).
    /// If no tag is found, prompts the user for a changelog entry.
    /// </summary>
    private async Task<IReadOnlyList<string>?> GetCommitMessagesSinceVersionAsync(string version)
    {
        // Try both "vX.Y.Z" and "X.Y.Z" tag formats.
        var tagFormats = new[] { $"v{version}", version };

        foreach (var tag in tagFormats)
        {
            if (await _git.TagExistsAsync(tag))
            {
                var messages = await _git.GetCommitMessagesSinceTagAsync(tag);
                if (messages.Count > 0)
                {
                    _logger.Info($"Found {messages.Count} commit(s) since tag {tag}");
                    return messages;
                }
            }
        }

        // No tag found or no commits since tag - ask user for changelog entry.
        Console.WriteLine();
        _logger.Warning($"No git tag found for version {version}.");
        Console.Write("Enter changelog entry (or press Enter for 'Version bump'): ");
        var userEntry = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(userEntry))
            return null;

        return [userEntry];
    }
}
