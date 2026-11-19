// =============================================================================
// EfScriptArgsTests.cs
//
// Summary: Argument tests for Ef.Script (from/to, execution-time overload).
// `dotnet ef migrations script [from] [to]` is positional; "0" is the empty database.
// =============================================================================

using Ando.Context;
using Ando.Operations;
using Ando.References;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class EfScriptArgsTests
{
    private readonly StepRegistry _registry = new();
    private readonly MockExecutor _executor = new();

    private const string Prefix = "ef migrations script --project ./a.csproj -o /out.sql --idempotent";

    private async Task<string> RunAsync(Action<EfOperations, EfContextRef> register)
    {
        var ef = new EfOperations(_registry, new TestLogger(), () => _executor);
        register(ef, ef.DbContextFrom(ProjectRef.From("./a.csproj")));
        _registry.Steps[0].Name.ShouldBe("Ef.Script");
        (await _registry.Steps[0].Execute()).ShouldBeTrue();
        return string.Join(' ', _executor.ExecutedCommands[0].Args);
    }

    private static BuildPath Out => new("/out.sql");

    [Fact]
    public async Task NoMigrations_NoPositionalArgs() =>
        (await RunAsync((ef, c) => ef.Script(c, Out))).ShouldBe(Prefix);

    [Fact]
    public async Task From_IsPositional() =>
        (await RunAsync((ef, c) => ef.Script(c, Out, "M1"))).ShouldBe($"{Prefix} M1");

    [Fact]
    public async Task FromAndTo_AreBothPassed() =>
        (await RunAsync((ef, c) => ef.Script(c, Out, "M1", "M2"))).ShouldBe($"{Prefix} M1 M2");

    [Fact]
    public async Task ToWithoutFrom_UsesZeroAsFrom() =>
        (await RunAsync((ef, c) => ef.Script(c, Out, toMigration: "M2"))).ShouldBe($"{Prefix} 0 M2");

    [Fact]
    public async Task Options_IsResolvedWhenTheStepRuns()
    {
        var from = "early";
        var ef = new EfOperations(_registry, new TestLogger(), () => _executor);
        ef.Script(ef.DbContextFrom(ProjectRef.From("./a.csproj")), Out, o => o.From(() => from).To(() => "M9"));

        from = "late";
        (await _registry.Steps[0].Execute()).ShouldBeTrue();

        string.Join(' ', _executor.ExecutedCommands[0].Args).ShouldBe($"{Prefix} late M9");
    }

    [Fact]
    public async Task Options_EmptyFromWithTo_UsesZero() =>
        (await RunAsync((ef, c) => ef.Script(c, Out, o => o.From((Func<string?>)(() => null)).To(() => "M2")))).ShouldBe($"{Prefix} 0 M2");

    [Fact]
    public async Task Options_FromOnly() =>
        (await RunAsync((ef, c) => ef.Script(c, Out, o => o.From(() => "M1")))).ShouldBe($"{Prefix} M1");

    [Fact]
    public void ContextName_StepContextUnchanged()
    {
        var ef = new EfOperations(_registry, new TestLogger(), () => _executor);
        var ctx = ef.DbContextFrom(ProjectRef.From("./a.csproj"), "Main");
        ef.Script(ctx, Out, o => o.From(() => "M1"));

        _registry.Steps[0].Context.ShouldBe(ctx.ToString());
    }

    [Fact]
    public async Task Options_FixedStringsAndNullAreAccepted() =>
        (await RunAsync((ef, c) => ef.Script(c, Out, o => o.From("M1").To((string?)null)))).ShouldBe($"{Prefix} M1");

    [Fact]
    public async Task ScriptCompiles_WithNullAndNamedNullArguments()
    {
        var dir = Directory.CreateTempSubdirectory("ando-efscript-").FullName;
        try
        {
            var path = Path.Combine(dir, "build.csando");
            await File.WriteAllTextAsync(path, """
                var db = Ef.DbContextFrom(Dotnet.Project("./a.csproj"));
                Ef.Script(db, Root / "a.sql", null);
                Ef.Script(db, Root / "b.sql", fromMigration: null);
                Ef.Script(db, Root / "c.sql", null, "M2");
                Ef.Script(db, Root / "d.sql", o => o.From(null).To(() => "M2"));
                Ef.Script(db, Root / "e.sql", o => o.From("M1"));
                """);

            var errors = await new Ando.Scripting.ScriptHost(new TestLogger()).VerifyScriptAsync(path);

            errors.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
