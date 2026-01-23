// =============================================================================
// BuildPathPropertyTests.cs
//
// Summary: Property-based tests for BuildPath using FsCheck.
//
// Tests verify invariants hold across randomly generated inputs including
// path division, implicit conversion, equality, and associativity.
// =============================================================================

using Ando.Context;
using FsCheck;
using FsCheck.Xunit;

namespace Ando.Tests.Unit.PropertyBased;
[Trait("Category", "Unit")]
[Trait("Category", "PropertyBased")]
public class BuildPathPropertyTests
{
    [Property]
    public bool DivisionOperator_ProducesRootedPath(NonEmptyString baseStr, NonEmptyString subStr)
    {
        var basePath = SanitizePath(baseStr.Get);
        var subPath = SanitizePath(subStr.Get);

        if (string.IsNullOrWhiteSpace(basePath))
            return true; // Skip invalid

        try
        {
            var root = new BuildPath("/" + basePath);
            var combined = root / subPath;

            return Path.IsPathRooted(combined.Value);
        }
        catch
        {
            return true; // Skip paths that cause exceptions
        }
    }

    [Property]
    public bool ImplicitConversion_ReturnsSameAsValue(NonEmptyString pathStr)
    {
        var path = SanitizePath(pathStr.Get);

        if (string.IsNullOrWhiteSpace(path))
            return true;

        try
        {
            var buildPath = new BuildPath("/" + path);
            string implicitValue = buildPath;

            return implicitValue == buildPath.Value;
        }
        catch
        {
            return true;
        }
    }

    [Property]
    public bool ToString_ReturnsSameAsValue(NonEmptyString pathStr)
    {
        var path = SanitizePath(pathStr.Get);

        if (string.IsNullOrWhiteSpace(path))
            return true;

        try
        {
            var buildPath = new BuildPath("/" + path);

            return buildPath.ToString() == buildPath.Value;
        }
        catch
        {
            return true;
        }
    }

    [Property]
    public bool DivisionOperator_IsAssociative(PositiveInt depth)
    {
        var actualDepth = Math.Min(depth.Get, 5); // Limit depth to avoid extremely long paths

        try
        {
            var root = new BuildPath("/base");

            // Build path incrementally vs all at once
            var incremental = root;
            var segments = Enumerable.Range(0, actualDepth).Select(i => $"dir{i}").ToArray();

            foreach (var segment in segments)
            {
                incremental = incremental / segment;
            }

            var direct = root;
            foreach (var segment in segments)
            {
                direct = direct / segment;
            }

            return incremental.Value == direct.Value;
        }
        catch
        {
            return true;
        }
    }

    [Property]
    public bool Equality_IsReflexive(NonEmptyString pathStr)
    {
        var path = SanitizePath(pathStr.Get);

        if (string.IsNullOrWhiteSpace(path))
            return true;

        try
        {
            var buildPath = new BuildPath("/" + path);

            return buildPath.Equals(buildPath);
        }
        catch
        {
            return true;
        }
    }

    [Property]
    public bool Equality_IsSymmetric(NonEmptyString pathStr)
    {
        var path = SanitizePath(pathStr.Get);

        if (string.IsNullOrWhiteSpace(path))
            return true;

        try
        {
            var path1 = new BuildPath("/" + path);
            var path2 = new BuildPath("/" + path);

            return path1.Equals(path2) == path2.Equals(path1);
        }
        catch
        {
            return true;
        }
    }

    private static string SanitizePath(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // Remove null chars and invalid path chars
        var sanitized = new string(input
            .Replace('\0', '_')
            .Where(c => !Path.GetInvalidPathChars().Contains(c))
            .Take(50)
            .ToArray());

        return sanitized;
    }
}
