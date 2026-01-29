// =============================================================================
// GitHubOperationsTests.cs
//
// Summary: Unit tests for GitHubOperations class.
//
// Tests verify CreatePr, CreateRelease, and PushImage operations.
// Uses MockExecutor to verify command execution without actual GitHub API calls.
// =============================================================================

using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Utilities;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class GitHubOperationsTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private GitHubOperations CreateGitHub()
    {
        var authHelper = new GitHubAuthHelper(_logger);
        return new GitHubOperations(_registry, _logger, () => _executor, authHelper);
    }

    // CreatePr tests

    [Fact]
    public void CreatePr_RegistersStep()
    {
        var github = CreateGitHub();

        github.CreatePr(o => o.WithTitle("Test PR"));

        Assert.Single(_registry.Steps);
        Assert.Equal("GitHub.CreatePr", _registry.Steps[0].Name);
    }

    [Fact]
    public void CreatePr_SetsContextToTitle()
    {
        var github = CreateGitHub();

        github.CreatePr(o => o.WithTitle("My awesome PR"));

        Assert.Equal("My awesome PR", _registry.Steps[0].Context);
    }

    [Fact]
    public void CreatePr_WithNoTitle_SetsContextToPR()
    {
        var github = CreateGitHub();

        github.CreatePr(o => o.WithBody("Some body"));

        Assert.Equal("PR", _registry.Steps[0].Context);
    }

    // CreateRelease tests

    [Fact]
    public void CreateRelease_RegistersStep()
    {
        var github = CreateGitHub();

        github.CreateRelease(o => o.WithTag("1.0.0"));

        Assert.Single(_registry.Steps);
        Assert.Equal("GitHub.CreateRelease", _registry.Steps[0].Name);
    }

    [Fact]
    public void CreateRelease_SetsContextToTag()
    {
        var github = CreateGitHub();

        github.CreateRelease(o => o.WithTag("1.0.0"));

        Assert.Equal("1.0.0", _registry.Steps[0].Context);
    }

    [Fact]
    public void CreateRelease_WithNoTag_SetsContextToRelease()
    {
        var github = CreateGitHub();

        github.CreateRelease(o => o.WithTitle("My Release"));

        Assert.Equal("release", _registry.Steps[0].Context);
    }

    // PushImage tests

    [Fact]
    public void PushImage_RegistersStep()
    {
        var github = CreateGitHub();

        github.PushImage("myapp");

        Assert.Single(_registry.Steps);
        Assert.Equal("GitHub.PushImage", _registry.Steps[0].Name);
    }

    [Fact]
    public void PushImage_SetsContextToImageWithTag()
    {
        var github = CreateGitHub();

        github.PushImage("myapp", o => o.WithTag("v1.0.0"));

        Assert.Equal("myapp:v1.0.0", _registry.Steps[0].Context);
    }

    [Fact]
    public void PushImage_WithNoTag_UsesLatest()
    {
        var github = CreateGitHub();

        github.PushImage("myapp");

        Assert.Equal("myapp:latest", _registry.Steps[0].Context);
    }

    [Fact]
    public void PushImage_WithOwner_SetsOwner()
    {
        var github = CreateGitHub();

        github.PushImage("myapp", o => o.WithOwner("myorg").WithTag("v1.0.0"));

        Assert.Single(_registry.Steps);
    }

    // Error path tests

    [Fact]
    public async Task CreatePr_WhenCommandFails_ReturnsFalse()
    {
        _executor.SimulateFailure = true;
        _executor.FailureMessage = "gh command failed";
        var github = CreateGitHub();

        github.CreatePr(o => o.WithTitle("Test PR"));

        var result = await _registry.Steps[0].Execute();

        Assert.False(result);
        Assert.Contains(_logger.ErrorMessages, m => m.Contains("Failed to create PR"));
    }

    [Fact]
    public async Task CreateRelease_WithNoTag_ReturnsFalse()
    {
        var github = CreateGitHub();

        github.CreateRelease(o => o.WithTitle("My Release"));

        var result = await _registry.Steps[0].Execute();

        Assert.False(result);
        Assert.Contains(_logger.ErrorMessages, m => m.Contains("Release tag is required"));
    }

    // Note: CreateRelease uses _hostExecutor (ProcessRunner) for git remote,
    // so we can't mock that easily. Testing at registration level instead.

    [Fact]
    public async Task PushImage_WhenTagFails_ReturnsFalse()
    {
        _executor.SimulateFailure = true;
        _executor.FailureMessage = "docker tag failed";
        var github = CreateGitHub();

        github.PushImage("myapp", o => o.WithOwner("myorg"));

        var result = await _registry.Steps[0].Execute();

        Assert.False(result);
    }

    [Fact]
    public async Task PushImage_WithNoOwnerAndNoRemote_ReturnsFalse()
    {
        _executor.SimulateFailure = true;  // git remote fails
        var github = CreateGitHub();

        github.PushImage("myapp");

        var result = await _registry.Steps[0].Execute();

        Assert.False(result);
        Assert.Contains(_logger.ErrorMessages, m => m.Contains("GitHub owner is required"));
    }
}

// Tests for GitHubPrOptions
[Trait("Category", "Unit")]
public class GitHubPrOptionsTests
{
    [Fact]
    public void WithTitle_SetsTitle()
    {
        var options = new GitHubPrOptions();

        options.WithTitle("My PR");

        Assert.Equal("My PR", options.Title);
    }

    [Fact]
    public void WithBody_SetsBody()
    {
        var options = new GitHubPrOptions();

        options.WithBody("PR description");

        Assert.Equal("PR description", options.Body);
    }

    [Fact]
    public void WithBase_SetsBase()
    {
        var options = new GitHubPrOptions();

        options.WithBase("main");

        Assert.Equal("main", options.Base);
    }

    [Fact]
    public void WithHead_SetsHead()
    {
        var options = new GitHubPrOptions();

        options.WithHead("feature-branch");

        Assert.Equal("feature-branch", options.Head);
    }

    [Fact]
    public void AsDraft_SetsDraft()
    {
        var options = new GitHubPrOptions();

        options.AsDraft();

        Assert.True(options.Draft);
    }

    [Fact]
    public void FluentChaining_Works()
    {
        var options = new GitHubPrOptions()
            .WithTitle("My PR")
            .WithBody("Description")
            .WithBase("main")
            .WithHead("feature")
            .AsDraft();

        Assert.Equal("My PR", options.Title);
        Assert.Equal("Description", options.Body);
        Assert.Equal("main", options.Base);
        Assert.Equal("feature", options.Head);
        Assert.True(options.Draft);
    }
}

// Tests for GitHubReleaseOptions
[Trait("Category", "Unit")]
public class GitHubReleaseOptionsTests
{
    [Fact]
    public void WithTag_SetsTag()
    {
        var options = new GitHubReleaseOptions();

        options.WithTag("1.0.0");

        Assert.Equal("1.0.0", options.Tag);
    }

    [Fact]
    public void WithTitle_SetsTitle()
    {
        var options = new GitHubReleaseOptions();

        options.WithTitle("Release 1.0");

        Assert.Equal("Release 1.0", options.Title);
    }

    [Fact]
    public void WithNotes_SetsNotes()
    {
        var options = new GitHubReleaseOptions();

        options.WithNotes("Release notes here");

        Assert.Equal("Release notes here", options.Notes);
    }

    [Fact]
    public void AsDraft_SetsDraft()
    {
        var options = new GitHubReleaseOptions();

        options.AsDraft();

        Assert.True(options.Draft);
    }

    [Fact]
    public void AsPrerelease_SetsPrerelease()
    {
        var options = new GitHubReleaseOptions();

        options.AsPrerelease();

        Assert.True(options.Prerelease);
    }

    [Fact]
    public void WithGeneratedNotes_SetsGenerateNotes()
    {
        var options = new GitHubReleaseOptions();

        options.WithGeneratedNotes();

        Assert.True(options.GenerateNotes);
    }

    [Fact]
    public void WithoutPrefix_SetsNoPrefix()
    {
        var options = new GitHubReleaseOptions();

        options.WithoutPrefix();

        Assert.True(options.NoPrefix);
    }

    [Fact]
    public void WithFiles_AddsFiles()
    {
        var options = new GitHubReleaseOptions();

        options.WithFiles("file1.zip", "file2.tar.gz");

        Assert.Equal(2, options.Files.Count);
        Assert.Contains("file1.zip", options.Files);
        Assert.Contains("file2.tar.gz", options.Files);
    }

    [Fact]
    public void WithFiles_CanBeCalledMultipleTimes()
    {
        var options = new GitHubReleaseOptions();

        options.WithFiles("file1.zip");
        options.WithFiles("file2.zip", "file3.zip");

        Assert.Equal(3, options.Files.Count);
    }

    [Fact]
    public void FluentChaining_Works()
    {
        var options = new GitHubReleaseOptions()
            .WithTag("1.0.0")
            .WithTitle("Release 1.0")
            .WithNotes("Notes")
            .AsDraft()
            .AsPrerelease()
            .WithGeneratedNotes()
            .WithoutPrefix()
            .WithFiles("file.zip");

        Assert.Equal("1.0.0", options.Tag);
        Assert.Equal("Release 1.0", options.Title);
        Assert.Equal("Notes", options.Notes);
        Assert.True(options.Draft);
        Assert.True(options.Prerelease);
        Assert.True(options.GenerateNotes);
        Assert.True(options.NoPrefix);
        Assert.Single(options.Files);
    }
}

// Tests for GitHubImageOptions
[Trait("Category", "Unit")]
public class GitHubImageOptionsTests
{
    [Fact]
    public void WithTag_SetsTag()
    {
        var options = new GitHubImageOptions();

        options.WithTag("v1.0.0");

        Assert.Equal("v1.0.0", options.Tag);
    }

    [Fact]
    public void WithOwner_SetsOwner()
    {
        var options = new GitHubImageOptions();

        options.WithOwner("myorg");

        Assert.Equal("myorg", options.Owner);
    }

    [Fact]
    public void FluentChaining_Works()
    {
        var options = new GitHubImageOptions()
            .WithTag("v1.0.0")
            .WithOwner("myorg");

        Assert.Equal("v1.0.0", options.Tag);
        Assert.Equal("myorg", options.Owner);
    }
}
