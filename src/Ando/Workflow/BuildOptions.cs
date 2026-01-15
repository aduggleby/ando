// =============================================================================
// BuildOptions.cs
//
// Summary: Build configuration options available to build scripts.
//
// BuildOptions provides a fluent API for configuring build behavior.
// Scripts can set options like configuration (Debug/Release) that affect
// subsequent build operations.
//
// Example usage:
//   Options.UseConfiguration(Configuration.Release);
//
// Design Decisions:
// - Fluent API allows chaining and readable configuration
// - Private setter prevents modification after configuration
// - Default is Debug for development convenience
// =============================================================================

namespace Ando.Workflow;

/// <summary>
/// Build configuration: Debug or Release.
/// </summary>
public enum Configuration
{
    /// <summary>Debug configuration with debugging symbols.</summary>
    Debug,

    /// <summary>Release configuration with optimizations.</summary>
    Release
}

/// <summary>
/// Build options configured via fluent API in build scripts.
/// </summary>
public class BuildOptions
{
    /// <summary>
    /// Current build configuration. Default is Debug.
    /// </summary>
    public Configuration Configuration { get; private set; } = Configuration.Debug;

    /// <summary>
    /// Docker image to use for the build. If null, uses CLI default.
    /// </summary>
    public string? Image { get; private set; }

    /// <summary>
    /// Sets the build configuration.
    /// </summary>
    /// <param name="configuration">The configuration to use.</param>
    public BuildOptions UseConfiguration(Configuration configuration)
    {
        Configuration = configuration;
        return this;
    }

    /// <summary>
    /// Sets the Docker image to use for the build.
    /// </summary>
    /// <param name="image">Docker image name (e.g., "ubuntu:22.04", "node:20").</param>
    public BuildOptions UseImage(string image)
    {
        Image = image;
        return this;
    }
}
