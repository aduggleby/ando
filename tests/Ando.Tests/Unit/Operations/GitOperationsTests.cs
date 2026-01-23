// =============================================================================
// GitOperationsTests.cs
//
// Unit tests for GitOperations build script commands (step registration only).
// Note: Execution tests require integration testing since GitOperations uses
// a real ProcessRunner internally for host git commands.
// =============================================================================

using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Shouldly;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class GitOperationsTests
{
    private readonly StepRegistry _registry;
    private readonly TestLogger _logger;
    private readonly MockExecutor _executor;
    private readonly GitOperations _git;

    public GitOperationsTests()
    {
        _registry = new StepRegistry();
        _logger = new TestLogger();
        _executor = new MockExecutor();
        _git = new GitOperations(_registry, _logger, () => _executor);
    }

    #region Tag Registration Tests

    [Fact]
    public void Tag_RegistersStep()
    {
        _git.Tag("v1.0.0");

        _registry.Steps.ShouldContain(s => s.Name == "Git.Tag");
    }

    [Fact]
    public void Tag_SetsContextToTagName()
    {
        _git.Tag("v1.2.3");

        var step = _registry.Steps.First(s => s.Name == "Git.Tag");
        step.Context.ShouldBe("v1.2.3");
    }

    [Fact]
    public void Tag_DefaultsToAnnotated()
    {
        var options = new GitTagOptions();

        options.Annotated.ShouldBeTrue();
    }

    #endregion

    #region Push Registration Tests

    [Fact]
    public void Push_RegistersStep()
    {
        _git.Push();

        _registry.Steps.ShouldContain(s => s.Name == "Git.Push");
    }

    [Fact]
    public void Push_MultipleCallsRegisterMultipleSteps()
    {
        _git.Push();
        _git.Push(opt => opt.ToRemote("upstream"));

        _registry.Steps.Count(s => s.Name == "Git.Push").ShouldBe(2);
    }

    #endregion

    #region PushTags Registration Tests

    [Fact]
    public void PushTags_RegistersStep()
    {
        _git.PushTags();

        _registry.Steps.ShouldContain(s => s.Name == "Git.PushTags");
    }

    [Fact]
    public void PushTags_SetsContextToRemote()
    {
        _git.PushTags("upstream");

        var step = _registry.Steps.First(s => s.Name == "Git.PushTags");
        step.Context.ShouldBe("upstream");
    }

    #endregion

    #region Add Registration Tests

    [Fact]
    public void Add_RegistersStep()
    {
        _git.Add("file.txt");

        _registry.Steps.ShouldContain(s => s.Name == "Git.Add");
    }

    [Fact]
    public void Add_SetsContextToFiles()
    {
        _git.Add("file1.txt", "file2.txt");

        var step = _registry.Steps.First(s => s.Name == "Git.Add");
        step.Context.ShouldContain("file1.txt");
        step.Context.ShouldContain("file2.txt");
    }

    #endregion

    #region Commit Registration Tests

    [Fact]
    public void Commit_RegistersStep()
    {
        _git.Commit("Test message");

        _registry.Steps.ShouldContain(s => s.Name == "Git.Commit");
    }

    #endregion

    #region Options Fluent API Tests

    [Fact]
    public void GitTagOptions_FluentApi_Chainable()
    {
        var options = new GitTagOptions()
            .WithMessage("Message")
            .WithSkipIfExists()
            .AsLightweight();

        options.Message.ShouldBe("Message");
        options.SkipIfExists.ShouldBeTrue();
        options.Annotated.ShouldBeFalse();
    }

    [Fact]
    public void GitPushOptions_FluentApi_Chainable()
    {
        var options = new GitPushOptions()
            .ToRemote("origin")
            .ToBranch("main")
            .WithUpstream();

        options.Remote.ShouldBe("origin");
        options.Branch.ShouldBe("main");
        options.SetUpstream.ShouldBeTrue();
    }

    [Fact]
    public void GitCommitOptions_FluentApi_Chainable()
    {
        var options = new GitCommitOptions().WithAllowEmpty();

        options.AllowEmpty.ShouldBeTrue();
    }

    [Fact]
    public void GitTagOptions_Defaults()
    {
        var options = new GitTagOptions();

        options.Annotated.ShouldBeTrue();
        options.Message.ShouldBeNull();
        options.SkipIfExists.ShouldBeFalse();
    }

    [Fact]
    public void GitPushOptions_Defaults()
    {
        var options = new GitPushOptions();

        options.Remote.ShouldBeNull();
        options.Branch.ShouldBeNull();
        options.SetUpstream.ShouldBeFalse();
        options.Force.ShouldBeFalse();
    }

    [Fact]
    public void GitCommitOptions_Defaults()
    {
        var options = new GitCommitOptions();

        options.AllowEmpty.ShouldBeFalse();
    }

    #endregion
}
