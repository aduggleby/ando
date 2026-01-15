// =============================================================================
// ProjectsController.cs
//
// Summary: Controller for project management operations.
//
// Handles listing projects, viewing project details, creating new projects,
// and managing project settings including secrets.
//
// Design Decisions:
// - All actions require authentication
// - Ownership is verified for all project-specific actions
// - Secrets can only be set/deleted, never viewed
// =============================================================================

using System.Security.Claims;
using Ando.Server.Data;
using Ando.Server.GitHub;
using Ando.Server.Models;
using Ando.Server.Services;
using Ando.Server.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Controllers;

/// <summary>
/// Controller for project management.
/// </summary>
[Authorize]
[Route("projects")]
public class ProjectsController : Controller
{
    private readonly AndoDbContext _db;
    private readonly IProjectService _projectService;
    private readonly IBuildService _buildService;
    private readonly IGitHubService _gitHubService;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        AndoDbContext db,
        IProjectService projectService,
        IBuildService buildService,
        IGitHubService gitHubService,
        ILogger<ProjectsController> logger)
    {
        _db = db;
        _projectService = projectService;
        _buildService = buildService;
        _gitHubService = gitHubService;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // List Projects
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lists all projects for the current user.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var userId = GetCurrentUserId();
        var projects = await _projectService.GetProjectsForUserAsync(userId);

        // Get last build status and count for each project
        var projectItems = new List<ProjectListItem>();
        foreach (var project in projects)
        {
            var lastBuild = await _db.Builds
                .Where(b => b.ProjectId == project.Id)
                .OrderByDescending(b => b.QueuedAt)
                .FirstOrDefaultAsync();

            var buildCount = await _db.Builds
                .Where(b => b.ProjectId == project.Id)
                .CountAsync();

            projectItems.Add(new ProjectListItem
            {
                Id = project.Id,
                RepoFullName = project.RepoFullName,
                RepoUrl = project.RepoUrl,
                CreatedAt = project.CreatedAt,
                LastBuildAt = project.LastBuildAt,
                LastBuildStatus = lastBuild?.Status,
                TotalBuilds = buildCount
            });
        }

        return View(new ProjectListViewModel { Projects = projectItems });
    }

    // -------------------------------------------------------------------------
    // Project Status
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows deployment status for all projects as sortable cards.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(
        StatusSortField sortBy = StatusSortField.Alphabetical,
        SortDirection direction = SortDirection.Ascending)
    {
        var userId = GetCurrentUserId();
        var projects = await _projectService.GetProjectsForUserAsync(userId);

        // Build status items with deployment status
        var statusItems = new List<ProjectStatusItem>();
        foreach (var project in projects)
        {
            var lastBuild = await _db.Builds
                .Where(b => b.ProjectId == project.Id)
                .OrderByDescending(b => b.QueuedAt)
                .FirstOrDefaultAsync();

            var buildCount = await _db.Builds
                .Where(b => b.ProjectId == project.Id)
                .CountAsync();

            // Determine deployment status from last build
            var deploymentStatus = DeploymentStatus.NotDeployed;
            if (lastBuild != null)
            {
                deploymentStatus = lastBuild.Status switch
                {
                    BuildStatus.Success => DeploymentStatus.Deployed,
                    BuildStatus.Failed or BuildStatus.TimedOut => DeploymentStatus.Failed,
                    _ => DeploymentStatus.NotDeployed
                };
            }

            statusItems.Add(new ProjectStatusItem
            {
                Id = project.Id,
                RepoFullName = project.RepoFullName,
                RepoUrl = project.RepoUrl,
                CreatedAt = project.CreatedAt,
                LastDeploymentAt = lastBuild?.FinishedAt ?? lastBuild?.QueuedAt,
                DeploymentStatus = deploymentStatus,
                TotalBuilds = buildCount
            });
        }

        // Apply sorting
        IEnumerable<ProjectStatusItem> sorted = sortBy switch
        {
            StatusSortField.Alphabetical => direction == SortDirection.Ascending
                ? statusItems.OrderBy(p => p.RepoFullName)
                : statusItems.OrderByDescending(p => p.RepoFullName),
            StatusSortField.LastDeployment => direction == SortDirection.Ascending
                ? statusItems.OrderBy(p => p.LastDeploymentAt ?? DateTime.MinValue)
                : statusItems.OrderByDescending(p => p.LastDeploymentAt ?? DateTime.MinValue),
            StatusSortField.CreatedDate => direction == SortDirection.Ascending
                ? statusItems.OrderBy(p => p.CreatedAt)
                : statusItems.OrderByDescending(p => p.CreatedAt),
            _ => statusItems.OrderBy(p => p.RepoFullName)
        };

        var viewModel = new ProjectStatusViewModel
        {
            Projects = sorted.ToList(),
            SortField = sortBy,
            SortDirection = direction
        };

        return View(viewModel);
    }

    // -------------------------------------------------------------------------
    // Project Details
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows project details and recent builds.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var userId = GetCurrentUserId();
        var project = await _projectService.GetProjectForUserAsync(id, userId);

        if (project == null)
        {
            return View("NotFound");
        }

        var recentBuilds = await _buildService.GetBuildsForProjectAsync(id, 0, 20);
        var totalBuilds = await _db.Builds.CountAsync(b => b.ProjectId == id);

        var viewModel = new ProjectDetailsViewModel
        {
            Id = project.Id,
            RepoFullName = project.RepoFullName,
            RepoUrl = project.RepoUrl,
            DefaultBranch = project.DefaultBranch,
            BranchFilter = project.BranchFilter,
            EnablePrBuilds = project.EnablePrBuilds,
            TimeoutMinutes = project.TimeoutMinutes,
            CreatedAt = project.CreatedAt,
            LastBuildAt = project.LastBuildAt,
            TotalBuilds = totalBuilds,
            RecentBuilds = recentBuilds.Select(b => new BuildListItem
            {
                Id = b.Id,
                CommitSha = b.CommitSha,
                Branch = b.Branch,
                CommitMessage = b.CommitMessage,
                CommitAuthor = b.CommitAuthor,
                Status = b.Status,
                Trigger = b.Trigger,
                QueuedAt = b.QueuedAt,
                StartedAt = b.StartedAt,
                FinishedAt = b.FinishedAt,
                Duration = b.Duration,
                PullRequestNumber = b.PullRequestNumber
            }).ToList()
        };

        return View(viewModel);
    }

    // -------------------------------------------------------------------------
    // Create Project
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows the create project page with available repositories.
    /// </summary>
    [HttpGet("create")]
    public async Task<IActionResult> Create()
    {
        var userId = GetCurrentUserId();
        var user = await _db.Users.FindAsync(userId);

        if (user?.GitHubAccessToken == null)
        {
            return RedirectToAction("Login", "Auth");
        }

        // Get user's repositories from GitHub
        var repos = await _gitHubService.GetUserRepositoriesAsync(user.GitHubAccessToken);

        // Get already connected repo IDs
        var connectedRepoIds = await _db.Projects
            .Where(p => p.OwnerId == userId)
            .Select(p => p.GitHubRepoId)
            .ToListAsync();

        var viewModel = new CreateProjectViewModel
        {
            Repositories = repos
                .OrderBy(r => r.FullName)
                .Select(r => new AvailableRepository
                {
                    GitHubRepoId = r.Id,
                    FullName = r.FullName,
                    HtmlUrl = r.HtmlUrl,
                    DefaultBranch = r.DefaultBranch,
                    IsPrivate = r.IsPrivate,
                    AlreadyConnected = connectedRepoIds.Contains(r.Id)
                }).ToList()
        };

        return View(viewModel);
    }

    /// <summary>
    /// Creates a new project from a GitHub repository.
    /// </summary>
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(long gitHubRepoId)
    {
        var userId = GetCurrentUserId();
        var user = await _db.Users.FindAsync(userId);

        if (user?.GitHubAccessToken == null)
        {
            return RedirectToAction("Login", "Auth");
        }

        // Get repository info from GitHub
        var repos = await _gitHubService.GetUserRepositoriesAsync(user.GitHubAccessToken);
        var repo = repos.FirstOrDefault(r => r.Id == gitHubRepoId);

        if (repo == null)
        {
            TempData["Error"] = "Repository not found or you don't have access to it.";
            return RedirectToAction("Create");
        }

        try
        {
            var project = await _projectService.CreateProjectAsync(
                userId,
                repo.Id,
                repo.FullName,
                repo.HtmlUrl,
                repo.DefaultBranch);

            TempData["Success"] = $"Project {repo.FullName} created successfully.";
            return RedirectToAction("Details", new { id = project.Id });
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction("Create");
        }
    }

    // -------------------------------------------------------------------------
    // Project Settings
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows the project settings page.
    /// </summary>
    [HttpGet("{id:int}/settings")]
    public async Task<IActionResult> Settings(int id)
    {
        var userId = GetCurrentUserId();
        var project = await _projectService.GetProjectForUserAsync(id, userId);

        if (project == null)
        {
            return View("NotFound");
        }

        var secretNames = await _projectService.GetSecretNamesAsync(id);

        var viewModel = new ProjectSettingsViewModel
        {
            Id = project.Id,
            RepoFullName = project.RepoFullName,
            BranchFilter = project.BranchFilter,
            EnablePrBuilds = project.EnablePrBuilds,
            TimeoutMinutes = project.TimeoutMinutes,
            DockerImage = project.DockerImage,
            NotifyOnFailure = project.NotifyOnFailure,
            NotificationEmail = project.NotificationEmail,
            SecretNames = secretNames
        };

        return View(viewModel);
    }

    /// <summary>
    /// Updates project settings.
    /// </summary>
    [HttpPost("{id:int}/settings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(int id, ProjectSettingsFormModel form)
    {
        var userId = GetCurrentUserId();
        var project = await _projectService.GetProjectForUserAsync(id, userId);

        if (project == null)
        {
            return View("NotFound");
        }

        await _projectService.UpdateProjectSettingsAsync(
            id,
            form.BranchFilter,
            form.EnablePrBuilds,
            form.TimeoutMinutes,
            form.DockerImage,
            form.NotifyOnFailure,
            form.NotificationEmail);

        TempData["Success"] = "Settings updated successfully.";
        return RedirectToAction("Settings", new { id });
    }

    /// <summary>
    /// Adds or updates a secret.
    /// </summary>
    [HttpPost("{id:int}/secrets")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddSecret(int id, AddSecretFormModel form)
    {
        var userId = GetCurrentUserId();
        var project = await _projectService.GetProjectForUserAsync(id, userId);

        if (project == null)
        {
            return View("NotFound");
        }

        if (string.IsNullOrWhiteSpace(form.Name) || string.IsNullOrWhiteSpace(form.Value))
        {
            TempData["Error"] = "Secret name and value are required.";
            return RedirectToAction("Settings", new { id });
        }

        // Validate secret name (env var format)
        if (!System.Text.RegularExpressions.Regex.IsMatch(form.Name, @"^[A-Z_][A-Z0-9_]*$"))
        {
            TempData["Error"] = "Secret name must be uppercase with underscores only (e.g., MY_SECRET).";
            return RedirectToAction("Settings", new { id });
        }

        await _projectService.SetSecretAsync(id, form.Name.Trim(), form.Value);

        TempData["Success"] = $"Secret {form.Name} saved.";
        return RedirectToAction("Settings", new { id });
    }

    /// <summary>
    /// Deletes a secret.
    /// </summary>
    [HttpPost("{id:int}/secrets/{name}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSecret(int id, string name)
    {
        var userId = GetCurrentUserId();
        var project = await _projectService.GetProjectForUserAsync(id, userId);

        if (project == null)
        {
            return View("NotFound");
        }

        await _projectService.DeleteSecretAsync(id, name);

        TempData["Success"] = $"Secret {name} deleted.";
        return RedirectToAction("Settings", new { id });
    }

    // -------------------------------------------------------------------------
    // Delete Project
    // -------------------------------------------------------------------------

    /// <summary>
    /// Deletes a project.
    /// </summary>
    [HttpPost("{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = GetCurrentUserId();
        var project = await _projectService.GetProjectForUserAsync(id, userId);

        if (project == null)
        {
            return View("NotFound");
        }

        await _projectService.DeleteProjectAsync(id);

        TempData["Success"] = $"Project {project.RepoFullName} deleted.";
        return RedirectToAction("Index");
    }

    // -------------------------------------------------------------------------
    // Trigger Build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Manually triggers a build for the default branch.
    /// </summary>
    [HttpPost("{id:int}/build")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TriggerBuild(int id, string? branch = null)
    {
        var userId = GetCurrentUserId();
        var project = await _projectService.GetProjectForUserAsync(id, userId);

        if (project == null)
        {
            return View("NotFound");
        }

        // Get the latest commit SHA for the branch
        // For now, we'll use a placeholder - in production this would call GitHub API
        var targetBranch = branch ?? project.DefaultBranch;

        // Queue a manual build
        var buildId = await _buildService.QueueBuildAsync(
            id,
            "HEAD", // Placeholder - would be actual commit SHA
            targetBranch,
            BuildTrigger.Manual,
            "Manual build trigger",
            User.Identity?.Name);

        return RedirectToAction("Details", "Builds", new { id = buildId });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return int.Parse(claim?.Value ?? "0");
    }
}
