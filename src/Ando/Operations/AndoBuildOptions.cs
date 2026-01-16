// =============================================================================
// AndoBuildOptions.cs
//
// Summary: Fluent builder for configuring nested build options.
//
// AndoBuildOptions uses a fluent builder pattern for readable configuration
// of child build execution. Each method returns 'this' to allow chaining.
//
// Example usage:
//   Ando.Build(Directory("./website"), o => o
//       .WithVerbosity("detailed")
//       .ColdStart());
//
// Design Decisions:
// - Fluent builder pattern for readable, chainable configuration
// - Options map to ando CLI flags
// - ColdStart forces fresh container (useful for debugging)
// =============================================================================

namespace Ando.Operations;

/// <summary>
/// Fluent builder for configuring nested build options.
/// Methods return 'this' for chaining.
/// </summary>
public class AndoBuildOptions
{
    /// <summary>Verbosity level (quiet, minimal, normal, detailed).</summary>
    public string? Verbosity { get; private set; }

    /// <summary>Force fresh container instead of reusing warm container.</summary>
    public bool Cold { get; private set; }

    /// <summary>Enable Docker-in-Docker for the child build.</summary>
    public bool Dind { get; private set; }

    /// <summary>Custom Docker image for the child build.</summary>
    public string? Image { get; private set; }

    /// <summary>Sets the verbosity level for the child build.</summary>
    public AndoBuildOptions WithVerbosity(string verbosity)
    {
        Verbosity = verbosity;
        return this;
    }

    /// <summary>Forces a fresh container for the child build.</summary>
    public AndoBuildOptions ColdStart(bool cold = true)
    {
        Cold = cold;
        return this;
    }

    /// <summary>Enables Docker-in-Docker for the child build.</summary>
    public AndoBuildOptions WithDind(bool dind = true)
    {
        Dind = dind;
        return this;
    }

    /// <summary>Sets a custom Docker image for the child build.</summary>
    public AndoBuildOptions WithImage(string image)
    {
        Image = image;
        return this;
    }

    /// <summary>Builds the CLI arguments for ando run.</summary>
    internal string[] BuildArgs()
    {
        var args = new List<string> { "run" };

        if (!string.IsNullOrEmpty(Verbosity))
        {
            args.Add("--verbosity");
            args.Add(Verbosity);
        }

        if (Cold)
        {
            args.Add("--cold");
        }

        if (Dind)
        {
            args.Add("--dind");
        }

        if (!string.IsNullOrEmpty(Image))
        {
            args.Add("--image");
            args.Add(Image);
        }

        return args.ToArray();
    }
}
