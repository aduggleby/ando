// =============================================================================
// ArgumentBuilder.cs
//
// Summary: Fluent builder for constructing CLI argument arrays.
//
// ArgumentBuilder provides a clean, declarative way to build command-line
// arguments with conditional inclusion. This eliminates repetitive patterns
// across operations classes.
// =============================================================================

namespace Ando.Execution;

/// <summary>
/// Fluent builder for constructing CLI argument arrays.
/// Provides conditional argument inclusion for cleaner code.
/// </summary>
public class ArgumentBuilder
{
    private readonly List<string> _args = [];

    /// <summary>Adds one or more arguments unconditionally.</summary>
    public ArgumentBuilder Add(params string[] args)
    {
        _args.AddRange(args);
        return this;
    }

    /// <summary>Adds arguments only if condition is true.</summary>
    public ArgumentBuilder AddIf(bool condition, params string[] args)
        => condition ? Add(args) : this;

    /// <summary>Adds flag and value if value is not null.</summary>
    public ArgumentBuilder AddIfNotNull(string flag, string? value)
        => value != null ? Add(flag, value) : this;

    /// <summary>Adds flag and value from a value type if it has a value.</summary>
    public ArgumentBuilder AddIfNotNull<T>(string flag, T? value) where T : struct
        => value.HasValue ? Add(flag, value.Value.ToString()!) : this;

    /// <summary>Adds a single value if it is not null.</summary>
    public ArgumentBuilder AddIfNotNull(string? value)
        => value != null ? Add(value) : this;

    /// <summary>Adds a single flag if condition is true.</summary>
    public ArgumentBuilder AddFlag(bool condition, string flag)
        => condition ? Add(flag) : this;

    /// <summary>Builds the final argument array.</summary>
    public string[] Build() => [.. _args];
}
