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
//   Options.UseConfiguration(c => c.Release);
//   // or
//   Options.UseConfiguration(Configuration.Release);
//
// Design Decisions:
// - Fluent API allows chaining and readable configuration
// - Selector pattern provides IntelliSense-friendly syntax: c => c.Release
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
    /// Sets the build configuration using a fluent selector.
    /// Usage: Options.UseConfiguration(c => c.Release);
    /// </summary>
    /// <param name="selector">Lambda that selects Debug or Release.</param>
    public BuildOptions UseConfiguration(Func<ConfigurationSelector, Configuration> selector)
    {
        Configuration = selector(new ConfigurationSelector());
        return this;
    }

    /// <summary>
    /// Sets the build configuration directly.
    /// </summary>
    /// <param name="configuration">The configuration to use.</param>
    public BuildOptions UseConfiguration(Configuration configuration)
    {
        Configuration = configuration;
        return this;
    }
}

/// <summary>
/// Selector for configuration values used in fluent API.
/// Provides IntelliSense-friendly syntax: c => c.Release
/// </summary>
public class ConfigurationSelector
{
    /// <summary>Select Debug configuration.</summary>
    public Configuration Debug => Configuration.Debug;

    /// <summary>Select Release configuration.</summary>
    public Configuration Release => Configuration.Release;
}
