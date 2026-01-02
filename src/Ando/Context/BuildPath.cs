// =============================================================================
// BuildPath.cs
//
// Summary: Immutable value type representing a filesystem path in build scripts.
//
// BuildPath provides type-safe path manipulation with operator overloading for
// intuitive path construction. It normalizes all paths to absolute form and
// provides filesystem existence checking.
//
// Design Decisions:
// - Struct for value semantics and zero allocation when passed by value
// - Readonly struct to guarantee immutability
// - Operator / for path joining (e.g., root / "src" / "app") is more readable
//   than Path.Combine chains and common in build systems (Cake, FAKE, etc.)
// - Implicit conversion to string allows seamless use with .NET path APIs
// - Always stores absolute path to avoid ambiguity in different working directories
// =============================================================================

namespace Ando.Context;

/// <summary>
/// Immutable value type for type-safe path manipulation in build scripts.
/// Provides operator overloading for intuitive path construction.
/// </summary>
public readonly struct BuildPath
{
    /// <summary>The absolute path value.</summary>
    public string Value { get; }

    /// <summary>
    /// Creates a BuildPath from a path string, normalizing to absolute form.
    /// </summary>
    /// <param name="path">Relative or absolute path.</param>
    public BuildPath(string path)
    {
        // Always store absolute paths to avoid ambiguity when working directory changes.
        Value = Path.GetFullPath(path);
    }

    // Path join operator: root / "subdir" / "file.txt"
    // This syntax is common in build systems and more readable than Path.Combine chains.
    public static BuildPath operator /(BuildPath left, string right)
    {
        return new BuildPath(Path.Combine(left.Value, right));
    }

    // Path join operator for combining two BuildPaths.
    public static BuildPath operator /(BuildPath left, BuildPath right)
    {
        return new BuildPath(Path.Combine(left.Value, right.Value));
    }

    // Implicit conversion allows BuildPath to be used anywhere a string path is expected.
    public static implicit operator string(BuildPath path) => path.Value;

    public override string ToString() => Value;

    // Filesystem existence checks - useful for conditional build logic.
    public bool Exists() => Directory.Exists(Value) || File.Exists(Value);

    public bool IsDirectory() => Directory.Exists(Value);

    public bool IsFile() => File.Exists(Value);
}
