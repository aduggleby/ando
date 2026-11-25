// =============================================================================
// StepTreePrinterTests.cs
//
// Summary: Tests for the `ando run --dry-run` step tree and the run option check.
// =============================================================================

using Ando.Cli;
using Ando.Operations;
using Ando.Profiles;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Workflow;

namespace Ando.Tests.Unit.Cli;

[Trait("Category", "Unit")]
public class StepTreePrinterTests
{
    private readonly StepRegistry _registry = new();

    private AndoOperations CreateAndo() =>
        new(_registry, new TestLogger(), p => p, new BuildOptions(), new ArtifactOperations(new TestLogger()),
            new ProfileRegistry(), () => new MockExecutor());

    private void Step(string name, string? context = null) =>
        _registry.Register(name, () => Task.FromResult(true), context);

    [Fact]
    public void Render_NestedBlockWithOnFailureAndFinally_ShowsStructure()
    {
        var ando = CreateAndo();
        Step("Dotnet.Build", "App");
        ando.Block("eu", () => Step("Azure.Deploy", "eu-app"))
            .OnFailure(() => Step("Azure.Rollback", "eu-app"))
            .Finally(() => Step("Azure.Stop", "eu-slot"));
        Step("Dotnet.Publish");

        var lines = StepTreePrinter.Render(_registry.Root);

        lines.ShouldBe(
        [
            "Dotnet.Build (App)",
            "Block: eu",
            "  Azure.Deploy (eu-app)",
            "  on failure:",
            "    Azure.Rollback (eu-app)",
            "  finally (runs last-registered first):",
            "    Azure.Stop (eu-slot)",
            "Dotnet.Publish"
        ]);
    }

    [Fact]
    public void Render_TopLevelFinally_ListsLastRegisteredFirst()
    {
        var ando = CreateAndo();
        Step("Dotnet.Build");
        ando.Finally(() => Step("First.Cleanup"));
        ando.Finally(() => Step("Second.Cleanup"));

        var lines = StepTreePrinter.Render(_registry.Root);

        lines.ShouldBe(
        [
            "Dotnet.Build",
            "finally (runs last-registered first):",
            "  Second.Cleanup",
            "  First.Cleanup"
        ]);
    }

    [Fact]
    public void Render_WhenAndDefer_AreSingleLinesWithoutRunningTheirBodies()
    {
        var ando = CreateAndo();
        var ran = false;
        ando.When(() => ran = true, () => Step("Never.Shown"));
        ando.Defer(() => ran = true);

        var lines = StepTreePrinter.Render(_registry.Root);

        lines.ShouldBe(["When (decided at run time)", "Defer (runs C# at run time)"]);
        ran.ShouldBeFalse();
    }

    [Fact]
    public void Render_Require_IsMarkedCheckedAtRunTimeWithoutEvaluatingTheCondition()
    {
        var ando = CreateAndo();
        var evaluated = false;
        ando.Require(() => evaluated = true, "Database has pending migrations");
        ando.Block("eu", () => ando.Require(() => true, "Slot is healthy"));

        var lines = StepTreePrinter.Render(_registry.Root);

        lines.ShouldBe(
        [
            "Ando.Require (Database has pending migrations) (checked at run time)",
            "Block: eu",
            "  Ando.Require (Slot is healthy) (checked at run time)"
        ]);
        evaluated.ShouldBeFalse();
        // Real-run logs keep the plain display name.
        _registry.Steps[0].DisplayName.ShouldBe("Ando.Require (Database has pending migrations)");
    }

    [Theory]
    [InlineData("run", "-p", "release", "--dry-run", "--yes", "--ref", "v1", "--read-env")]
    [InlineData("-f", "x.csando", "--verbosity", "detailed", "--no-color", "--cold", "--dind", "--image", "img")]
    [InlineData("--profile", "a,b")]
    public void FindUnknownRunFlag_ValidOptions_ReturnsNull(params string[] args) =>
        AndoCli.FindUnknownRunFlag(args).ShouldBeNull();

    [Fact]
    public void FindUnknownRunFlag_Typo_IsReported() =>
        AndoCli.FindUnknownRunFlag(["run", "-p", "prod", "--dry-rn"]).ShouldBe("--dry-rn");

    [Fact]
    public void FindUnknownRunFlag_OptionValueStartingWithDashIsNotAFlag() =>
        AndoCli.FindUnknownRunFlag(["--image", "-weird"]).ShouldBeNull();
}
