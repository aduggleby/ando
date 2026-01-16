// =============================================================================
// BuildOptions.cs
//
// Summary: Internal build options for container configuration.
//
// BuildOptions stores settings that affect how the build container is created.
// These are set via Ando.UseImage() in build scripts.
//
// Design Decisions:
// - Image is set via Ando.UseImage() for a cleaner API
// - Internal class - not directly exposed to build scripts
// =============================================================================

namespace Ando.Workflow;

/// <summary>
/// Build configuration: Debug or Release.
/// Used by Dotnet operations to specify build configuration.
/// </summary>
public enum Configuration
{
    /// <summary>Debug configuration with debugging symbols.</summary>
    Debug,

    /// <summary>Release configuration with optimizations.</summary>
    Release
}

/// <summary>
/// Internal build options for container configuration.
/// </summary>
public class BuildOptions
{
    /// <summary>
    /// Docker image to use for the build. If null, uses CLI default.
    /// </summary>
    public string? Image { get; private set; }

    /// <summary>
    /// Sets the Docker image to use for the build.
    /// Called by AndoOperations.UseImage().
    /// </summary>
    internal BuildOptions UseImage(string image)
    {
        Image = image;
        return this;
    }
}
