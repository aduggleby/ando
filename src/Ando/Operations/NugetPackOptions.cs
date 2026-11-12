// =============================================================================
// NugetPackOptions.cs
//
// Summary: Fluent builder for configuring 'dotnet pack' command options.
//
// NugetPackOptions uses a fluent builder pattern for readable configuration
// of pack options. Each method returns 'this' to allow chaining.
//
// Example usage:
//   Nuget.Pack(project, o => o
//       .WithConfiguration(Configuration.Release)
//       .WithVersion("1.0.0")
//       .WithSymbols()
//       .Output(Root / "packages"));
//
// Design Decisions:
// - Fluent builder pattern for readable, chainable configuration
// - Private setters prevent external modification after configuration
// - Output() accepts both BuildPath and string for flexibility
// - Version can be set explicitly or via suffix for pre-release versions
// =============================================================================

using Ando.Context;
using Ando.Workflow;

namespace Ando.Operations;

/// <summary>
/// Fluent builder for configuring 'dotnet pack' command options.
/// Methods return 'this' for chaining.
/// </summary>
public class NugetPackOptions
{
    /// <summary>Output directory for the generated package.</summary>
    public BuildPath? OutputPath { get; private set; }

    /// <summary>Build configuration (Debug or Release).</summary>
    public Configuration? Configuration { get; private set; }

    /// <summary>Skip package restore before packing.</summary>
    public bool NoRestore { get; private set; }

    /// <summary>Skip build before packing (uses existing build output).</summary>
    public bool NoBuild { get; private set; }

    /// <summary>Include symbol package (.snupkg) for debugging.</summary>
    public bool IncludeSymbols { get; private set; }

    /// <summary>Include source files in the symbol package.</summary>
    public bool IncludeSource { get; private set; }

    /// <summary>Explicit package version (overrides project file version).</summary>
    public string? Version { get; private set; }

    /// <summary>Version suffix for pre-release packages (e.g., "beta1", "preview").</summary>
    public string? VersionSuffix { get; private set; }


    /// <summary>Explicit SourceRevisionId (full commit SHA); overrides the automatic --ref value.</summary>
    public string? SourceRevision { get; private set; }

    /// <summary>
    /// Passes `-p:SourceRevisionId=&lt;sha&gt;` so the assembly's InformationalVersion gets `+&lt;sha&gt;`
    /// even though the build container has no .git. Overrides the value ANDO adds automatically
    /// for --ref runs.
    /// </summary>
    public NugetPackOptions WithSourceRevision(string sha)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha);
        SourceRevision = sha.Trim();
        return this;
    }

    /// <summary>Sets the output directory using a BuildPath.</summary>
    public NugetPackOptions Output(BuildPath path)
    {
        OutputPath = path;
        return this;
    }

    /// <summary>Sets the output directory using a string path.</summary>
    public NugetPackOptions Output(string path)
    {
        OutputPath = new BuildPath(path);
        return this;
    }

    /// <summary>Sets the build configuration (Debug or Release).</summary>
    public NugetPackOptions WithConfiguration(Configuration configuration)
    {
        Configuration = configuration;
        return this;
    }

    /// <summary>Sets the explicit package version.</summary>
    public NugetPackOptions WithVersion(string version)
    {
        Version = version;
        return this;
    }

    /// <summary>Sets the version suffix for pre-release packages.</summary>
    public NugetPackOptions WithVersionSuffix(string suffix)
    {
        VersionSuffix = suffix;
        return this;
    }

    /// <summary>Includes symbol package (.snupkg) for debugging support.</summary>
    public NugetPackOptions WithSymbols(bool include = true)
    {
        IncludeSymbols = include;
        return this;
    }

    /// <summary>Includes source files in the symbol package.</summary>
    public NugetPackOptions WithSource(bool include = true)
    {
        IncludeSource = include;
        return this;
    }

    /// <summary>Skips package restore before packing.</summary>
    public NugetPackOptions SkipRestore()
    {
        NoRestore = true;
        return this;
    }

    /// <summary>Skips build before packing (uses existing build output).</summary>
    public NugetPackOptions SkipBuild()
    {
        NoBuild = true;
        return this;
    }
}
