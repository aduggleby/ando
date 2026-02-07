// =============================================================================
// DockerOperations.cs
//
// Summary: Provides Docker CLI operations for build scripts using buildx.
//
// DockerOperations exposes docker buildx commands as typed methods for build
// scripts. Uses buildx for all builds, supporting single or multi-platform
// builds with optional registry push.
//
// Example usage in build.csando:
//   // Single platform build (loads into local docker)
//   Docker.Build("Dockerfile", o => o
//       .WithTag("myapp:v1.0.0")
//       .WithBuildArg("VERSION", "1.0.0"));
//
//   // Multi-platform build with push to registry
//   Docker.Build("Dockerfile", o => o
//       .WithTag("ghcr.io/myorg/myapp:v1.0.0")
//       .WithPlatforms("linux/amd64", "linux/arm64")
//       .WithPush());
//
// Design Decisions:
// - Uses buildx for all builds (future-proof, supports multi-arch)
// - Single Build operation with WithPlatform/WithPlatforms options
// - Auto-login to ghcr.io when pushing (uses GITHUB_TOKEN or gh CLI)
// - Creates "ando-builder" buildx builder for multi-platform builds
// =============================================================================

using System.Diagnostics;
using Ando.Execution;
using Ando.Logging;
using Ando.Steps;
using Ando.Utilities;

namespace Ando.Operations;

/// <summary>
/// Docker CLI operations for building container images.
/// Methods register steps in the workflow rather than executing immediately.
/// </summary>
public class DockerOperations(
    StepRegistry registry,
    IBuildLogger logger,
    Func<ICommandExecutor> executorFactory,
    GitHubAuthHelper? gitHubAuthHelper = null)
    : OperationsBase(registry, logger, executorFactory)
{
    private readonly GitHubAuthHelper? _gitHubAuthHelper = gitHubAuthHelper;
    /// <summary>
    /// Checks if Docker CLI is available and the Docker daemon is accessible.
    /// Use this to conditionally skip operations that require Docker.
    /// This executes immediately (not registered as a step).
    /// </summary>
    /// <returns>True if Docker is available and working, false otherwise.</returns>
    public bool IsAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Installs the Docker CLI in the container.
    /// Required before using Docker.Build when running with --dind.
    /// </summary>
    public void Install()
    {
        Registry.Register("Docker.Install", async () =>
        {
            // Check if docker is already installed.
            var checkResult = await ExecutorFactory().ExecuteAsync("which", ["docker"]);
            if (checkResult.Success)
            {
                Logger.Info("Docker CLI already installed");
                return true;
            }

            // Install Docker CLI using the official convenience script.
            // This works on most Linux distributions.
            var installResult = await ExecutorFactory().ExecuteAsync("bash", ["-c",
                "curl -fsSL https://get.docker.com | sh"]);

            if (!installResult.Success)
            {
                Logger.Error($"Failed to install Docker CLI: {installResult.Error}");
                return false;
            }

            Logger.Info("Docker CLI installed");
            return true;
        });
    }

    /// <summary>
    /// Builds a Docker image from a Dockerfile using buildx.
    /// Supports single or multi-platform builds, with optional push to registry.
    /// </summary>
    /// <param name="dockerfile">Path to the Dockerfile or directory containing it.</param>
    /// <param name="configure">Configuration for the build.</param>
    public void Build(string dockerfile, Action<DockerBuildOptions>? configure = null)
    {
        var options = new DockerBuildOptions();
        configure?.Invoke(options);

        Registry.Register("Docker.Build", async () =>
        {
            var dockerfilePath = dockerfile;
            var contextPath = options.Context ?? ".";

            // If dockerfile is a directory, assume Dockerfile is in it.
            if (Directory.Exists(dockerfile))
            {
                contextPath = dockerfile;
                dockerfilePath = Path.Combine(dockerfile, "Dockerfile");
            }

            // Auto-login to ghcr.io if pushing to it.
            if (options.Push && options.Tags.Any(t => t.Contains("ghcr.io")))
            {
                if (!await LoginToGhcrAsync(options.Tags))
                {
                    return false;
                }
            }

            // Ensure buildx builder exists for multi-platform builds.
            if (options.Platforms.Count > 0)
            {
                var createBuilder = await ExecutorFactory().ExecuteAsync("docker",
                    ["buildx", "create", "--name", "ando-builder", "--use", "--bootstrap"]);
                if (!createBuilder.Success)
                {
                    // Builder may already exist, try to use it.
                    await ExecutorFactory().ExecuteAsync("docker",
                        ["buildx", "use", "ando-builder"]);
                }
            }

            var argsBuilder = new ArgumentBuilder()
                .Add("buildx", "build")
                .Add("-f", dockerfilePath);

            // Add platforms (multiple or single).
            if (options.Platforms.Count > 0)
            {
                argsBuilder.Add("--platform", string.Join(",", options.Platforms));
            }

            // Add all tags.
            foreach (var tag in options.Tags)
            {
                argsBuilder.Add("-t", tag);
            }

            // Add build arguments.
            foreach (var (key, value) in options.BuildArgs)
            {
                argsBuilder.Add("--build-arg", $"{key}={value}");
            }

            // Add push flag.
            argsBuilder.AddFlag(options.Push, "--push");

            // Add load flag (loads image into local docker).
            // Required for single-platform builds without push to make image available locally.
            argsBuilder.AddFlag(options.Load, "--load");

            // Add no-cache flag.
            argsBuilder.AddFlag(options.NoCache, "--no-cache");

            // Add the context path last.
            argsBuilder.Add(contextPath);

            var args = argsBuilder.Build();

            Logger.Debug($"docker {string.Join(" ", args)}");
            // Docker builds (especially multi-arch buildx builds) can easily exceed the
            // default command timeout. Use a higher default for builds.
            var cmdOptions = new CommandOptions
            {
                TimeoutMs = options.TimeoutMs
            };
            var result = await ExecutorFactory().ExecuteAsync("docker", args, cmdOptions);

            if (!result.Success)
            {
                Logger.Error($"Docker build failed: {result.Error}");
                return false;
            }

            if (options.Tags.Count > 0)
            {
                var action = options.Push ? "Built and pushed" : "Built";
                Logger.Info($"{action} image: {string.Join(", ", options.Tags)}");
            }

            return true;
        }, options.Tags.FirstOrDefault() ?? dockerfile);
    }

    /// <summary>
    /// Logs in to GitHub Container Registry using the GitHub token.
    /// Extracts the owner from the ghcr.io tag (e.g., ghcr.io/owner/image:tag).
    /// </summary>
    private async Task<bool> LoginToGhcrAsync(List<string> tags)
    {
        if (_gitHubAuthHelper == null)
        {
            Logger.Error("GitHub authentication not available for ghcr.io login");
            return false;
        }

        var token = _gitHubAuthHelper.GetToken();
        if (string.IsNullOrEmpty(token))
        {
            Logger.Error("GitHub token required for pushing to ghcr.io");
            return false;
        }

        // Extract owner from ghcr.io tag.
        var owner = GhcrHelper.ExtractOwnerFromTags(tags);
        if (string.IsNullOrEmpty(owner))
        {
            Logger.Error("Could not extract GitHub owner from ghcr.io tag");
            return false;
        }

        return await GhcrHelper.LoginAsync(ExecutorFactory(), Logger, token, owner);
    }
}

/// <summary>Options for 'docker buildx build' command.</summary>
public class DockerBuildOptions
{
    // Docker builds can be slow (e.g., multi-arch with QEMU). Default to 30 minutes
    // to avoid spurious failures; callers can override if desired.
    private const int DefaultBuildTimeoutMs = 30 * 60 * 1000;

    /// <summary>Image tags (e.g., "myapp:v1.0.0", "myapp:latest").</summary>
    public List<string> Tags { get; } = new();

    /// <summary>Target platforms (e.g., "linux/amd64", "linux/arm64").</summary>
    public List<string> Platforms { get; } = new();

    /// <summary>Build context directory (default: current directory).</summary>
    public string? Context { get; private set; }

    /// <summary>Build arguments to pass to Docker.</summary>
    public Dictionary<string, string> BuildArgs { get; } = new();

    /// <summary>Push images to registry after building.</summary>
    public bool Push { get; private set; }

    /// <summary>Load image into local docker after building (default: true for single-platform builds without push).</summary>
    public bool Load { get; private set; } = true;

    /// <summary>Do not use cache when building.</summary>
    public bool NoCache { get; private set; }

    /// <summary>Timeout for the docker build command (milliseconds).</summary>
    public int TimeoutMs { get; private set; } = DefaultBuildTimeoutMs;

    /// <summary>Adds an image tag. Can be called multiple times for multiple tags.</summary>
    public DockerBuildOptions WithTag(string tag)
    {
        Tags.Add(tag);
        return this;
    }

    /// <summary>Sets a single target platform (e.g., "linux/amd64").</summary>
    public DockerBuildOptions WithPlatform(string platform)
    {
        Platforms.Clear();
        Platforms.Add(platform);
        return this;
    }

    /// <summary>Adds target platforms for multi-arch builds. Can be called with multiple platforms.</summary>
    public DockerBuildOptions WithPlatforms(params string[] platforms)
    {
        Platforms.AddRange(platforms);
        return this;
    }

    /// <summary>Sets the build context directory.</summary>
    public DockerBuildOptions WithContext(string context)
    {
        Context = context;
        return this;
    }

    /// <summary>Adds a build argument.</summary>
    public DockerBuildOptions WithBuildArg(string key, string value)
    {
        BuildArgs[key] = value;
        return this;
    }

    /// <summary>Push images to registry after building. Disables --load.</summary>
    public DockerBuildOptions WithPush()
    {
        Push = true;
        Load = false; // Can't use --load with --push
        return this;
    }

    /// <summary>Disables the build cache.</summary>
    public DockerBuildOptions WithNoCache()
    {
        NoCache = true;
        return this;
    }

    /// <summary>Disables loading the image into local docker. Useful when only pushing to a registry.</summary>
    public DockerBuildOptions WithoutLoad()
    {
        Load = false;
        return this;
    }

    /// <summary>Overrides the docker build timeout.</summary>
    public DockerBuildOptions WithTimeoutMinutes(int minutes)
    {
        TimeoutMs = Math.Max(1, minutes) * 60 * 1000;
        return this;
    }
}
