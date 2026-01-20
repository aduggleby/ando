// =============================================================================
// BuildSettings.cs
//
// Summary: Configuration settings for build execution.
//
// Controls default build behavior including timeout, Docker image, and
// worker configuration. These are system-wide defaults that can be
// overridden per-project.
//
// Design Decisions:
// - Timeout has both default and maximum limits
// - Default Docker image is .NET SDK for Ando builds
// - WorkerCount controls Hangfire parallelism
// =============================================================================

namespace Ando.Server.Configuration;

/// <summary>
/// Configuration for build execution.
/// </summary>
public class BuildSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Build";

    /// <summary>
    /// Default build timeout in minutes if not specified per-project.
    /// </summary>
    public int DefaultTimeoutMinutes { get; set; } = 15;

    /// <summary>
    /// Maximum allowed timeout in minutes (prevents abuse).
    /// </summary>
    public int MaxTimeoutMinutes { get; set; } = 60;

    /// <summary>
    /// Default Docker image for builds if not specified per-project.
    /// </summary>
    public string DefaultDockerImage { get; set; } = "mcr.microsoft.com/dotnet/sdk:9.0-alpine";

    /// <summary>
    /// Number of Hangfire worker threads for build execution.
    /// Each worker can run one build at a time.
    /// </summary>
    public int WorkerCount { get; set; } = 2;

    /// <summary>
    /// Path to repos directory on the HOST filesystem.
    /// This path is used for Docker volume mounts when creating build containers.
    /// Example: /opt/ando/data/repos
    /// </summary>
    public string ReposPath { get; set; } = "/data/repos";

    /// <summary>
    /// Path to repos directory inside the SERVER container (optional).
    /// When running the server in a container, this is where the ReposPath is mounted internally.
    /// If not set, ReposPath is used for both purposes (assumes matching paths).
    /// Example: /data/repos (when ReposPath=/opt/ando/data/repos is mounted at /data/repos)
    /// </summary>
    public string? ReposPathInContainer { get; set; }

    /// <summary>
    /// Gets the path to use for git operations (clone, checkout) inside the server.
    /// Returns ReposPathInContainer if set, otherwise falls back to ReposPath.
    /// </summary>
    public string GetReposPathForServer() => ReposPathInContainer ?? ReposPath;

    /// <summary>
    /// Base URL of the server for generating absolute URLs (e.g., for GitHub commit statuses).
    /// Example: https://demo.andobuild.com
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Path to the Docker socket on the HOST filesystem.
    /// This is used when creating build containers that need Docker-in-Docker support.
    /// For standard Docker: /var/run/docker.sock
    /// For rootless Docker: /var/run/user/{UID}/docker.sock
    /// </summary>
    public string DockerSocketPath { get; set; } = "/var/run/docker.sock";
}
