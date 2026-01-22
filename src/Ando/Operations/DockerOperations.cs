// =============================================================================
// DockerOperations.cs
//
// Summary: Provides Docker CLI operations for build scripts.
//
// DockerOperations exposes docker commands (build) as typed methods for build
// scripts. Currently focused on building images; pushing is handled by
// registry-specific operations (e.g., GitHub.PushImage for ghcr.io).
//
// Example usage in build.csando:
//   Docker.Build("Dockerfile", o => o
//       .WithTag("myapp:v1.0.0")
//       .WithBuildArg("VERSION", "1.0.0"));
//
// Design Decisions:
// - Minimal scope initially (just Build), expand as needed
// - Registry push handled by specific providers (GitHub, Azure, etc.)
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// Docker CLI operations for building container images.
/// Methods register steps in the workflow rather than executing immediately.
/// </summary>
public class DockerOperations(
    StepRegistry registry,
    IBuildLogger logger,
    Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
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
    /// Builds a Docker image from a Dockerfile.
    /// </summary>
    /// <param name="dockerfile">Path to the Dockerfile or directory containing it.</param>
    /// <param name="configure">Configuration for the build.</param>
    public void Build(string dockerfile, Action<DockerBuildOptions>? configure = null)
    {
        var options = new DockerBuildOptions();
        configure?.Invoke(options);

        Registry.Register("Docker.Build", async () =>
        {
            var tag = options.Tag;

            // Determine the context directory and dockerfile path.
            var dockerfilePath = dockerfile;
            var contextPath = options.Context ?? ".";

            // If dockerfile is a directory, assume Dockerfile is in it.
            if (Directory.Exists(dockerfile))
            {
                contextPath = dockerfile;
                dockerfilePath = Path.Combine(dockerfile, "Dockerfile");
            }

            var argsBuilder = new ArgumentBuilder()
                .Add("build")
                .Add("-f", dockerfilePath)
                .AddIfNotNull("-t", tag);

            // Add build arguments.
            foreach (var (key, value) in options.BuildArgs)
            {
                argsBuilder.Add("--build-arg", $"{key}={value}");
            }

            // Add platform if specified.
            if (!string.IsNullOrEmpty(options.Platform))
            {
                argsBuilder.Add("--platform", options.Platform);
            }

            // Add no-cache flag.
            argsBuilder.AddFlag(options.NoCache, "--no-cache");

            // Add the context path last.
            argsBuilder.Add(contextPath);

            var args = argsBuilder.Build();

            Logger.Debug($"docker {string.Join(" ", args)}");
            var result = await ExecutorFactory().ExecuteAsync("docker", args);

            if (!result.Success)
            {
                Logger.Error($"Docker build failed: {result.Error}");
                return false;
            }

            if (!string.IsNullOrEmpty(tag))
            {
                Logger.Info($"Built image: {tag}");
            }

            return true;
        }, options.Tag ?? dockerfile);
    }
}

/// <summary>Options for 'docker build' command.</summary>
public class DockerBuildOptions
{
    /// <summary>Image tag (e.g., "myapp:v1.0.0").</summary>
    public string? Tag { get; private set; }

    /// <summary>Build context directory (default: current directory).</summary>
    public string? Context { get; private set; }

    /// <summary>Build arguments to pass to Docker.</summary>
    public Dictionary<string, string> BuildArgs { get; } = new();

    /// <summary>Target platform (e.g., "linux/amd64").</summary>
    public string? Platform { get; private set; }

    /// <summary>Do not use cache when building.</summary>
    public bool NoCache { get; private set; }

    /// <summary>Sets the image tag.</summary>
    public DockerBuildOptions WithTag(string tag)
    {
        Tag = tag;
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

    /// <summary>Sets the target platform.</summary>
    public DockerBuildOptions WithPlatform(string platform)
    {
        Platform = platform;
        return this;
    }

    /// <summary>Disables the build cache.</summary>
    public DockerBuildOptions WithNoCache()
    {
        NoCache = true;
        return this;
    }
}
