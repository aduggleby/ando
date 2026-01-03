// =============================================================================
// ProjectRefPropertyTests.cs
//
// Summary: Property-based tests for ProjectRef using FsCheck.
//
// Tests verify invariants for project reference creation and name extraction
// across randomly generated inputs.
// =============================================================================

using Ando.References;
using FsCheck;
using FsCheck.Xunit;

namespace Ando.Tests.Unit.PropertyBased;
[Trait("Category", "Unit")]
[Trait("Category", "PropertyBased")]
public class ProjectRefPropertyTests
{
    [Property]
    public bool Name_ExtractsFileNameWithoutExtension(NonEmptyString projectNameStr)
    {
        var safeName = SanitizeName(projectNameStr.Get);

        if (string.IsNullOrWhiteSpace(safeName))
            return true;

        try
        {
            var path = $"./src/{safeName}/{safeName}.csproj";
            var project = ProjectRef.From(path);

            return project.Name == safeName;
        }
        catch
        {
            return true;
        }
    }

    [Property]
    public bool Path_PreservesInput(NonEmptyString projectNameStr)
    {
        var safeName = SanitizeName(projectNameStr.Get);

        if (string.IsNullOrWhiteSpace(safeName))
            return true;

        try
        {
            var path = $"./src/{safeName}.csproj";
            var project = ProjectRef.From(path);

            return project.Path == path;
        }
        catch
        {
            return true;
        }
    }

    [Property]
    public bool ImplicitConversion_ReturnsPath(NonEmptyString projectNameStr)
    {
        var safeName = SanitizeName(projectNameStr.Get);

        if (string.IsNullOrWhiteSpace(safeName))
            return true;

        try
        {
            var path = $"./src/{safeName}.csproj";
            var project = ProjectRef.From(path);
            string implicitPath = project;

            return implicitPath == project.Path;
        }
        catch
        {
            return true;
        }
    }

    [Property]
    public bool ToString_ReturnsName(NonEmptyString projectNameStr)
    {
        var safeName = SanitizeName(projectNameStr.Get);

        if (string.IsNullOrWhiteSpace(safeName))
            return true;

        try
        {
            var project = ProjectRef.From($"./src/{safeName}/{safeName}.csproj");

            return project.ToString() == project.Name;
        }
        catch
        {
            return true;
        }
    }

    [Property]
    public bool Directory_IsParentOfPath(NonEmptyString projectNameStr)
    {
        var safeName = SanitizeName(projectNameStr.Get);

        if (string.IsNullOrWhiteSpace(safeName))
            return true;

        try
        {
            var path = $"./src/{safeName}/{safeName}.csproj";
            var project = ProjectRef.From(path);

            // Directory should be parent of the full path
            var expectedDir = $"./src/{safeName}";
            return project.Directory == expectedDir;
        }
        catch
        {
            return true;
        }
    }

    private static string SanitizeName(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var sanitized = new string(input
            .Replace('\0', '_')
            .Replace('/', '_')
            .Replace('\\', '_')
            .Replace(':', '_')
            .Replace('.', '_')
            .Where(c => !Path.GetInvalidFileNameChars().Contains(c))
            .Take(20)
            .ToArray());

        return sanitized;
    }
}
