// =============================================================================
// GitHubOperations.cs
//
// Summary: Provides GitHub-specific operations for build scripts.
//
// GitHubOperations uses the GitHub CLI (gh) to interact with GitHub, including
// creating pull requests, releases, and pushing Docker images to ghcr.io.
//
// Example usage in build.csando:
//   var release = DefineProfile("release");
//   if (release) {
//       GitHub.CreateRelease(o => o.WithTag("v1.0.0").WithNotes("Release notes"));
//       GitHub.PushImage("myapp", o => o.WithTag("v1.0.0"));
//   }
//
// Authentication:
// - Uses GITHUB_TOKEN/GH_TOKEN environment variable if set
// - Falls back to extracting token from ~/.config/gh/hosts.yml
// - Token is passed to containers via GITHUB_TOKEN env var
//
// Design Decisions:
// - Uses gh CLI for GitHub API interactions (simpler than direct API calls)
// - Authentication helper provides unified token resolution
// - Docker commands use standard docker CLI with ghcr.io registry
// - gh CLI commands (CreatePr, CreateRelease) run on host via ProcessRunner
// - Docker commands (PushImage) run in container via ExecutorFactory
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.Steps;
using Ando.Utilities;

namespace Ando.Operations;

/// <summary>
/// GitHub-specific operations for pull requests, releases, and container registry.
/// Methods register steps in the workflow rather than executing immediately.
/// </summary>
public class GitHubOperations(
    StepRegistry registry,
    IBuildLogger logger,
    Func<ICommandExecutor> executorFactory,
    GitHubAuthHelper authHelper)
    : OperationsBase(registry, logger, executorFactory)
{
    private readonly GitHubAuthHelper _authHelper = authHelper;

    // Host executor for gh CLI commands (CreatePr, CreateRelease).
    // These must run on the host where gh CLI is installed, not in the container.
    private readonly ICommandExecutor _hostExecutor = new ProcessRunner(logger);

    /// <summary>
    /// Creates a pull request on GitHub.
    /// </summary>
    /// <param name="configure">Configuration for the pull request.</param>
    public void CreatePr(Action<GitHubPrOptions> configure)
    {
        var options = new GitHubPrOptions();
        configure(options);

        Registry.Register("GitHub.CreatePr", async () =>
        {
            var args = new ArgumentBuilder()
                .Add("pr", "create")
                .AddIfNotNull("--title", options.Title)
                .AddIfNotNull("--body", options.Body)
                .AddIfNotNull("--base", options.Base)
                .AddIfNotNull("--head", options.Head)
                .AddFlag(options.Draft, "--draft")
                .Build();

            var commandOptions = new CommandOptions();
            foreach (var (key, value) in _authHelper.GetEnvironment())
            {
                commandOptions.Environment[key] = value;
            }

            // Run on host where gh CLI is installed.
            var result = await _hostExecutor.ExecuteAsync("gh", args, commandOptions);

            if (!result.Success)
            {
                Logger.Error($"Failed to create PR: {result.Error}");
                return false;
            }

            // Log the PR URL from output.
            var prUrl = result.Output?.Trim();
            if (!string.IsNullOrEmpty(prUrl))
            {
                Logger.Info($"Created PR: {prUrl}");
            }

            return true;
        }, options.Title ?? "PR");
    }

    /// <summary>
    /// Creates a GitHub release with optional file uploads.
    /// </summary>
    /// <param name="configure">Configuration for the release.</param>
    public void CreateRelease(Action<GitHubReleaseOptions> configure)
    {
        var options = new GitHubReleaseOptions();
        configure(options);

        Registry.Register("GitHub.CreateRelease", async () =>
        {
            var tag = options.Tag;
            if (string.IsNullOrEmpty(tag))
            {
                Logger.Error("Release tag is required");
                return false;
            }

            // Add 'v' prefix if not present and not disabled.
            if (!options.NoPrefix && !tag.StartsWith("v"))
            {
                tag = $"v{tag}";
            }

            var commandOptions = new CommandOptions
            {
                // Set working directory to ensure relative paths work correctly.
                // When running on host, we need to be in the project root.
                WorkingDirectory = Environment.CurrentDirectory
            };
            foreach (var (key, value) in _authHelper.GetEnvironment())
            {
                commandOptions.Environment[key] = value;
            }

            // Get the repository path for --repo flag.
            // This is needed because gh CLI runs on host and may not detect the repo correctly.
            var repo = await GetGitHubRepoAsync();
            if (string.IsNullOrEmpty(repo))
            {
                Logger.Error("Could not determine GitHub repository from git remote");
                return false;
            }

            // Step 1: Create the release without files first.
            // This is more reliable than uploading files during creation.
            var createArgs = new ArgumentBuilder()
                .Add("release", "create", tag)
                .Add("--repo", repo)
                .AddIfNotNull("--title", options.Title ?? tag)
                .AddIfNotNull("--notes", options.Notes)
                .AddFlag(options.Draft, "--draft")
                .AddFlag(options.Prerelease, "--prerelease")
                .AddFlag(options.GenerateNotes, "--generate-notes")
                .Build();

            // Run on host where gh CLI is installed.
            var createResult = await _hostExecutor.ExecuteAsync("gh", createArgs, commandOptions);

            if (!createResult.Success)
            {
                Logger.Error($"Failed to create release: {createResult.Error}");
                return false;
            }

            Logger.Info($"Created release: {tag}");

            // Step 2: Upload files separately using gh release upload.
            // This is more reliable than including files in the create command.
            if (options.Files.Count > 0)
            {
                // Poll GitHub to ensure the release is ready before uploading.
                // The release API may take a moment to be fully available after creation.
                const int maxAttempts = 30;
                const int delayMs = 1000;
                var releaseReady = false;

                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    var checkArgs = new[] { "release", "view", tag, "--repo", repo };
                    var checkResult = await _hostExecutor.ExecuteAsync("gh", checkArgs, commandOptions);

                    if (checkResult.Success)
                    {
                        releaseReady = true;
                        break;
                    }

                    Logger.Debug($"Waiting for release to be ready... (attempt {attempt}/{maxAttempts})");
                    await Task.Delay(delayMs);
                }

                if (!releaseReady)
                {
                    Logger.Error($"Release {tag} not ready after {maxAttempts} seconds");
                    return false;
                }

                // Upload files one at a time for better reliability.
                // Files with the same name are renamed based on their parent directory.
                var uploadedCount = 0;
                var tempDir = Path.Combine(Path.GetTempPath(), $"ando-release-{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    foreach (var file in options.Files)
                    {
                        // Parse the file path - may include #label suffix
                        var filePath = file.Contains('#') ? file.Split('#')[0] : file;
                        var fullPath = Path.IsPathRooted(filePath)
                            ? filePath
                            : Path.Combine(commandOptions.WorkingDirectory ?? Environment.CurrentDirectory, filePath);

                        if (!File.Exists(fullPath))
                        {
                            Logger.Error($"File not found: {fullPath}");
                            return false;
                        }

                        // Generate unique name: ando-{parentdir}.exe or ando-{parentdir}
                        var fileName = Path.GetFileName(fullPath);
                        var parentDir = Path.GetFileName(Path.GetDirectoryName(fullPath) ?? "");
                        var extension = Path.GetExtension(fileName);
                        var baseName = Path.GetFileNameWithoutExtension(fileName);
                        var uniqueName = string.IsNullOrEmpty(parentDir)
                            ? fileName
                            : $"{baseName}-{parentDir}{extension}";

                        // Copy to temp directory with unique name
                        var tempPath = Path.Combine(tempDir, uniqueName);
                        File.Copy(fullPath, tempPath, overwrite: true);

                        var uploadArgs = new[] { "release", "upload", tag, "--repo", repo, tempPath };
                        var uploadResult = await _hostExecutor.ExecuteAsync("gh", uploadArgs, commandOptions);

                        if (!uploadResult.Success)
                        {
                            Logger.Error($"Failed to upload {uniqueName}: {uploadResult.Error}");
                            return false;
                        }
                        uploadedCount++;
                        Logger.Debug($"Uploaded {uploadedCount}/{options.Files.Count}: {uniqueName}");
                    }

                    Logger.Info($"Uploaded {options.Files.Count} asset(s) to release {tag}");
                }
                finally
                {
                    // Clean up temp directory
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                }
            }

            return true;
        }, options.Tag ?? "release");
    }

    /// <summary>
    /// Pushes a Docker image to GitHub Container Registry (ghcr.io).
    /// The image must already be built and tagged locally.
    /// </summary>
    /// <param name="imageName">The local image name (without registry prefix).</param>
    /// <param name="configure">Configuration for the push.</param>
    public void PushImage(string imageName, Action<GitHubImageOptions>? configure = null)
    {
        var options = new GitHubImageOptions();
        configure?.Invoke(options);

        Registry.Register("GitHub.PushImage", async () =>
        {
            var tag = options.Tag ?? "latest";

            // Determine the owner (required for ghcr.io).
            var owner = options.Owner;
            if (string.IsNullOrEmpty(owner))
            {
                // Try to get owner from git remote.
                owner = await GetGitHubOwnerAsync();
                if (string.IsNullOrEmpty(owner))
                {
                    Logger.Error("GitHub owner is required. Set via WithOwner() or configure git remote.");
                    return false;
                }
            }

            var localImage = $"{imageName}:{tag}";
            var remoteImage = $"ghcr.io/{owner.ToLowerInvariant()}/{imageName}:{tag}";

            // Check for required scope before attempting push.
            if (!_authHelper.HasScope("write:packages"))
            {
                Logger.Error("GitHub authentication missing 'write:packages' scope required for pushing to ghcr.io.");
                Logger.Error("Re-authenticate with: gh auth login --scopes write:packages");
                return false;
            }

            // Get authentication token.
            var token = _authHelper.GetToken();
            if (string.IsNullOrEmpty(token))
            {
                Logger.Error("GitHub token required for pushing to ghcr.io");
                return false;
            }

            // Login to ghcr.io using bash to pipe the token.
            Logger.Debug("Logging in to ghcr.io...");
            var loginScript = $"echo '{token}' | docker login ghcr.io -u {owner} --password-stdin";
            var loginResult = await ExecutorFactory().ExecuteAsync("bash", ["-c", loginScript]);

            if (!loginResult.Success)
            {
                Logger.Error($"Docker login to ghcr.io failed: {loginResult.Error}");
                return false;
            }

            // Tag the local image for ghcr.io.
            Logger.Debug($"Tagging {localImage} as {remoteImage}");
            var tagResult = await ExecutorFactory().ExecuteAsync("docker",
                ["tag", localImage, remoteImage]);

            if (!tagResult.Success)
            {
                Logger.Error($"Failed to tag image: {tagResult.Error}");
                return false;
            }

            // Push to ghcr.io.
            Logger.Info($"Pushing to {remoteImage}...");
            var pushResult = await ExecutorFactory().ExecuteAsync("docker",
                ["push", remoteImage]);

            if (!pushResult.Success)
            {
                Logger.Error($"Failed to push image: {pushResult.Error}");
                return false;
            }

            Logger.Info($"Pushed: {remoteImage}");
            return true;
        }, $"{imageName}:{options.Tag ?? "latest"}");
    }

    // Attempts to get the GitHub owner from the git remote URL.
    private async Task<string?> GetGitHubOwnerAsync()
    {
        try
        {
            var result = await ExecutorFactory().ExecuteAsync("git",
                ["remote", "get-url", "origin"]);

            if (!result.Success || string.IsNullOrEmpty(result.Output))
            {
                return null;
            }

            var url = result.Output.Trim();

            // Parse owner from various URL formats:
            // https://github.com/owner/repo.git
            // git@github.com:owner/repo.git
            if (url.Contains("github.com"))
            {
                var parts = url
                    .Replace("https://github.com/", "")
                    .Replace("git@github.com:", "")
                    .Split('/');

                if (parts.Length >= 1)
                {
                    return parts[0];
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    // Gets the full GitHub repository path (owner/repo) from git remote.
    // Uses host executor since this runs on the host where .git is located.
    private async Task<string?> GetGitHubRepoAsync()
    {
        try
        {
            var result = await _hostExecutor.ExecuteAsync("git",
                ["remote", "get-url", "origin"]);

            if (!result.Success || string.IsNullOrEmpty(result.Output))
            {
                return null;
            }

            var url = result.Output.Trim();

            // Parse owner/repo from various URL formats:
            // https://github.com/owner/repo.git
            // git@github.com:owner/repo.git
            if (url.Contains("github.com"))
            {
                var repoPath = url
                    .Replace("https://github.com/", "")
                    .Replace("git@github.com:", "")
                    .TrimEnd('/');

                // Remove .git suffix if present.
                if (repoPath.EndsWith(".git"))
                {
                    repoPath = repoPath[..^4];
                }

                return repoPath;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Options for creating a GitHub pull request.</summary>
public class GitHubPrOptions
{
    /// <summary>Title of the pull request.</summary>
    public string? Title { get; private set; }

    /// <summary>Body/description of the pull request.</summary>
    public string? Body { get; private set; }

    /// <summary>Base branch to merge into (default: main).</summary>
    public string? Base { get; private set; }

    /// <summary>Head branch containing the changes.</summary>
    public string? Head { get; private set; }

    /// <summary>Create as draft PR.</summary>
    public bool Draft { get; private set; }

    /// <summary>Sets the PR title.</summary>
    public GitHubPrOptions WithTitle(string title)
    {
        Title = title;
        return this;
    }

    /// <summary>Sets the PR body/description.</summary>
    public GitHubPrOptions WithBody(string body)
    {
        Body = body;
        return this;
    }

    /// <summary>Sets the base branch.</summary>
    public GitHubPrOptions WithBase(string baseBranch)
    {
        Base = baseBranch;
        return this;
    }

    /// <summary>Sets the head branch.</summary>
    public GitHubPrOptions WithHead(string headBranch)
    {
        Head = headBranch;
        return this;
    }

    /// <summary>Creates as a draft PR.</summary>
    public GitHubPrOptions AsDraft()
    {
        Draft = true;
        return this;
    }
}

/// <summary>Options for creating a GitHub release.</summary>
public class GitHubReleaseOptions
{
    /// <summary>Tag name for the release.</summary>
    public string? Tag { get; private set; }

    /// <summary>Release title.</summary>
    public string? Title { get; private set; }

    /// <summary>Release notes.</summary>
    public string? Notes { get; private set; }

    /// <summary>Create as draft release.</summary>
    public bool Draft { get; private set; }

    /// <summary>Mark as pre-release.</summary>
    public bool Prerelease { get; private set; }

    /// <summary>Auto-generate release notes.</summary>
    public bool GenerateNotes { get; private set; }

    /// <summary>Don't add 'v' prefix to tag.</summary>
    public bool NoPrefix { get; private set; }

    /// <summary>Files to upload as release assets.</summary>
    public List<string> Files { get; private set; } = [];

    /// <summary>Sets the tag name.</summary>
    public GitHubReleaseOptions WithTag(string tag)
    {
        Tag = tag;
        return this;
    }

    /// <summary>Sets the release title.</summary>
    public GitHubReleaseOptions WithTitle(string title)
    {
        Title = title;
        return this;
    }

    /// <summary>Sets the release notes.</summary>
    public GitHubReleaseOptions WithNotes(string notes)
    {
        Notes = notes;
        return this;
    }

    /// <summary>Creates as a draft release.</summary>
    public GitHubReleaseOptions AsDraft()
    {
        Draft = true;
        return this;
    }

    /// <summary>Marks as a pre-release.</summary>
    public GitHubReleaseOptions AsPrerelease()
    {
        Prerelease = true;
        return this;
    }

    /// <summary>Auto-generates release notes from commits.</summary>
    public GitHubReleaseOptions WithGeneratedNotes()
    {
        GenerateNotes = true;
        return this;
    }

    /// <summary>Don't add 'v' prefix to tag.</summary>
    public GitHubReleaseOptions WithoutPrefix()
    {
        NoPrefix = true;
        return this;
    }

    /// <summary>Adds files to upload as release assets.</summary>
    public GitHubReleaseOptions WithFiles(params string[] files)
    {
        Files.AddRange(files);
        return this;
    }
}

/// <summary>Options for pushing a Docker image to GitHub Container Registry.</summary>
public class GitHubImageOptions
{
    /// <summary>Image tag.</summary>
    public string? Tag { get; private set; }

    /// <summary>GitHub owner (user or organization).</summary>
    public string? Owner { get; private set; }

    /// <summary>Sets the image tag.</summary>
    public GitHubImageOptions WithTag(string tag)
    {
        Tag = tag;
        return this;
    }

    /// <summary>Sets the GitHub owner (user or organization).</summary>
    public GitHubImageOptions WithOwner(string owner)
    {
        Owner = owner;
        return this;
    }
}
