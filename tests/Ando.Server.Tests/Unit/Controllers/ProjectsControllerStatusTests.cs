// =============================================================================
// ProjectsControllerStatusTests.cs
//
// Summary: Unit tests for the ProjectsController Status action.
//
// Tests the status page functionality including deployment status calculation,
// sorting by various fields, and handling of edge cases like empty project lists.
//
// Design Decisions:
// - Uses in-memory database for realistic querying behavior
// - Tests all three deployment statuses (NotDeployed, Failed, Deployed)
// - Verifies all sort field and direction combinations
// =============================================================================

using System.Security.Claims;
using Ando.Server.Configuration;
using Ando.Server.Controllers;
using Ando.Server.GitHub;
using Ando.Server.Models;
using Ando.Server.Services;
using Ando.Server.Tests.TestFixtures;
using Ando.Server.ViewModels;
using SortDirection = Ando.Server.ViewModels.SortDirection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Ando.Server.Tests.Unit.Controllers;

public class ProjectsControllerStatusTests : IDisposable
{
    private readonly Data.AndoDbContext _db;
    private readonly Mock<IProjectService> _projectService;
    private readonly Mock<IBuildService> _buildService;
    private readonly Mock<IGitHubService> _gitHubService;
    private readonly ProjectsController _controller;
    private readonly ApplicationUser _testUser;

    public ProjectsControllerStatusTests()
    {
        _db = TestDbContextFactory.Create();
        _projectService = new Mock<IProjectService>();
        _buildService = new Mock<IBuildService>();
        _gitHubService = new Mock<IGitHubService>();

        var gitHubSettings = Options.Create(new GitHubSettings());
        _controller = new ProjectsController(
            _db,
            _projectService.Object,
            _buildService.Object,
            _gitHubService.Object,
            gitHubSettings,
            NullLogger<ProjectsController>.Instance);

        // Create test user
        _testUser = new ApplicationUser
        {
            Id = 1,
            GitHubId = 12345,
            GitHubLogin = "testuser",
            Email = "test@example.com",
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(_testUser);
        _db.SaveChanges();

        SetupAuthenticatedUser(_testUser.Id);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    // -------------------------------------------------------------------------
    // Basic Status Action Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Status_WithNoProjects_ReturnsEmptyList()
    {
        // Arrange
        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project>());

        // Act
        var result = await _controller.Status();

        // Assert
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.Projects.ShouldBeEmpty();
    }

    [Fact]
    public async Task Status_WithProjects_ReturnsProjectList()
    {
        // Arrange
        var project = CreateProject(1, "user/repo-a");
        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project> { project });

        // Act
        var result = await _controller.Status();

        // Assert
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.Projects.Count.ShouldBe(1);
        model.Projects[0].RepoFullName.ShouldBe("user/repo-a");
    }

    // -------------------------------------------------------------------------
    // Deployment Status Calculation Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Status_WithNoBuilds_ReturnsNotDeployedStatus()
    {
        // Arrange
        var project = CreateProject(1, "user/repo");
        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project> { project });

        // Act
        var result = await _controller.Status();

        // Assert
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.Projects[0].DeploymentStatus.ShouldBe(DeploymentStatus.NotDeployed);
    }

    [Fact]
    public async Task Status_WithSuccessfulBuild_ReturnsDeployedStatus()
    {
        // Arrange
        var project = CreateProject(1, "user/repo");
        CreateBuild(project, BuildStatus.Success);
        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project> { project });

        // Act
        var result = await _controller.Status();

        // Assert
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.Projects[0].DeploymentStatus.ShouldBe(DeploymentStatus.Deployed);
    }

    [Fact]
    public async Task Status_WithFailedBuild_ReturnsFailedStatus()
    {
        // Arrange
        var project = CreateProject(1, "user/repo");
        CreateBuild(project, BuildStatus.Failed);
        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project> { project });

        // Act
        var result = await _controller.Status();

        // Assert
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.Projects[0].DeploymentStatus.ShouldBe(DeploymentStatus.Failed);
    }

    [Fact]
    public async Task Status_WithTimedOutBuild_ReturnsFailedStatus()
    {
        // Arrange
        var project = CreateProject(1, "user/repo");
        CreateBuild(project, BuildStatus.TimedOut);
        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project> { project });

        // Act
        var result = await _controller.Status();

        // Assert
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.Projects[0].DeploymentStatus.ShouldBe(DeploymentStatus.Failed);
    }

    [Fact]
    public async Task Status_WithQueuedBuild_ReturnsNotDeployedStatus()
    {
        // Arrange
        var project = CreateProject(1, "user/repo");
        CreateBuild(project, BuildStatus.Queued);
        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project> { project });

        // Act
        var result = await _controller.Status();

        // Assert
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.Projects[0].DeploymentStatus.ShouldBe(DeploymentStatus.NotDeployed);
    }

    [Fact]
    public async Task Status_WithRunningBuild_ReturnsNotDeployedStatus()
    {
        // Arrange
        var project = CreateProject(1, "user/repo");
        CreateBuild(project, BuildStatus.Running);
        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project> { project });

        // Act
        var result = await _controller.Status();

        // Assert
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.Projects[0].DeploymentStatus.ShouldBe(DeploymentStatus.NotDeployed);
    }

    [Fact]
    public async Task Status_UsesLatestBuildForStatus()
    {
        // Arrange - older build succeeded, newer build failed
        var project = CreateProject(1, "user/repo");
        CreateBuild(project, BuildStatus.Success, DateTime.UtcNow.AddHours(-2));
        CreateBuild(project, BuildStatus.Failed, DateTime.UtcNow.AddHours(-1));
        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project> { project });

        // Act
        var result = await _controller.Status();

        // Assert - should use the latest (failed) build
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.Projects[0].DeploymentStatus.ShouldBe(DeploymentStatus.Failed);
    }

    // -------------------------------------------------------------------------
    // Sorting Tests - Alphabetical
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Status_SortByAlphabetical_Ascending_SortsCorrectly()
    {
        // Arrange
        var projectC = CreateProject(1, "user/charlie");
        var projectA = CreateProject(2, "user/alpha");
        var projectB = CreateProject(3, "user/bravo");
        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project> { projectC, projectA, projectB });

        // Act
        var result = await _controller.Status(StatusSortField.Alphabetical, SortDirection.Ascending);

        // Assert
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.Projects[0].RepoFullName.ShouldBe("user/alpha");
        model.Projects[1].RepoFullName.ShouldBe("user/bravo");
        model.Projects[2].RepoFullName.ShouldBe("user/charlie");
    }

    [Fact]
    public async Task Status_SortByAlphabetical_Descending_SortsCorrectly()
    {
        // Arrange
        var projectA = CreateProject(1, "user/alpha");
        var projectC = CreateProject(2, "user/charlie");
        var projectB = CreateProject(3, "user/bravo");
        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project> { projectA, projectC, projectB });

        // Act
        var result = await _controller.Status(StatusSortField.Alphabetical, SortDirection.Descending);

        // Assert
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.Projects[0].RepoFullName.ShouldBe("user/charlie");
        model.Projects[1].RepoFullName.ShouldBe("user/bravo");
        model.Projects[2].RepoFullName.ShouldBe("user/alpha");
    }

    // -------------------------------------------------------------------------
    // Sorting Tests - Created Date
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Status_SortByCreatedDate_Ascending_SortsCorrectly()
    {
        // Arrange
        var projectNew = CreateProject(1, "user/new", DateTime.UtcNow);
        var projectOld = CreateProject(2, "user/old", DateTime.UtcNow.AddDays(-7));
        var projectMid = CreateProject(3, "user/mid", DateTime.UtcNow.AddDays(-3));
        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project> { projectNew, projectOld, projectMid });

        // Act
        var result = await _controller.Status(StatusSortField.CreatedDate, SortDirection.Ascending);

        // Assert
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.Projects[0].RepoFullName.ShouldBe("user/old");
        model.Projects[1].RepoFullName.ShouldBe("user/mid");
        model.Projects[2].RepoFullName.ShouldBe("user/new");
    }

    [Fact]
    public async Task Status_SortByCreatedDate_Descending_SortsCorrectly()
    {
        // Arrange
        var projectOld = CreateProject(1, "user/old", DateTime.UtcNow.AddDays(-7));
        var projectNew = CreateProject(2, "user/new", DateTime.UtcNow);
        var projectMid = CreateProject(3, "user/mid", DateTime.UtcNow.AddDays(-3));
        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project> { projectOld, projectNew, projectMid });

        // Act
        var result = await _controller.Status(StatusSortField.CreatedDate, SortDirection.Descending);

        // Assert
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.Projects[0].RepoFullName.ShouldBe("user/new");
        model.Projects[1].RepoFullName.ShouldBe("user/mid");
        model.Projects[2].RepoFullName.ShouldBe("user/old");
    }

    // -------------------------------------------------------------------------
    // Sorting Tests - Last Deployment
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Status_SortByLastDeployment_Ascending_SortsCorrectly()
    {
        // Arrange
        var projectRecent = CreateProject(1, "user/recent");
        var projectOld = CreateProject(2, "user/old");
        var projectNever = CreateProject(3, "user/never");

        CreateBuild(projectRecent, BuildStatus.Success, DateTime.UtcNow.AddHours(-1));
        CreateBuild(projectOld, BuildStatus.Success, DateTime.UtcNow.AddDays(-7));
        // projectNever has no builds

        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project> { projectRecent, projectOld, projectNever });

        // Act
        var result = await _controller.Status(StatusSortField.LastDeployment, SortDirection.Ascending);

        // Assert - never deployed should come first (DateTime.MinValue)
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.Projects[0].RepoFullName.ShouldBe("user/never");
        model.Projects[1].RepoFullName.ShouldBe("user/old");
        model.Projects[2].RepoFullName.ShouldBe("user/recent");
    }

    [Fact]
    public async Task Status_SortByLastDeployment_Descending_SortsCorrectly()
    {
        // Arrange
        var projectRecent = CreateProject(1, "user/recent");
        var projectOld = CreateProject(2, "user/old");
        var projectNever = CreateProject(3, "user/never");

        CreateBuild(projectRecent, BuildStatus.Success, DateTime.UtcNow.AddHours(-1));
        CreateBuild(projectOld, BuildStatus.Success, DateTime.UtcNow.AddDays(-7));
        // projectNever has no builds

        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project> { projectRecent, projectOld, projectNever });

        // Act
        var result = await _controller.Status(StatusSortField.LastDeployment, SortDirection.Descending);

        // Assert - most recent should come first
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.Projects[0].RepoFullName.ShouldBe("user/recent");
        model.Projects[1].RepoFullName.ShouldBe("user/old");
        model.Projects[2].RepoFullName.ShouldBe("user/never");
    }

    // -------------------------------------------------------------------------
    // View Model Properties Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Status_SetsCorrectSortFieldInViewModel()
    {
        // Arrange
        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project>());

        // Act
        var result = await _controller.Status(StatusSortField.CreatedDate, SortDirection.Descending);

        // Assert
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.SortField.ShouldBe(StatusSortField.CreatedDate);
        model.SortDirection.ShouldBe(SortDirection.Descending);
    }

    [Fact]
    public async Task Status_DefaultsSortToAlphabeticalAscending()
    {
        // Arrange
        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project>());

        // Act
        var result = await _controller.Status();

        // Assert
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.SortField.ShouldBe(StatusSortField.Alphabetical);
        model.SortDirection.ShouldBe(SortDirection.Ascending);
    }

    [Fact]
    public async Task Status_IncludesTotalBuildCount()
    {
        // Arrange
        var project = CreateProject(1, "user/repo");
        CreateBuild(project, BuildStatus.Success);
        CreateBuild(project, BuildStatus.Failed);
        CreateBuild(project, BuildStatus.Success);
        _projectService.Setup(s => s.GetProjectsForUserAsync(1))
            .ReturnsAsync(new List<Project> { project });

        // Act
        var result = await _controller.Status();

        // Assert
        var viewResult = result.ShouldBeOfType<ViewResult>();
        var model = viewResult.Model.ShouldBeOfType<ProjectStatusViewModel>();
        model.Projects[0].TotalBuilds.ShouldBe(3);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Project CreateProject(int id, string repoFullName, DateTime? createdAt = null)
    {
        var project = new Project
        {
            Id = id,
            OwnerId = _testUser.Id,
            Owner = _testUser,
            GitHubRepoId = 10000 + id,
            RepoFullName = repoFullName,
            RepoUrl = $"https://github.com/{repoFullName}",
            DefaultBranch = "main",
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
        _db.Projects.Add(project);
        _db.SaveChanges();
        return project;
    }

    private Build CreateBuild(Project project, BuildStatus status, DateTime? queuedAt = null)
    {
        // Generate a 40-character commit SHA (GUID is 32 chars, pad with extra chars)
        var sha = Guid.NewGuid().ToString("N") + "00000000";
        var build = new Build
        {
            ProjectId = project.Id,
            Project = project,
            CommitSha = sha[..40],
            Branch = "main",
            Status = status,
            Trigger = BuildTrigger.Push,
            QueuedAt = queuedAt ?? DateTime.UtcNow,
            FinishedAt = status is BuildStatus.Success or BuildStatus.Failed or BuildStatus.TimedOut
                ? (queuedAt ?? DateTime.UtcNow).AddMinutes(5)
                : null
        };
        _db.Builds.Add(build);
        _db.SaveChanges();
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
}
