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
    /// Path to repos directory inside the server container.
    /// Repos are cloned here before being mounted into build containers.
    /// </summary>
    public string ReposPath { get; set; } = "/data/repos";
}
