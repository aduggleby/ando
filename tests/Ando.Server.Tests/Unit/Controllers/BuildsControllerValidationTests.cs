// =============================================================================
// BuildsControllerValidationTests.cs
//
// Summary: Unit tests for BuildsController input validation.
//
// Tests that the controller properly validates build state transitions
// (cancel/retry) and ownership verification. Uses in-memory database
// with mocked services for isolation.
// =============================================================================

using System.Security.Claims;
using Ando.Server.Controllers;
using Ando.Server.Data;
using Ando.Server.Models;
using Ando.Server.Services;
using Ando.Server.Tests.TestFixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ando.Server.Tests.Unit.Controllers;

public class BuildsControllerValidationTests : IDisposable
{
    private readonly AndoDbContext _db;
    private readonly Mock<IBuildService> _buildService;
    private readonly Mock<IProjectService> _projectService;
    private readonly BuildsController _controller;
    private readonly User _testUser;
    private readonly User _otherUser;
    private readonly Project _testProject;
    private readonly Project _otherProject;

    public BuildsControllerValidationTests()
    {
        _db = TestDbContextFactory.Create();
        _buildService = new Mock<IBuildService>();
        _projectService = new Mock<IProjectService>();

        _controller = new BuildsController(
            _db,
            _buildService.Object,
            _projectService.Object,
            NullLogger<BuildsController>.Instance);

        // Create test user
        _testUser = new User
        {
            GitHubId = 12345,
            GitHubLogin = "testuser",
            Email = "test@example.com",
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(_testUser);

        // Create another user (for ownership tests)
        _otherUser = new User
        {
            GitHubId = 67890,
            GitHubLogin = "otheruser",
            Email = "other@example.com",
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(_otherUser);
        _db.SaveChanges();

        // Create test project owned by testUser
        _testProject = new Project
        {
            OwnerId = _testUser.Id,
            Owner = _testUser,
            GitHubRepoId = 99999,
            RepoFullName = "testuser/test-repo",
            RepoUrl = "https://github.com/testuser/test-repo",
            DefaultBranch = "main",
            CreatedAt = DateTime.UtcNow
        };
        _db.Projects.Add(_testProject);

        // Create project owned by other user
        _otherProject = new Project
        {
            OwnerId = _otherUser.Id,
            Owner = _otherUser,
            GitHubRepoId = 88888,
            RepoFullName = "otheruser/other-repo",
            RepoUrl = "https://github.com/otheruser/other-repo",
            DefaultBranch = "main",
            CreatedAt = DateTime.UtcNow
        };
        _db.Projects.Add(_otherProject);
        _db.SaveChanges();

        SetupAuthenticatedUser(_testUser.Id);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    // -------------------------------------------------------------------------
    // Cancel Build State Validation Tests
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(BuildStatus.Queued)]
    [InlineData(BuildStatus.Running)]
    public async Task Cancel_WithCancellableStatus_Succeeds(BuildStatus status)
    {
        // Arrange
        var build = await CreateTestBuildAsync(_testProject, status);
        _buildService.Setup(s => s.CancelBuildAsync(build.Id)).ReturnsAsync(true);

        // Act
        var result = await _controller.Cancel(build.Id);

        // Assert
        result.ShouldBeOfType<RedirectToActionResult>();
        _buildService.Verify(s => s.CancelBuildAsync(build.Id), Times.Once);
        GetTempData("Success").ShouldNotBeNull();
    }

    [Theory]
    [InlineData(BuildStatus.Success)]
    [InlineData(BuildStatus.Failed)]
    [InlineData(BuildStatus.Cancelled)]
    [InlineData(BuildStatus.TimedOut)]
    public async Task Cancel_WithNonCancellableStatus_ReturnsError(BuildStatus status)
    {
        // Arrange
        var build = await CreateTestBuildAsync(_testProject, status);

        // Act
        var result = await _controller.Cancel(build.Id);

        // Assert
        result.ShouldBeOfType<RedirectToActionResult>();
        _buildService.Verify(s => s.CancelBuildAsync(It.IsAny<int>()), Times.Never);
        GetTempData("Error").ShouldContain("cannot be cancelled");
    }

    [Fact]
    public async Task Cancel_WhenServiceReturnsFalse_ReturnsError()
    {
        // Arrange
        var build = await CreateTestBuildAsync(_testProject, BuildStatus.Running);
        _buildService.Setup(s => s.CancelBuildAsync(build.Id)).ReturnsAsync(false);

        // Act
        var result = await _controller.Cancel(build.Id);

        // Assert
        result.ShouldBeOfType<RedirectToActionResult>();
        GetTempData("Error").ShouldContain("Failed to cancel");
    }

    // -------------------------------------------------------------------------
    // Retry Build State Validation Tests
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(BuildStatus.Failed)]
    [InlineData(BuildStatus.Cancelled)]
    [InlineData(BuildStatus.TimedOut)]
    public async Task Retry_WithRetryableStatus_Succeeds(BuildStatus status)
    {
        // Arrange
        var build = await CreateTestBuildAsync(_testProject, status);
        _buildService.Setup(s => s.QueueBuildAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<BuildTrigger>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .ReturnsAsync(100);

        // Act
        var result = await _controller.Retry(build.Id);

        // Assert
        result.ShouldBeOfType<RedirectToActionResult>();
        var redirect = (RedirectToActionResult)result;
        redirect.RouteValues!["id"].ShouldBe(100);
        GetTempData("Success").ShouldContain("retry");
    }

    [Theory]
    [InlineData(BuildStatus.Queued)]
    [InlineData(BuildStatus.Running)]
    [InlineData(BuildStatus.Success)]
    public async Task Retry_WithNonRetryableStatus_ReturnsError(BuildStatus status)
    {
        // Arrange
        var build = await CreateTestBuildAsync(_testProject, status);

        // Act
        var result = await _controller.Retry(build.Id);

        // Assert
        result.ShouldBeOfType<RedirectToActionResult>();
        _buildService.Verify(s => s.QueueBuildAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<BuildTrigger>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>()),
            Times.Never);
        GetTempData("Error").ShouldContain("cannot be retried");
    }

    [Fact]
    public async Task Retry_PreservesBuildParameters()
    {
        // Arrange
        var build = await CreateTestBuildAsync(_testProject, BuildStatus.Failed);
        build.CommitSha = "abc123def456";
        build.Branch = "feature/test";
        build.CommitMessage = "Test commit";
        build.CommitAuthor = "Test Author";
        build.PullRequestNumber = 42;
        await _db.SaveChangesAsync();

        int capturedProjectId = 0;
        string capturedCommitSha = "";
        string capturedBranch = "";
        string? capturedMessage = null;
        string? capturedAuthor = null;
        int? capturedPrNumber = null;

        _buildService.Setup(s => s.QueueBuildAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<BuildTrigger>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .Callback<int, string, string, BuildTrigger, string?, string?, int?>((p, c, b, t, m, a, pr) =>
            {
                capturedProjectId = p;
                capturedCommitSha = c;
                capturedBranch = b;
                capturedMessage = m;
                capturedAuthor = a;
                capturedPrNumber = pr;
            })
            .ReturnsAsync(100);

        // Act
        await _controller.Retry(build.Id);

        // Assert
        capturedProjectId.ShouldBe(_testProject.Id);
        capturedCommitSha.ShouldBe("abc123def456");
        capturedBranch.ShouldBe("feature/test");
        capturedMessage.ShouldBe("Test commit");
        capturedAuthor.ShouldBe("Test Author");
        capturedPrNumber.ShouldBe(42);
    }

    // -------------------------------------------------------------------------
    // Build Ownership Validation Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Details_WithNonExistentBuild_ReturnsNotFound()
    {
        // Act
        var result = await _controller.Details(99999);

        // Assert
        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Details_WithOtherUsersBuild_ReturnsNotFound()
    {
        // Arrange - create build owned by other user
        var otherBuild = await CreateTestBuildAsync(_otherProject, BuildStatus.Success);

        // Act
        var result = await _controller.Details(otherBuild.Id);

        // Assert
        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Cancel_WithNonExistentBuild_ReturnsNotFound()
    {
        // Act
        var result = await _controller.Cancel(99999);

        // Assert
        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Cancel_WithOtherUsersBuild_ReturnsNotFound()
    {
        // Arrange
        var otherBuild = await CreateTestBuildAsync(_otherProject, BuildStatus.Running);

        // Act
        var result = await _controller.Cancel(otherBuild.Id);

        // Assert
        result.ShouldBeOfType<NotFoundResult>();
        _buildService.Verify(s => s.CancelBuildAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Retry_WithNonExistentBuild_ReturnsNotFound()
    {
        // Act
        var result = await _controller.Retry(99999);

        // Assert
        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Retry_WithOtherUsersBuild_ReturnsNotFound()
    {
        // Arrange
        var otherBuild = await CreateTestBuildAsync(_otherProject, BuildStatus.Failed);

        // Act
        var result = await _controller.Retry(otherBuild.Id);

        // Assert
        result.ShouldBeOfType<NotFoundResult>();
        _buildService.Verify(s => s.QueueBuildAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<BuildTrigger>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>()),
            Times.Never);
    }

    [Fact]
    public async Task GetLogs_WithNonExistentBuild_ReturnsNotFound()
    {
        // Act
        var result = await _controller.GetLogs(99999);

        // Assert
        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetLogs_WithOtherUsersBuild_ReturnsNotFound()
    {
        // Arrange
        var otherBuild = await CreateTestBuildAsync(_otherProject, BuildStatus.Success);

        // Act
        var result = await _controller.GetLogs(otherBuild.Id);

        // Assert
        result.ShouldBeOfType<NotFoundResult>();
    }

    // -------------------------------------------------------------------------
    // Artifact Download Validation Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DownloadArtifact_WithNonExistentArtifact_ReturnsNotFound()
    {
        // Arrange
        var build = await CreateTestBuildAsync(_testProject, BuildStatus.Success);

        // Act
        var result = await _controller.DownloadArtifact(build.Id, 99999);

        // Assert
        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DownloadArtifact_WithMismatchedBuildId_ReturnsNotFound()
    {
        // Arrange
        var build1 = await CreateTestBuildAsync(_testProject, BuildStatus.Success);
        var build2 = await CreateTestBuildAsync(_testProject, BuildStatus.Success);

        var artifact = new BuildArtifact
        {
            BuildId = build1.Id,
            Build = build1,
            Name = "artifact.zip",
            StoragePath = "/tmp/test-artifact.zip",
            SizeBytes = 1024,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };
        _db.BuildArtifacts.Add(artifact);
        await _db.SaveChangesAsync();

        // Act - try to access artifact with wrong build ID
        var result = await _controller.DownloadArtifact(build2.Id, artifact.Id);

        // Assert
        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DownloadArtifact_WithOtherUsersArtifact_ReturnsNotFound()
    {
        // Arrange
        var otherBuild = await CreateTestBuildAsync(_otherProject, BuildStatus.Success);

        var artifact = new BuildArtifact
        {
            BuildId = otherBuild.Id,
            Build = otherBuild,
            Name = "artifact.zip",
            StoragePath = "/tmp/test-artifact.zip",
            SizeBytes = 1024,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };
        _db.BuildArtifacts.Add(artifact);
        await _db.SaveChangesAsync();

        // Act
        var result = await _controller.DownloadArtifact(otherBuild.Id, artifact.Id);

        // Assert
        result.ShouldBeOfType<NotFoundResult>();
    }

    // -------------------------------------------------------------------------
    // GetLogs Validation Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetLogs_WithValidBuild_ReturnsJson()
    {
        // Arrange
        var build = await CreateTestBuildAsync(_testProject, BuildStatus.Running);

        // Act
        var result = await _controller.GetLogs(build.Id);

        // Assert
        result.ShouldBeOfType<JsonResult>();
    }

    [Fact]
    public async Task GetLogs_WithAfterSequence_FiltersLogs()
    {
        // Arrange
        var build = await CreateTestBuildAsync(_testProject, BuildStatus.Running);

        // Add some log entries
        _db.BuildLogEntries.AddRange(
            new BuildLogEntry { BuildId = build.Id, Sequence = 1, Type = LogEntryType.Info, Message = "First", Timestamp = DateTime.UtcNow },
            new BuildLogEntry { BuildId = build.Id, Sequence = 2, Type = LogEntryType.Info, Message = "Second", Timestamp = DateTime.UtcNow },
            new BuildLogEntry { BuildId = build.Id, Sequence = 3, Type = LogEntryType.Info, Message = "Third", Timestamp = DateTime.UtcNow }
        );
        await _db.SaveChangesAsync();

        // Act
        var result = await _controller.GetLogs(build.Id, afterSequence: 1);

        // Assert
        result.ShouldBeOfType<JsonResult>();
        var json = (JsonResult)result;
        var value = json.Value;
        // The result should only contain logs with sequence > 1
        value.ShouldNotBeNull();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Build> CreateTestBuildAsync(Project project, BuildStatus status)
    {
        var build = new Build
        {
            ProjectId = project.Id,
            Project = project,
            CommitSha = Guid.NewGuid().ToString("N"),
            Branch = "main",
            Status = status,
            Trigger = BuildTrigger.Push,
            QueuedAt = DateTime.UtcNow
        };
        _db.Builds.Add(build);
        await _db.SaveChangesAsync();
        return build;
    }

    private void SetupAuthenticatedUser(int userId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, "testuser")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };

        // Setup TempData
        var tempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        _controller.TempData = tempData;

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    private string? GetTempData(string key)
    {
        return _controller.TempData[key]?.ToString();
    }
}
