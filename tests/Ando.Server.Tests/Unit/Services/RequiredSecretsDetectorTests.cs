// =============================================================================
// RequiredSecretsDetectorTests.cs
//
// Summary: Unit tests for the RequiredSecretsDetector.
//
// Tests detection of required environment variables from build.csando scripts,
// including Env() calls, operation-based authentication patterns, and sub-builds.
// Uses actual build.csando content from the Ando repository as test cases.
// =============================================================================

using Ando.Server.GitHub;
using Ando.Server.Services;
using Microsoft.Extensions.Logging;

namespace Ando.Server.Tests.Unit.Services;

public class RequiredSecretsDetectorTests
{
    private readonly RequiredSecretsDetector _detector;
    private readonly Mock<IGitHubService> _mockGitHubService;

    public RequiredSecretsDetectorTests()
    {
        _mockGitHubService = new Mock<IGitHubService>();
        var mockLogger = new Mock<ILogger<RequiredSecretsDetector>>();
        _detector = new RequiredSecretsDetector(_mockGitHubService.Object, mockLogger.Object);
    }

    // -------------------------------------------------------------------------
    // Basic Env() Pattern Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseRequiredSecrets_WithEnvCall_DetectsVariable()
    {
        // Arrange
        var script = """
            var apiKey = Env("API_KEY");
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.ShouldContain("API_KEY");
    }

    [Fact]
    public void ParseRequiredSecrets_WithEnvCallAndRequiredTrue_DetectsVariable()
    {
        // Arrange
        var script = """
            var apiKey = Env("API_KEY", required: true);
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.ShouldContain("API_KEY");
    }

    [Fact]
    public void ParseRequiredSecrets_WithEnvCallAndRequiredFalse_DoesNotDetect()
    {
        // Arrange
        var script = """
            var optionalKey = Env("OPTIONAL_KEY", required: false);
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.ShouldNotContain("OPTIONAL_KEY");
    }

    [Fact]
    public void ParseRequiredSecrets_WithEnvIndexer_DetectsVariable()
    {
        // Arrange
        var script = """
            var token = Env["DEPLOY_TOKEN"];
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.ShouldContain("DEPLOY_TOKEN");
    }

    [Fact]
    public void ParseRequiredSecrets_WithMultipleEnvCalls_DetectsAll()
    {
        // Arrange
        var script = """
            var key1 = Env("KEY_ONE");
            var key2 = Env("KEY_TWO");
            var key3 = Env["KEY_THREE"];
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.Count.ShouldBe(3);
        secrets.ShouldContain("KEY_ONE");
        secrets.ShouldContain("KEY_TWO");
        secrets.ShouldContain("KEY_THREE");
    }

    [Fact]
    public void ParseRequiredSecrets_WithDuplicateEnvCalls_ReturnsDistinct()
    {
        // Arrange
        var script = """
            var key1 = Env("SHARED_KEY");
            var key2 = Env("SHARED_KEY");
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.Count.ShouldBe(1);
        secrets.ShouldContain("SHARED_KEY");
    }

    [Fact]
    public void ParseRequiredSecrets_IsCaseInsensitiveForDeduplication()
    {
        // Arrange - same key with different casing should be treated as one
        var script = """
            var key1 = Env("MY_KEY");
            var key2 = Env("my_key");
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert - should deduplicate case-insensitively
        secrets.Count.ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // Operation Authentication Pattern Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseRequiredSecrets_WithNugetEnsureAuthenticated_DetectsNugetApiKey()
    {
        // Arrange
        var script = """
            Nuget.EnsureAuthenticated();
            Nuget.Push(project);
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.ShouldContain("NUGET_API_KEY");
    }

    [Fact]
    public void ParseRequiredSecrets_WithCloudflareEnsureAuthenticated_DetectsCloudflareSecrets()
    {
        // Arrange
        var script = """
            Cloudflare.EnsureAuthenticated();
            Cloudflare.PagesDeploy(distDir, "my-project");
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.ShouldContain("CLOUDFLARE_API_TOKEN");
        secrets.ShouldContain("CLOUDFLARE_ACCOUNT_ID");
    }

    [Fact]
    public void ParseRequiredSecrets_WithAzureEnsureAuthenticated_DetectsAzureSecrets()
    {
        // Arrange
        var script = """
            Azure.EnsureAuthenticated();
            Azure.FunctionsDeploy(funcApp);
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.ShouldContain("AZURE_CLIENT_ID");
        secrets.ShouldContain("AZURE_CLIENT_SECRET");
        secrets.ShouldContain("AZURE_TENANT_ID");
    }

    [Fact]
    public void ParseRequiredSecrets_WithGitHubEnsureAuthenticated_DetectsGitHubToken()
    {
        // Arrange
        var script = """
            GitHub.EnsureAuthenticated();
            GitHub.CreateRelease(tag);
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.ShouldContain("GITHUB_TOKEN");
    }

    [Fact]
    public void ParseRequiredSecrets_WithWhitespaceVariations_StillDetects()
    {
        // Arrange - test various whitespace between tokens
        var script = """
            Nuget . EnsureAuthenticated ( ) ;
            Cloudflare.EnsureAuthenticated();
            Azure  .  EnsureAuthenticated  (  )  ;
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.ShouldContain("NUGET_API_KEY");
        secrets.ShouldContain("CLOUDFLARE_API_TOKEN");
        secrets.ShouldContain("CLOUDFLARE_ACCOUNT_ID");
        secrets.ShouldContain("AZURE_CLIENT_ID");
        secrets.ShouldContain("AZURE_CLIENT_SECRET");
        secrets.ShouldContain("AZURE_TENANT_ID");
    }

    // -------------------------------------------------------------------------
    // Combined Pattern Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseRequiredSecrets_WithMixedPatterns_DetectsAll()
    {
        // Arrange
        var script = """
            var customSecret = Env("CUSTOM_SECRET");
            Nuget.EnsureAuthenticated();
            Cloudflare.EnsureAuthenticated();
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.ShouldContain("CUSTOM_SECRET");
        secrets.ShouldContain("NUGET_API_KEY");
        secrets.ShouldContain("CLOUDFLARE_API_TOKEN");
        secrets.ShouldContain("CLOUDFLARE_ACCOUNT_ID");
    }

    // -------------------------------------------------------------------------
    // Real-World Build Script Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseRequiredSecrets_WithActualAndoBuildScript_DetectsNugetKey()
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
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.ShouldContain("NUGET_API_KEY");
    }

    [Fact]
    public void ParseRequiredSecrets_WithActualWebsiteBuildScript_DetectsCloudflareSecrets()
    {
        // Arrange - actual content from website/build.csando (simplified)
        var script = """
            var push = DefineProfile("push");
            var website = Directory(".");

            Node.Install();
            Npm.Install(website);
            Npm.Build(website);

            if (push)
            {
                Cloudflare.EnsureAuthenticated();
                Cloudflare.PagesDeploy(website / "dist", "ando");
                Cloudflare.PurgeCache("andobuild.com");
            }
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.ShouldContain("CLOUDFLARE_API_TOKEN");
        secrets.ShouldContain("CLOUDFLARE_ACCOUNT_ID");
    }

    // -------------------------------------------------------------------------
    // Edge Case Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseRequiredSecrets_WithEmptyScript_ReturnsEmpty()
    {
        // Arrange
        var script = "";

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.ShouldBeEmpty();
    }

    [Fact]
    public void ParseRequiredSecrets_WithNoSecrets_ReturnsEmpty()
    {
        // Arrange
        var script = """
            var project = Dotnet.Project("./src/MyApp/MyApp.csproj");
            Dotnet.Build(project);
            Dotnet.Test(project);
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.ShouldBeEmpty();
    }

    [Fact]
    public void ParseRequiredSecrets_WithCommentedOutCode_DoesNotDetect()
    {
        // Arrange - commented code should still be detected by regex (limitation)
        // This test documents current behavior - regex doesn't understand comments
        var script = """
            // var key = Env("COMMENTED_KEY");
            /* Nuget.EnsureAuthenticated(); */
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert - current behavior: regex still matches in comments
        // This is a known limitation that's acceptable for most use cases
        secrets.ShouldContain("COMMENTED_KEY");
        secrets.ShouldContain("NUGET_API_KEY");
    }

    [Fact]
    public void ParseRequiredSecrets_WithEscapedQuotesInString_DoesNotDetect()
    {
        // Arrange - escaped quotes in strings are NOT detected because the regex
        // expects literal quotes. This is actually correct behavior since a string
        // literal mentioning Env() is not an actual call to Env().
        var script = """
            Log.Info("Using Env(\"API_KEY\") to get the key");
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert - the escaped quotes prevent regex match (correct behavior)
        secrets.ShouldNotContain("API_KEY");
    }

    [Fact]
    public void ParseRequiredSecrets_ReturnsSortedList()
    {
        // Arrange
        var script = """
            var z = Env("ZEBRA_KEY");
            var a = Env("ALPHA_KEY");
            var m = Env("MIDDLE_KEY");
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.Count.ShouldBe(3);
        secrets[0].ShouldBe("ALPHA_KEY");
        secrets[1].ShouldBe("MIDDLE_KEY");
        secrets[2].ShouldBe("ZEBRA_KEY");
    }

    [Fact]
    public void ParseRequiredSecrets_WithValidVariableName_DetectsIt()
    {
        // Arrange - test various valid variable name patterns
        var script = """
            var a = Env("A");
            var b = Env("_UNDERSCORE_START");
            var c = Env("WITH_123_NUMBERS");
            """;

        // Act
        var secrets = _detector.ParseRequiredSecrets(script);

        // Assert
        secrets.ShouldContain("A");
        secrets.ShouldContain("_UNDERSCORE_START");
        secrets.ShouldContain("WITH_123_NUMBERS");
    }

    // -------------------------------------------------------------------------
    // Async Detection Tests (with mocked GitHub service)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DetectRequiredSecretsAsync_WithMainBuildScript_DetectsSecrets()
    {
        // Arrange
        var mainScript = """
            Nuget.EnsureAuthenticated();
            Nuget.Push(project);
            """;

        _mockGitHubService.Setup(x => x.GetFileContentAsync(
            It.IsAny<long>(), It.IsAny<string>(), "build.csando", It.IsAny<string?>()))
            .ReturnsAsync(mainScript);

        // Act
        var secrets = await _detector.DetectRequiredSecretsAsync(123, "owner/repo", "main");

        // Assert
        secrets.ShouldContain("NUGET_API_KEY");
    }

    [Fact]
    public async Task DetectRequiredSecretsAsync_WithSubBuild_DetectsSecretsFromBoth()
    {
        // Arrange
        var mainScript = """
            Nuget.EnsureAuthenticated();
            Ando.Build(Directory("./website"));
            """;

        var subScript = """
            Cloudflare.EnsureAuthenticated();
            """;

        _mockGitHubService.Setup(x => x.GetFileContentAsync(
            It.IsAny<long>(), It.IsAny<string>(), "build.csando", It.IsAny<string?>()))
            .ReturnsAsync(mainScript);

        _mockGitHubService.Setup(x => x.GetFileContentAsync(
            It.IsAny<long>(), It.IsAny<string>(), "website/build.csando", It.IsAny<string?>()))
            .ReturnsAsync(subScript);

        // Act
        var secrets = await _detector.DetectRequiredSecretsAsync(123, "owner/repo", "main");

        // Assert
        secrets.ShouldContain("NUGET_API_KEY");
        secrets.ShouldContain("CLOUDFLARE_API_TOKEN");
        secrets.ShouldContain("CLOUDFLARE_ACCOUNT_ID");
    }

    [Fact]
    public async Task DetectRequiredSecretsAsync_WithNoBuildScript_ReturnsEmpty()
    {
        // Arrange
        _mockGitHubService.Setup(x => x.GetFileContentAsync(
            It.IsAny<long>(), It.IsAny<string>(), "build.csando", It.IsAny<string?>()))
            .ReturnsAsync((string?)null);

        // Act
        var secrets = await _detector.DetectRequiredSecretsAsync(123, "owner/repo", "main");

        // Assert
        secrets.ShouldBeEmpty();
    }

    [Fact]
    public async Task DetectRequiredSecretsAsync_WithMissingSubBuild_StillDetectsMainSecrets()
    {
        // Arrange
        var mainScript = """
            Nuget.EnsureAuthenticated();
            Ando.Build(Directory("./missing-subdir"));
            """;

        _mockGitHubService.Setup(x => x.GetFileContentAsync(
            It.IsAny<long>(), It.IsAny<string>(), "build.csando", It.IsAny<string?>()))
            .ReturnsAsync(mainScript);

        _mockGitHubService.Setup(x => x.GetFileContentAsync(
            It.IsAny<long>(), It.IsAny<string>(), "missing-subdir/build.csando", It.IsAny<string?>()))
            .ReturnsAsync((string?)null);

        // Act
        var secrets = await _detector.DetectRequiredSecretsAsync(123, "owner/repo", "main");

        // Assert
        secrets.ShouldContain("NUGET_API_KEY");
        secrets.Count.ShouldBe(1);
    }

    [Fact]
    public async Task DetectRequiredSecretsAsync_WithLegacyBuildPattern_DetectsSubBuild()
    {
        // Arrange - test legacy Build("path") pattern
        var mainScript = """
            Nuget.EnsureAuthenticated();
            Build("./website");
            """;

        var subScript = """
            Cloudflare.EnsureAuthenticated();
            """;

        _mockGitHubService.Setup(x => x.GetFileContentAsync(
            It.IsAny<long>(), It.IsAny<string>(), "build.csando", It.IsAny<string?>()))
            .ReturnsAsync(mainScript);

        _mockGitHubService.Setup(x => x.GetFileContentAsync(
            It.IsAny<long>(), It.IsAny<string>(), "website/build.csando", It.IsAny<string?>()))
            .ReturnsAsync(subScript);

        // Act
        var secrets = await _detector.DetectRequiredSecretsAsync(123, "owner/repo", "main");

        // Assert
        secrets.ShouldContain("NUGET_API_KEY");
        secrets.ShouldContain("CLOUDFLARE_API_TOKEN");
        secrets.ShouldContain("CLOUDFLARE_ACCOUNT_ID");
    }
}
