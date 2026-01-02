// =============================================================================
// DotnetPublishOptions.cs
//
// Summary: Fluent builder for configuring 'dotnet publish' command options.
//
// DotnetPublishOptions uses a fluent builder pattern for readable configuration
// of publish options. Each method returns 'this' to allow chaining.
//
// Example usage:
//   Dotnet.Publish(project, opt => opt
//       .WithConfiguration(Configuration.Release)
//       .WithRuntime("linux-x64")
//       .AsSelfContained()
//       .AsSingleFile()
//       .Output(Context.Paths.Artifacts / "app"));
//
// Design Decisions:
// - Fluent builder pattern for readable, chainable configuration
// - Private setters prevent external modification after configuration
// - Output() accepts both BuildPath and string for flexibility
// - Boolean flags use "As" prefix for readability (AsSelfContained, AsSingleFile)
// - Skip methods clearly indicate what phase is being skipped
// =============================================================================

using Ando.Context;
using Ando.Workflow;

namespace Ando.Operations;

/// <summary>
/// Fluent builder for configuring 'dotnet publish' command options.
/// Methods return 'this' for chaining.
/// </summary>
public class DotnetPublishOptions
{
    /// <summary>Output directory for published artifacts.</summary>
    public BuildPath? OutputPath { get; private set; }

    /// <summary>Build configuration (Debug or Release).</summary>
    public Configuration? Configuration { get; private set; }

    /// <summary>Target runtime identifier (e.g., linux-x64, win-x64).</summary>
    public string? Runtime { get; private set; }

    /// <summary>Whether to include .NET runtime in output.</summary>
    public bool? SelfContained { get; private set; }

    /// <summary>Whether to publish as a single executable file.</summary>
    public bool SingleFile { get; private set; }

    /// <summary>Skip restore before publishing.</summary>
    public bool NoRestore { get; private set; }

    /// <summary>Skip build before publishing.</summary>
    public bool NoBuild { get; private set; }

    /// <summary>Sets the output directory using a BuildPath.</summary>
    public DotnetPublishOptions Output(BuildPath path)
    {
        OutputPath = path;
        return this;
    }

    /// <summary>Sets the output directory using a string path.</summary>
    public DotnetPublishOptions Output(string path)
    {
        OutputPath = new BuildPath(path);
        return this;
    }

    /// <summary>Sets the build configuration (Debug or Release).</summary>
    public DotnetPublishOptions WithConfiguration(Configuration configuration)
    {
        Configuration = configuration;
        return this;
    }

    /// <summary>Sets the target runtime (e.g., linux-x64, win-x64, osx-arm64).</summary>
    public DotnetPublishOptions WithRuntime(string runtime)
    {
        Runtime = runtime;
        return this;
    }

    /// <summary>
    /// Publishes as self-contained, including the .NET runtime.
    /// Required for machines without .NET installed.
    /// </summary>
    public DotnetPublishOptions AsSelfContained(bool selfContained = true)
    {
        SelfContained = selfContained;
        return this;
    }

    /// <summary>
    /// Publishes as a single executable file.
    /// Requires self-contained and a specific runtime.
    /// </summary>
    public DotnetPublishOptions AsSingleFile(bool singleFile = true)
    {
        SingleFile = singleFile;
        return this;
    }

    /// <summary>Skips package restore before publishing.</summary>
    public DotnetPublishOptions SkipRestore()
    {
        NoRestore = true;
        return this;
    }

    /// <summary>Skips build before publishing (uses existing build output).</summary>
    public DotnetPublishOptions SkipBuild()
    {
        NoBuild = true;
        return this;
    }
}
