// =============================================================================
// EfContextRefTests.cs
//
// Summary: Unit tests for EfContextRef class.
//
// Tests verify DbContext reference creation, project association,
// and context name handling.
// =============================================================================

using Ando.Operations;
using Ando.References;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.References;

[Trait("Category", "Unit")]
public class EfContextRefTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private EfOperations CreateEf() =>
        new EfOperations(_registry, _logger, () => _executor);

    [Fact]
    public void ToString_WithDefaultContext_ReturnsProjectName()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var efContext = ef.DbContextFrom(project);

        Assert.Equal("MyApp", efContext.ToString());
    }

    [Fact]
    public void ToString_WithNamedContext_ReturnsProjectAndContextName()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var efContext = ef.DbContextFrom(project, "Reporting");

        Assert.Equal("MyApp:Reporting", efContext.ToString());
    }

    [Fact]
    public void Project_ReturnsUnderlyingProject()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var efContext = ef.DbContextFrom(project, "Main");

        Assert.Same(project, efContext.Project);
    }

    [Fact]
    public void ContextName_ReturnsConfiguredName()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var efContext = ef.DbContextFrom(project, "CustomContext");

        Assert.Equal("CustomContext", efContext.ContextName);
    }
}
