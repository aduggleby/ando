// =============================================================================
// VersionRef.cs
//
// Summary: Represents a deferred version reference resolved at execution time.
//
// VersionRef allows build scripts to reference versions that aren't known until
// a step executes. For example, BumpVersion returns a VersionRef that contains
// the new version after the bump step runs.
//
// Example usage in build.csando:
//   var version = Dotnet.BumpVersion(project);
//   GitHub.PushImage("myapp", o => o.WithTag(version));
//
// Design Decisions:
// - Implicit string conversion for seamless use in string contexts
// - Throws if accessed before value is set (catches misuse at runtime)
// - Supports ToString() for interpolated strings and logging
// =============================================================================

namespace Ando.References;

/// <summary>
/// Represents a version that will be resolved at step execution time.
/// Used by BumpVersion operations to provide the new version to subsequent steps.
/// </summary>
public class VersionRef
{
    private string? _value;
    private bool _isResolved;
    private readonly string _description;

    /// <summary>
    /// Creates a new unresolved version reference.
    /// </summary>
    /// <param name="description">Description for error messages (e.g., "Dotnet.BumpVersion").</param>
    public VersionRef(string description)
    {
        _description = description;
    }

    /// <summary>
    /// Gets the resolved version value.
    /// Throws if accessed before the version is resolved.
    /// </summary>
    public string Value
    {
        get
        {
            if (!_isResolved)
            {
                throw new InvalidOperationException(
                    $"Version from {_description} has not been resolved yet. " +
                    "This typically means the step hasn't executed. " +
                    "VersionRef values are resolved during workflow execution.");
            }
            return _value!;
        }
    }

    /// <summary>
    /// Returns true if the version has been resolved.
    /// </summary>
    public bool IsResolved => _isResolved;

    /// <summary>
    /// Sets the resolved version value.
    /// Called by the step that determines the version.
    /// </summary>
    /// <param name="value">The resolved version string.</param>
    internal void Resolve(string value)
    {
        _value = value;
        _isResolved = true;
    }

    /// <summary>
    /// Implicit conversion to string for use in string contexts.
    /// </summary>
    public static implicit operator string(VersionRef versionRef) => versionRef.Value;

    /// <summary>
    /// Returns the version string, or a placeholder if not yet resolved.
    /// Useful for logging during script loading phase.
    /// </summary>
    public override string ToString() => _isResolved ? _value! : $"<{_description}:pending>";
}
