// =============================================================================
// ProfileDetectorTests.cs
//
// Summary: Unit tests for the ProfileDetector.
//
// Tests detection of available profiles from build.csando scripts via
// DefineProfile() calls. Uses actual build.csando content patterns as test cases.
// =============================================================================

using Ando.Server.GitHub;
using Ando.Server.Services;
using Microsoft.Extensions.Logging;

namespace Ando.Server.Tests.Unit.Services;

public class ProfileDetectorTests
{
    private readonly ProfileDetector _detector;
    private readonly Mock<IGitHubService> _mockGitHubService;

    public ProfileDetectorTests()
    {
        _mockGitHubService = new Mock<IGitHubService>();
        var mockLogger = new Mock<ILogger<ProfileDetector>>();
        _detector = new ProfileDetector(_mockGitHubService.Object, mockLogger.Object);
    }

    // -------------------------------------------------------------------------
    // Basic DefineProfile() Pattern Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseProfiles_WithDefineProfileCall_DetectsProfile()
    {
        // Arrange
        var script = """
            var push = DefineProfile("push");
            """;

        // Act
        var profiles = _detector.ParseProfiles(script);

        // Assert
        profiles.ShouldContain("push");
    }

    [Fact]
    public void ParseProfiles_WithMultipleProfiles_DetectsAll()
    {
        // Arrange
        var script = """
            var push = DefineProfile("push");
            var deploy = DefineProfile("deploy");
            var test = DefineProfile("test");
            """;

        // Act
        var profiles = _detector.ParseProfiles(script);

        // Assert
        profiles.Count.ShouldBe(3);
        profiles.ShouldContain("push");
        profiles.ShouldContain("deploy");
        profiles.ShouldContain("test");
    }

    [Fact]
    public void ParseProfiles_WithDuplicateProfiles_ReturnsDistinct()
    {
        // Arrange
        var script = """
            var push1 = DefineProfile("push");
            var push2 = DefineProfile("push");
            """;

        // Act
        var profiles = _detector.ParseProfiles(script);

        // Assert
        profiles.Count.ShouldBe(1);
        profiles.ShouldContain("push");
    }

    [Fact]
    public void ParseProfiles_IsCaseInsensitiveForDeduplication()
    {
        // Arrange - same profile with different casing should be treated as one
        var script = """
            var push1 = DefineProfile("push");
            var push2 = DefineProfile("PUSH");
            """;

        // Act
        var profiles = _detector.ParseProfiles(script);

        // Assert - should deduplicate case-insensitively
        profiles.Count.ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // Profile Name Format Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseProfiles_WithHyphenatedName_DetectsProfile()
    {
        // Arrange
        var script = """
            var prod = DefineProfile("production-deploy");
            """;

        // Act
        var profiles = _detector.ParseProfiles(script);

        // Assert
        profiles.ShouldContain("production-deploy");
    }

    [Fact]
    public void ParseProfiles_WithUnderscoreName_DetectsProfile()
    {
        // Arrange
        var script = """
            var prod = DefineProfile("production_deploy");
            """;

        // Act
        var profiles = _detector.ParseProfiles(script);

        // Assert
        profiles.ShouldContain("production_deploy");
    }

    [Fact]
    public void ParseProfiles_WithNumbersInName_DetectsProfile()
    {
        // Arrange
        var script = """
            var v2 = DefineProfile("deploy-v2");
            """;

        // Act
        var profiles = _detector.ParseProfiles(script);

        // Assert
        profiles.ShouldContain("deploy-v2");
    }

    // -------------------------------------------------------------------------
    // Whitespace Variations
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseProfiles_WithWhitespaceVariations_StillDetects()
    {
        // Arrange - test various whitespace between tokens
        var script = """
            var push1 = DefineProfile("push1");
            var push2 = DefineProfile( "push2" );
            var push3 = DefineProfile  (  "push3"  )  ;
            """;

        // Act
        var profiles = _detector.ParseProfiles(script);

        // Assert
        profiles.Count.ShouldBe(3);
        profiles.ShouldContain("push1");
        profiles.ShouldContain("push2");
        profiles.ShouldContain("push3");
    }

    // -------------------------------------------------------------------------
    // Real-World Build Script Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseProfiles_WithActualAndoBuildScript_DetectsPushProfile()
    {
        // Arrange - actual content from build.csando (simplified)
        var script = """
            var push = DefineProfile("push");
            var project = Dotnet.Project("./src/Ando/Ando.csproj");

            Dotnet.SdkInstall();
            Dotnet.Restore(project);
            Dotnet.Build(project);
            Dotnet.Test(testProject);

            Nuget.Pack(project);

            if (push)
            {
                Nuget.EnsureAuthenticated();
                Nuget.Push(project);
                Ando.Build(Directory("./website"));
            }
            """;

        // Act
        var profiles = _detector.ParseProfiles(script);

        // Assert
        profiles.Count.ShouldBe(1);
        profiles.ShouldContain("push");
    }

    [Fact]
    public void ParseProfiles_WithMultipleProfilesInScript_DetectsAll()
    {
        // Arrange - script with multiple profiles
        var script = """
            var push = DefineProfile("push");
            var deploy = DefineProfile("deploy");
            var ci = DefineProfile("ci");

            Dotnet.Build(project);
            Dotnet.Test(testProject);

            if (ci || push)
            {
                Nuget.Pack(project);
            }

            if (push)
            {
                Nuget.Push(project);
            }

            if (deploy)
            {
                Cloudflare.PagesDeploy(dist, "my-site");
            }
            """;

        // Act
        var profiles = _detector.ParseProfiles(script);

        // Assert
        profiles.Count.ShouldBe(3);
        profiles.ShouldContain("push");
        profiles.ShouldContain("deploy");
        profiles.ShouldContain("ci");
    }

    // -------------------------------------------------------------------------
    // Edge Case Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseProfiles_WithEmptyScript_ReturnsEmpty()
    {
        // Arrange
        var script = "";

        // Act
        var profiles = _detector.ParseProfiles(script);

        // Assert
        profiles.ShouldBeEmpty();
    }

    [Fact]
    public void ParseProfiles_WithNoProfiles_ReturnsEmpty()
    {
        // Arrange
        var script = """
            var project = Dotnet.Project("./src/MyApp/MyApp.csproj");
            Dotnet.Build(project);
            Dotnet.Test(project);
            """;

        // Act
        var profiles = _detector.ParseProfiles(script);

        // Assert
        profiles.ShouldBeEmpty();
    }

    [Fact]
    public void ParseProfiles_WithCommentedOutCode_StillDetects()
    {
        // Arrange - commented code should still be detected by regex (limitation)
        // This test documents current behavior - regex doesn't understand comments
        var script = """
            // var push = DefineProfile("commented-out");
            /* DefineProfile("block-comment"); */
            """;

        // Act
        var profiles = _detector.ParseProfiles(script);

        // Assert - current behavior: regex still matches in comments
        // This is a known limitation that's acceptable for most use cases
        profiles.ShouldContain("commented-out");
        profiles.ShouldContain("block-comment");
    }

    [Fact]
    public void ParseProfiles_ReturnsSortedList()
    {
        // Arrange
        var script = """
            var z = DefineProfile("zebra");
            var a = DefineProfile("alpha");
            var m = DefineProfile("middle");
            """;

        // Act
        var profiles = _detector.ParseProfiles(script);

        // Assert
        profiles.Count.ShouldBe(3);
        profiles[0].ShouldBe("alpha");
        profiles[1].ShouldBe("middle");
        profiles[2].ShouldBe("zebra");
    }

    // -------------------------------------------------------------------------
    // Async Detection Tests (with mocked GitHub service)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DetectProfilesAsync_WithMainBuildScript_DetectsProfiles()
    {
        // Arrange
        var mainScript = """
            var push = DefineProfile("push");
            Nuget.Push(project);
            """;

        _mockGitHubService.Setup(x => x.GetFileContentAsync(
            It.IsAny<long>(), It.IsAny<string>(), "build.csando", It.IsAny<string?>()))
            .ReturnsAsync(mainScript);

        // Act
        var profiles = await _detector.DetectProfilesAsync(123, "owner/repo", "main");

        // Assert
        profiles.ShouldContain("push");
    }

    [Fact]
    public async Task DetectProfilesAsync_WithNoBuildScript_ReturnsEmpty()
    {
        // Arrange
        _mockGitHubService.Setup(x => x.GetFileContentAsync(
            It.IsAny<long>(), It.IsAny<string>(), "build.csando", It.IsAny<string?>()))
            .ReturnsAsync((string?)null);

        // Act
        var profiles = await _detector.DetectProfilesAsync(123, "owner/repo", "main");

        // Assert
        profiles.ShouldBeEmpty();
    }

    [Fact]
    public async Task DetectProfilesAsync_WithEmptyBuildScript_ReturnsEmpty()
    {
        // Arrange
        _mockGitHubService.Setup(x => x.GetFileContentAsync(
            It.IsAny<long>(), It.IsAny<string>(), "build.csando", It.IsAny<string?>()))
            .ReturnsAsync("");

        // Act
        var profiles = await _detector.DetectProfilesAsync(123, "owner/repo", "main");

        // Assert
        profiles.ShouldBeEmpty();
    }

    [Fact]
    public async Task DetectProfilesAsync_WithMultipleProfiles_ReturnsAllSorted()
    {
        // Arrange
        var mainScript = """
            var deploy = DefineProfile("deploy");
            var push = DefineProfile("push");
            var ci = DefineProfile("ci");
            """;

        _mockGitHubService.Setup(x => x.GetFileContentAsync(
            It.IsAny<long>(), It.IsAny<string>(), "build.csando", It.IsAny<string?>()))
            .ReturnsAsync(mainScript);

        // Act
        var profiles = await _detector.DetectProfilesAsync(123, "owner/repo", "main");

        // Assert
        profiles.Count.ShouldBe(3);
        profiles[0].ShouldBe("ci");
        profiles[1].ShouldBe("deploy");
        profiles[2].ShouldBe("push");
    }
}
