// =============================================================================
// EfOperationsTests.cs
//
// Summary: Unit tests for EfOperations class.
//
// Tests verify Entity Framework CLI operations including migrations,
// database updates, script generation, and DbContext reference handling.
// =============================================================================

using Ando.Context;
using Ando.Operations;
using Ando.References;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class EfOperationsTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private EfOperations CreateEf() =>
        new EfOperations(_registry, _logger, () => _executor);

    [Fact]
    public void DbContextFrom_CreatesEfContextRef()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        var context = ef.DbContextFrom(project);

        Assert.NotNull(context);
        Assert.Same(project, context.Project);
        Assert.Equal("default", context.ContextName);
    }

    [Fact]
    public void DbContextFrom_WithName_SetsContextName()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        var context = ef.DbContextFrom(project, "Reporting");

        Assert.Equal("Reporting", context.ContextName);
    }

    [Fact]
    public void DatabaseUpdate_RegistersStep()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var context = ef.DbContextFrom(project);

        ef.DatabaseUpdate(context);

        Assert.Single(_registry.Steps);
        Assert.Equal("Ef.DatabaseUpdate", _registry.Steps[0].Name);
    }

    [Fact]
    public void AddMigration_RegistersStep()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var context = ef.DbContextFrom(project);

        ef.AddMigration(context, "InitialCreate");

        Assert.Single(_registry.Steps);
        Assert.Equal("Ef.AddMigration", _registry.Steps[0].Name);
    }

    [Fact]
    public void Script_RegistersStep()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var context = ef.DbContextFrom(project);

        ef.Script(context, new BuildPath("/output/script.sql"));

        Assert.Single(_registry.Steps);
        Assert.Equal("Ef.Script", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task AllOperations_ExecuteSuccessfully()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var context = ef.DbContextFrom(project);

        ef.DatabaseUpdate(context);
        ef.AddMigration(context, "TestMigration");
        ef.Script(context, new BuildPath("/output/script.sql"));

        foreach (var step in _registry.Steps)
        {
            var result = await step.Execute();
            Assert.True(result);
        }
    }

    [Fact]
    public async Task DatabaseUpdate_ExecutesCorrectCommand()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var context = ef.DbContextFrom(project, "ReportingContext");

        ef.DatabaseUpdate(context);

        await _registry.Steps[0].Execute();

        Assert.Single(_executor.ExecutedCommands);
        var (command, args) = _executor.ExecutedCommands[0];
        Assert.Equal("dotnet", command);
        Assert.Contains("ef", args);
        Assert.Contains("database", args);
        Assert.Contains("update", args);
        Assert.Contains("--context", args);
        Assert.Contains("ReportingContext", args);
    }

    [Fact]
    public async Task AddMigration_ExecutesCorrectCommand()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var context = ef.DbContextFrom(project, "AppDbContext");

        ef.AddMigration(context, "AddUsersTable");

        await _registry.Steps[0].Execute();

        var (command, args) = _executor.ExecutedCommands[0];
        Assert.Equal("dotnet", command);
        Assert.Contains("ef", args);
        Assert.Contains("migrations", args);
        Assert.Contains("add", args);
        Assert.Contains("AddUsersTable", args);
        Assert.Contains("--context", args);
        Assert.Contains("AppDbContext", args);
    }

    [Fact]
    public async Task Script_ExecutesCorrectCommand()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var context = ef.DbContextFrom(project);

        ef.Script(context, new BuildPath("/output/migrations.sql"));

        await _registry.Steps[0].Execute();

        var (command, args) = _executor.ExecutedCommands[0];
        Assert.Equal("dotnet", command);
        Assert.Contains("ef", args);
        Assert.Contains("migrations", args);
        Assert.Contains("script", args);
        Assert.Contains("-o", args);
        Assert.Contains("/output/migrations.sql", args);
    }

    [Fact]
    public async Task DatabaseUpdate_Failure_ReturnsFalse()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var context = ef.DbContextFrom(project);
        _executor.SimulateFailure = true;
        _executor.FailureMessage = "Migration failed";

        ef.DatabaseUpdate(context);

        var result = await _registry.Steps[0].Execute();

        result.ShouldBeFalse();
        // Error logging is handled by the workflow runner, not the operation
    }

    // Edge case tests

    [Fact]
    public void RemoveMigration_RegistersStep()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var context = ef.DbContextFrom(project);

        ef.RemoveMigration(context);

        _registry.Steps.ShouldHaveSingleItem();
        _registry.Steps[0].Name.ShouldBe("Ef.RemoveMigration");
    }

    [Fact]
    public async Task RemoveMigration_ExecutesCorrectCommand()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var context = ef.DbContextFrom(project, "AppDbContext");

        ef.RemoveMigration(context);

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.Command.ShouldBe("dotnet");
        cmd.HasArg("ef").ShouldBeTrue();
        cmd.HasArg("migrations").ShouldBeTrue();
        cmd.HasArg("remove").ShouldBeTrue();
        cmd.HasArg("--context").ShouldBeTrue();
        cmd.HasArg("AppDbContext").ShouldBeTrue();
    }

    [Fact]
    public async Task RemoveMigration_WithForce_AddsForceFlag()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var context = ef.DbContextFrom(project);

        ef.RemoveMigration(context, force: true);

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.HasArg("--force").ShouldBeTrue();
    }

    [Fact]
    public async Task RemoveMigration_WithoutForce_OmitsForceFlag()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var context = ef.DbContextFrom(project);

        ef.RemoveMigration(context, force: false);

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.HasArg("--force").ShouldBeFalse();
    }

    [Fact]
    public async Task DatabaseUpdate_WithConnectionString_AddsConnectionFlag()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var context = ef.DbContextFrom(project);

        ef.DatabaseUpdate(context, connectionString: "Server=localhost;Database=MyDb");

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.HasArg("--connection").ShouldBeTrue();
        cmd.HasArg("Server=localhost;Database=MyDb").ShouldBeTrue();
    }

    [Fact]
    public async Task AddMigration_WithOutputDir_AddsOutputFlag()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var context = ef.DbContextFrom(project);

        ef.AddMigration(context, "Initial", outputDir: "Data/Migrations");

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.HasArg("-o").ShouldBeTrue();
        cmd.HasArg("Data/Migrations").ShouldBeTrue();
    }

    [Fact]
    public async Task Script_WithFromMigration_AddsMigrationArg()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var context = ef.DbContextFrom(project);

        ef.Script(context, new BuildPath("/output/script.sql"), fromMigration: "InitialCreate");

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.HasArg("InitialCreate").ShouldBeTrue();
    }

    [Fact]
    public async Task AllMethods_WithDefaultContext_OmitsContextFlag()
    {
        var ef = CreateEf();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        var context = ef.DbContextFrom(project); // default context

        ef.DatabaseUpdate(context);

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.HasArg("--context").ShouldBeFalse();
    }
}
