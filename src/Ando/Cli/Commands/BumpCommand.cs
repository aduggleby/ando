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
//
// Opt-in release mode (ando.config / flags), AI-free and safe for shared trees:
// - versionFiles replaces project detection; versionScheme selects semver or
//   year-minor-patch; --changelog template builds the changelog from commits
// - In template/none mode (or requireClean) a dirty tree is refused and only
//   the files bump changed are committed, by explicit path
// - --suggest, --dry-run, --no-commit and --tag work without any AI process
// =============================================================================

using Ando.Config;
using Ando.Hooks;
using Ando.Logging;
using Ando.Utilities;
using Ando.Versioning;
using static Ando.Versioning.ProjectDetector;

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
    private readonly TimeProvider _clock;

    public BumpCommand(CliProcessRunner runner, IBuildLogger logger, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
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
    /// <param name="options">Opt-in release options (flags); null keeps the original behavior.</param>
    /// <returns>Exit code: 0 for success, 1 for errors.</returns>
    public async Task<int> ExecuteAsync(BumpType type = BumpType.Patch, bool autoConfirm = false, BumpOptions? options = null)
    {
        options ??= new BumpOptions();
        try
        {
            // Step 1: Check for build.csando (not needed when ando.config lists versionFiles).
            var buildScript = FindBuildScript();
            var repoRoot = buildScript != null ? Path.GetDirectoryName(buildScript) ?? "." : Directory.GetCurrentDirectory();
            var config = ProjectConfig.Load(repoRoot);
            var useVersionFiles = config.VersionFiles is { Length: > 0 };

            if (buildScript == null && !useVersionFiles)
            {
                _logger.Error("Error: No build.csando found in current directory.");
                return 1;
            }

            var settingsError = ValidateSettings(config, options);
            if (settingsError != null)
            {
                _logger.Error($"Error: {settingsError}");
                return 1;
            }

            var changelogMode = options.Changelog ?? config.Changelog ?? "claude";
            var scheme = config.VersionScheme ?? VersionCalculator.SemVerScheme;
            var safeMode = config.IsSafeReleaseMode(options.Changelog);
            var preview = options.DryRun || options.Suggest;

            if (options.Suggest)
            {
                return await SuggestAsync(config);
            }

            // Fail fast on misconfigured version files, before any hook or prompt.
            if (useVersionFiles)
            {
                var fileError = ValidateVersionFiles(repoRoot, config.VersionFiles!);
                if (fileError != null)
                {
                    _logger.Error($"Error: {fileError}");
                    return 1;
                }
            }

            // Check for Claude permission (only a real Claude changelog run uses Claude).
            if (changelogMode == "claude" && !preview)
            {
                var claudeChecker = new ClaudePermissionChecker(_logger);
                var claudeResult = claudeChecker.CheckAndPrompt(repoRoot, "bump");
                if (!ClaudePermissionChecker.IsAllowed(claudeResult))
                {
                    return 1;
                }
            }

            // Initialize hook runner.
            var hookRunner = new HookRunner(repoRoot, _logger);
            var hookContext = new HookContext
            {
                Command = "bump",
                BumpType = type.ToString().ToLower()
            };

            // Run pre-hooks.
            if (!options.DryRun && !await hookRunner.RunHooksAsync(HookRunner.HookType.Pre, "bump", hookContext))
            {
                _logger.Error("Bump aborted by pre-hook.");
                return 1;
            }

            // Step 2: Check for uncommitted changes.
            if (safeMode)
            {
                // Safe mode never runs the AI commit: a dirty tree is refused (ANDO's own log is ignored).
                var dirty = (await _git.GetStatusAsync(excludeAndoFiles: true))
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (dirty.Length > 0)
                {
                    if (!preview)
                    {
                        _logger.Error("Error: The working tree has uncommitted changes. Commit or stash them first:");
                        foreach (var line in dirty)
                        {
                            Console.WriteLine($"  {line.TrimEnd('\r')}");
                        }
                        return 1;
                    }

                    _logger.Warning("You have uncommitted changes (a real bump would refuse).");
                }
            }
            else if (await _git.HasUncommittedChangesAsync())
            {
                _logger.Warning("You have uncommitted changes.");
                Console.WriteLine();

                var changedFiles = await _git.GetChangedFilesAsync();
                foreach (var file in changedFiles)
                {
                    Console.WriteLine($"  {file}");
                }
                Console.WriteLine();

                if (!preview)
                {
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
            }

            // Step 3: Detect projects (or take them from versionFiles).
            List<DetectedProject> projects;
            if (useVersionFiles)
            {
                projects = config.VersionFiles!.Select(f => new DetectedProject(f, GetFileType(f))).ToList();
            }
            else
            {
                _logger.Info("Detecting projects...");
                projects = _detector.DetectProjects(buildScript!);
            }

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
            _logger.Info(useVersionFiles ? "Version files:" : "Detected projects:");
            var projectVersions = new List<(DetectedProject Project, string Version)>();

            foreach (var project in projects)
            {
                var fullPath = Path.Combine(repoRoot, project.Path);
                if (useVersionFiles && !File.Exists(fullPath))
                {
                    _logger.Error($"Error: Version file not found: {project.Path}");
                    return 1;
                }

                var version = _reader.ReadVersion(fullPath, project.Type, strictMsBuild: useVersionFiles);

                if (version == null)
                {
                    if (useVersionFiles)
                    {
                        _logger.Error($"Error: No version found in {project.Path} " +
                            (project.Type == ProjectType.Npm ? "(expected a \"version\" field)." : "(expected a <Version> element)."));
                        return 1;
                    }

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

            if (distinctVersions.Count > 1 && useVersionFiles)
            {
                _logger.Error("Error: Version files hold different versions: " +
                    string.Join(", ", projectVersions.Select(pv => $"{pv.Project.Path} {pv.Version}")));
                return 1;
            }

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
            var now = _clock.GetUtcNow().UtcDateTime;
            var newVersion = options.SetVersion != null
                ? VersionCalculator.ValidateExplicit(options.SetVersion, baseVersion, scheme, now)
                : VersionCalculator.Next(baseVersion, type, scheme, now);

            var message = ReleaseMessageTemplate(config).Replace("{version}", newVersion);
            var tagName = options.Tag ? $"{config.TagPrefix ?? "v"}{newVersion}" : null;
            var changelogFile = config.ChangelogFile ?? "CHANGELOG.md";

            Console.WriteLine();
            _logger.Info(options.SetVersion != null
                ? $"Setting version: {baseVersion} → {newVersion}"
                : $"Bumping {type.ToString().ToLower()}: {baseVersion} → {newVersion}");
            Console.WriteLine();

            string? changelogSection = null;
            if (changelogMode == "template")
            {
                var (_, commits) = await GetTemplateCommitsAsync(config);
                changelogSection = ChangelogGenerator.RenderSection(newVersion, now, commits, ReleaseMessageTemplate(config));
            }

            // An existing release tag (at HEAD or elsewhere) is refused before anything is written.
            if (tagName != null && await _git.GetTagCommitAsync(tagName) != null)
            {
                _logger.Error($"Error: Tag {tagName} already exists.");
                return 1;
            }

            if (options.DryRun)
            {
                PrintDryRun(projectVersions.Select(pv => pv.Project.Path), changelogMode, changelogFile,
                    changelogSection, options.NoCommit ? null : message, tagName);
                return 0;
            }

            // Step 7: Update all project files.
            _logger.Info("Updating project versions:");
            var updatedFiles = new List<string>();

            foreach (var (project, _) in projectVersions)
            {
                var fullPath = Path.Combine(repoRoot, project.Path);
                _writer.WriteVersion(fullPath, project.Type, newVersion, strictMsBuild: useVersionFiles);
                Console.WriteLine($"  ✓ {project.Path}");
                updatedFiles.Add(project.Path);
            }

            // Step 8: Update changelog files.
            if (changelogSection != null)
            {
                var changelogPath = Path.Combine(repoRoot, changelogFile);
                var existing = File.Exists(changelogPath) ? File.ReadAllText(changelogPath) : null;
                File.WriteAllText(changelogPath, ChangelogGenerator.Insert(existing, changelogSection));
                Console.WriteLine($"  ✓ {changelogFile}");
                updatedFiles.Add(changelogFile);
            }
            else if (changelogMode == "claude")
            {
                await UpdateChangelogsAsync(baseVersion, newVersion, autoConfirm);
            }

            // Step 9: Update version badges in documentation (not with versionFiles).
            if (!useVersionFiles)
            {
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
            }

            // Step 10: Commit changes.
            Console.WriteLine();
            if (options.NoCommit)
            {
                _logger.Info("Files updated; not committed (--no-commit).");
            }
            else if (safeMode)
            {
                // Only the files bump changed, by explicit path.
                await _git.StagePathsAsync(updatedFiles);
                await _git.CommitPathsAsync(message, updatedFiles);
                _logger.Info($"Committed: {message}");
            }
            else
            {
                await _git.StageFilesAsync(updatedFiles.Select(f => Path.Combine(repoRoot, f)));
                await _git.CommitAsync(message);
                _logger.Info($"Committed: {message}");
            }

            if (tagName != null)
            {
                await _git.CreateAnnotatedTagAsync(tagName, message);
                _logger.Info($"Tagged: {tagName}");
            }

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
    /// Checks the opt-in settings; returns an error message or null.
    /// </summary>
    private static string? ValidateSettings(ProjectConfig config, BumpOptions options)
    {
        if (config.VersionScheme != null && !VersionCalculator.IsKnownScheme(config.VersionScheme))
            return $"Invalid versionScheme '{config.VersionScheme}' in ando.config. Use semver or year-minor-patch.";
        if (config.Changelog != null && !BumpOptions.ChangelogModes.Contains(config.Changelog))
            return $"Invalid changelog '{config.Changelog}' in ando.config. Use template, claude or none.";
        if (options.Tag && options.NoCommit)
            return "--tag cannot be used with --no-commit.";
        if (config.CommitMessage != null && string.IsNullOrWhiteSpace(config.CommitMessage.Replace("{version}", "")))
            return "commitMessage in ando.config needs text besides {version}, for example \"chore(release): {version}\".";
        return null;
    }

    private static string ReleaseMessageTemplate(ProjectConfig config)
        => config.CommitMessage ?? "Bump version to {version}";

    /// <summary>
    /// Checks every versionFiles entry (supported type, exists, exactly one version); returns an error or null.
    /// </summary>
    private string? ValidateVersionFiles(string repoRoot, IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            try
            {
                var type = GetFileType(file);
                var fullPath = Path.Combine(repoRoot, file);
                if (!File.Exists(fullPath))
                    return $"Version file not found: {file}";

                if (_reader.ReadVersion(fullPath, type, strictMsBuild: true) == null)
                    return $"No version found in {file} (expected a \"version\" field).";
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return ex.Message;
            }
        }

        return null;
    }

    /// <summary>
    /// package.json holds a "version" field; every other version file is an XML MSBuild file.
    /// </summary>
    internal static ProjectType GetFileType(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("package.json", StringComparison.OrdinalIgnoreCase))
            return ProjectType.Npm;

        string[] xmlExtensions = [".props", ".csproj", ".targets", ".fsproj", ".vbproj", ".proj"];
        if (!xmlExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Unsupported version file '{path}'. Use package.json or an MSBuild file (.props, .csproj, .targets).");
        return ProjectType.Dotnet;
    }

    /// <summary>
    /// Commits since the last release tag matching tagPattern (all history without one),
    /// limited to changelogPaths, with release commits removed.
    /// </summary>
    private async Task<(string? Tag, List<(string Sha, string Subject)> Commits)> GetTemplateCommitsAsync(ProjectConfig config)
    {
        var tag = await _git.GetLastTagMatchingAsync(config.TagPattern ?? "v*");
        var commits = await _git.GetCommitLogAsync(tag, config.ChangelogPaths);
        return (tag, ChangelogGenerator.Filter(commits, ReleaseMessageTemplate(config)));
    }

    /// <summary>
    /// --suggest: prints minor when a feat commit exists since the last tag, else patch.
    /// </summary>
    private async Task<int> SuggestAsync(ProjectConfig config)
    {
        var (tag, commits) = await GetTemplateCommitsAsync(config);
        Console.WriteLine(tag == null
            ? $"Commits considered (no tag, all history): {commits.Count}"
            : $"Commits considered since {tag}: {commits.Count}");
        foreach (var (sha, subject) in commits)
        {
            Console.WriteLine($"  {sha} {subject}");
        }

        Console.WriteLine(commits.Any(c => ChangelogGenerator.GetType(c.Subject) == "feat") ? "minor" : "patch");
        return 0;
    }

    private static void PrintDryRun(IEnumerable<string> versionFiles, string changelogMode, string changelogFile,
        string? changelogSection, string? message, string? tagName)
    {
        Console.WriteLine("Dry run: nothing is changed.");
        Console.WriteLine("Files that would change:");
        foreach (var file in versionFiles)
        {
            Console.WriteLine($"  {file}");
        }

        if (changelogSection != null)
        {
            Console.WriteLine($"  {changelogFile}");
            Console.WriteLine();
            Console.WriteLine("Changelog entry:");
            Console.Write(changelogSection);
        }
        else if (changelogMode == "claude")
        {
            Console.WriteLine("  (changelog files would be updated by Claude)");
        }

        Console.WriteLine();
        Console.WriteLine(message != null ? $"Commit message: {message}" : "No commit (--no-commit).");
        if (tagName != null)
        {
            Console.WriteLine($"Tag: {tagName}");
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
