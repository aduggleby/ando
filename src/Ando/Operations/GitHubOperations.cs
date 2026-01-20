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
//       GitHub.CreateRelease(o => o.WithTag(version).WithNotes("Release notes"));
//       GitHub.PushImage("myapp", o => o.WithTag(version));
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
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.References;
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

            var result = await ExecutorFactory().ExecuteAsync("gh", args, commandOptions);

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
    /// Creates a GitHub release.
    /// </summary>
    /// <param name="configure">Configuration for the release.</param>
    public void CreateRelease(Action<GitHubReleaseOptions> configure)
    {
        var options = new GitHubReleaseOptions();
        configure(options);

        Registry.Register("GitHub.CreateRelease", async () =>
        {
            // Get the tag name, supporting VersionRef.
            var tag = options.TagRef?.Value ?? options.Tag;
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

            var args = new ArgumentBuilder()
                .Add("release", "create", tag)
                .AddIfNotNull("--title", options.Title ?? tag)
                .AddIfNotNull("--notes", options.Notes)
                .AddFlag(options.Draft, "--draft")
                .AddFlag(options.Prerelease, "--prerelease")
                .AddFlag(options.GenerateNotes, "--generate-notes")
                .Build();

            var commandOptions = new CommandOptions();
            foreach (var (key, value) in _authHelper.GetEnvironment())
            {
                commandOptions.Environment[key] = value;
            }

            var result = await ExecutorFactory().ExecuteAsync("gh", args, commandOptions);

            if (!result.Success)
            {
                Logger.Error($"Failed to create release: {result.Error}");
                return false;
            }

            Logger.Info($"Created release: {tag}");
            return true;
        }, options.Tag ?? options.TagRef?.ToString() ?? "release");
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
            // Get the tag, supporting VersionRef.
            var tag = options.TagRef?.Value ?? options.Tag ?? "latest";

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
        }, $"{imageName}:{options.Tag ?? options.TagRef?.ToString() ?? "latest"}");
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

    /// <summary>Tag from a VersionRef (for dynamic versioning).</summary>
    public VersionRef? TagRef { get; private set; }

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

    /// <summary>Sets the tag name.</summary>
    public GitHubReleaseOptions WithTag(string tag)
    {
        Tag = tag;
        return this;
    }

    /// <summary>Sets the tag from a VersionRef.</summary>
    public GitHubReleaseOptions WithTag(VersionRef version)
    {
        TagRef = version;
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
}

/// <summary>Options for pushing a Docker image to GitHub Container Registry.</summary>
public class GitHubImageOptions
{
    /// <summary>Image tag.</summary>
    public string? Tag { get; private set; }

    /// <summary>Tag from a VersionRef.</summary>
    public VersionRef? TagRef { get; private set; }

    /// <summary>GitHub owner (user or organization).</summary>
    public string? Owner { get; private set; }

    /// <summary>Sets the image tag.</summary>
    public GitHubImageOptions WithTag(string tag)
    {
        Tag = tag;
        return this;
    }

    /// <summary>Sets the image tag from a VersionRef.</summary>
    public GitHubImageOptions WithTag(VersionRef version)
    {
        TagRef = version;
        return this;
    }

    /// <summary>Sets the GitHub owner (user or organization).</summary>
    public GitHubImageOptions WithOwner(string owner)
    {
        Owner = owner;
        return this;
    }
}
