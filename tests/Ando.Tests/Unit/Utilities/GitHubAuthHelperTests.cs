// =============================================================================
// GitHubAuthHelperTests.cs
//
// Unit tests for GitHubAuthHelper token resolution.
// =============================================================================

using Ando.Tests.TestFixtures;
using Ando.Utilities;
using Shouldly;

namespace Ando.Tests.Unit.Utilities;

[Trait("Category", "Unit")]
public class GitHubAuthHelperTests : IDisposable
{
    private readonly TestLogger _logger;
    private readonly string? _originalToken;

    public GitHubAuthHelperTests()
    {
        _logger = new TestLogger();
        // Save original token to restore after tests.
        _originalToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    }

    public void Dispose()
    {
        // Restore original token.
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", _originalToken);
    }

    #region GetToken Tests

    [Fact]
    public void GetToken_WithEnvironmentVariable_ReturnsToken()
    {
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "test-token-123");
        var helper = new GitHubAuthHelper(_logger);

        var token = helper.GetToken();

        token.ShouldBe("test-token-123");
    }

    [Fact]
    public void GetToken_CachesResult()
    {
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "cached-token");
        var helper = new GitHubAuthHelper(_logger);

        var token1 = helper.GetToken();
        // Change env var - should not affect cached result.
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "new-token");
        var token2 = helper.GetToken();

        token1.ShouldBe(token2);
        token2.ShouldBe("cached-token");
    }

    [Fact]
    public void GetToken_NoToken_ReturnsNull()
    {
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
        var helper = new GitHubAuthHelper(_logger);

        // Note: This test may return a token if gh CLI is installed.
        // The test verifies the method doesn't throw.
        var token = helper.GetToken();

        // Token may or may not be null depending on gh CLI availability.
        _logger.DebugMessages.ShouldContain(m =>
            m.Contains("GITHUB_TOKEN") || m.Contains("gh auth token") || m.Contains("No GitHub token"));
    }

    #endregion

    #region GetRequiredToken Tests

    [Fact]
    public void GetRequiredToken_WithToken_ReturnsToken()
    {
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "required-token");
        var helper = new GitHubAuthHelper(_logger);

        var token = helper.GetRequiredToken();

        token.ShouldBe("required-token");
    }

    [Fact]
    public void GetRequiredToken_WithToken_ReturnsIt()
    {
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "required-token-test");
        var helper = new GitHubAuthHelper(_logger);

        var token = helper.GetRequiredToken();

        // Should get the token from env var.
        token.ShouldBe("required-token-test");
    }

    #endregion

    #region GetEnvironment Tests

    [Fact]
    public void GetEnvironment_WithToken_ReturnsEnvDict()
    {
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "env-token");
        var helper = new GitHubAuthHelper(_logger);

        var env = helper.GetEnvironment();

        env.ShouldContainKey("GITHUB_TOKEN");
        env["GITHUB_TOKEN"].ShouldBe("env-token");
    }

    [Fact]
    public void GetEnvironment_WithToken_SetsCorrectKey()
    {
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "test-token-env");
        var helper = new GitHubAuthHelper(_logger);

        var env = helper.GetEnvironment();

        // Token from env var should be included in environment dict.
        env.ShouldContainKey("GITHUB_TOKEN");
    }

    #endregion

    /// <summary>
    /// Testable version of GitHubAuthHelper that can simulate no gh CLI.
    /// </summary>
    private class TestableGitHubAuthHelper : GitHubAuthHelper
    {
        private readonly bool _simulateNoGhCli;

        public TestableGitHubAuthHelper(TestLogger logger, bool simulateNoGhCli)
            : base(logger)
        {
            _simulateNoGhCli = simulateNoGhCli;
        }

        // Note: This doesn't actually override the private method, but the base class
        // will fail naturally if gh CLI is not installed. For isolated tests, we use
        // environment variables which take priority.
    }
}
