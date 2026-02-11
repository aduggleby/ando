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
using System.Text.Json;
using Ando.Server.Configuration;
using Ando.Server.Data;
using Ando.Server.GitHub;
using Ando.Server.Models;
using Ando.Server.Services;
using Ando.Server.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    private readonly GitHubSettings _gitHubSettings;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(
        AndoDbContext db,
        IProjectService projectService,
        IBuildService buildService,
        IGitHubService gitHubService,
        IOptions<GitHubSettings> gitHubSettings,
        ILogger<ProjectsController> logger)
    {
        _db = db;
        _projectService = projectService;
        _buildService = buildService;
        _gitHubService = gitHubService;
        _gitHubSettings = gitHubSettings.Value;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // List Projects
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lists all projects for the current user.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(
        StatusSortField sortBy = StatusSortField.LastDeployment,
        SortDirection direction = SortDirection.Descending)
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

            // Load secrets to check configuration status
            var secrets = await _db.ProjectSecrets
                .Where(s => s.ProjectId == project.Id)
                .Select(s => s.Name)
                .ToListAsync();
            var missingSecrets = project.GetMissingSecretsFrom(secrets);

            projectItems.Add(new ProjectListItem
            {
                Id = project.Id,
                RepoFullName = project.RepoFullName,
                RepoUrl = project.RepoUrl,
                CreatedAt = project.CreatedAt,
                LastBuildAt = project.LastBuildAt,
                LastBuildStatus = lastBuild?.Status,
                TotalBuilds = buildCount,
                IsConfigured = missingSecrets.Count == 0,
                MissingSecretsCount = missingSecrets.Count,
                DeploymentStatus = lastBuild?.Status switch
                {
                    BuildStatus.Success => DeploymentStatus.Deployed,
                    BuildStatus.Failed or BuildStatus.TimedOut => DeploymentStatus.Failed,
                    _ => DeploymentStatus.NotDeployed
                },
                LastDeploymentAt = lastBuild?.FinishedAt ?? lastBuild?.QueuedAt
            });
        }

        IEnumerable<ProjectListItem> sorted = sortBy switch
        {
            StatusSortField.Alphabetical => direction == SortDirection.Ascending
                ? projectItems.OrderBy(p => p.RepoFullName)
                : projectItems.OrderByDescending(p => p.RepoFullName),
            StatusSortField.LastDeployment => direction == SortDirection.Ascending
                ? projectItems.OrderBy(p => p.LastDeploymentAt ?? DateTime.MinValue)
                : projectItems.OrderByDescending(p => p.LastDeploymentAt ?? DateTime.MinValue),
            StatusSortField.CreatedDate => direction == SortDirection.Ascending
                ? projectItems.OrderBy(p => p.CreatedAt)
                : projectItems.OrderByDescending(p => p.CreatedAt),
            _ => projectItems.OrderByDescending(p => p.LastDeploymentAt ?? DateTime.MinValue)
        };

        return View(new ProjectListViewModel
        {
            Projects = sorted.ToList(),
            SortField = sortBy,
            SortDirection = direction
        });
    }

    // -------------------------------------------------------------------------
    // Project Status
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows deployment status for all projects as sortable cards.
    /// </summary>
    [HttpGet("status")]
    public IActionResult Status(
        StatusSortField sortBy = StatusSortField.Alphabetical,
        SortDirection direction = SortDirection.Ascending)
    {
        return RedirectToAction(nameof(Index), new { sortBy, direction });
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

        // Get configuration status
        var secretNames = await _projectService.GetSecretNamesAsync(id);
        var missingSecrets = project.GetMissingSecretsFrom(secretNames);

        var viewModel = new ProjectDetailsViewModel
        {
            Id = project.Id,
            RepoFullName = project.RepoFullName,
            RepoUrl = project.RepoUrl,
            DefaultBranch = project.DefaultBranch,
            BranchFilter = project.BranchFilter,
            Profile = project.Profile,
            EnablePrBuilds = project.EnablePrBuilds,
            TimeoutMinutes = project.TimeoutMinutes,
            CreatedAt = project.CreatedAt,
            LastBuildAt = project.LastBuildAt,
            TotalBuilds = totalBuilds,
            IsConfigured = missingSecrets.Count == 0,
            MissingSecrets = missingSecrets,
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
    /// Shows the create project page with a form for manual repository entry.
    /// </summary>
    [HttpGet("create")]
    public IActionResult Create(string? repo = null, string? error = null)
    {
        var viewModel = new CreateProjectViewModel
        {
            RepoFullName = repo,
            ErrorMessage = error
        };
        return View(viewModel);
    }

    /// <summary>
    /// Creates a new project from a GitHub repository URL or name.
    /// Uses the GitHub App installation to access the repository.
    /// </summary>
    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string repoFullName)
    {
        var userId = GetCurrentUserId();

        // Validate and normalize input
        repoFullName = repoFullName?.Trim() ?? "";

        // Parse GitHub URL if provided (e.g., https://github.com/owner/repo)
        if (repoFullName.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(repoFullName);
            repoFullName = uri.AbsolutePath.TrimStart('/').TrimEnd('/');
            // Remove .git suffix if present
            if (repoFullName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                repoFullName = repoFullName[..^4];
            }
        }

        // Validate format (owner/repo)
        var parts = repoFullName.Split('/');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
        {
            return RedirectToAction("Create", new
            {
                repo = repoFullName,
                error = "Invalid repository format. Please enter as 'owner/repo' (e.g., 'octocat/hello-world')."
            });
        }

        // Look up the repository via GitHub App
        var result = await _gitHubService.GetRepositoryInstallationAsync(repoFullName);

        if (result == null)
        {
            // App not installed - redirect to GitHub to install it.
            // Prefer discovering the real app slug via the API rather than relying on config.
            var appSlug = await _gitHubService.GetAppSlugAsync();
            if (string.IsNullOrWhiteSpace(appSlug))
            {
                appSlug = _gitHubSettings.AppName; // fallback
            }

            if (!string.IsNullOrWhiteSpace(appSlug))
            {
                // Store the repo name in session for the callback
                HttpContext.Session.SetString("PendingRepo", repoFullName);

                var installUrl = $"https://github.com/apps/{appSlug}/installations/new";
                return Redirect(installUrl);
            }

            // Fallback if AppName not configured
            return RedirectToAction("Create", new
            {
                repo = repoFullName,
                error = $"Repository '{repoFullName}' not found or the Ando GitHub App is not installed. " +
                        "Please install the app on your repository first."
            });
        }

        var (installationId, repo) = result.Value;

        try
        {
            var project = await _projectService.CreateProjectAsync(
                userId,
                repo.Id,
                repo.FullName,
                repo.HtmlUrl,
                repo.DefaultBranch,
                installationId);

            TempData["Success"] = $"Project {repo.FullName} connected successfully. Pushes to the '{repo.DefaultBranch}' branch will now trigger builds.";
            return RedirectToAction("Details", new { id = project.Id });
        }
        catch (InvalidOperationException ex)
        {
            return RedirectToAction("Create", new
            {
                repo = repoFullName,
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Callback from GitHub after app installation.
    /// GitHub redirects here after the user installs the app on their repository.
    /// </summary>
    [HttpGet("github-callback")]
    public async Task<IActionResult> GitHubCallback(long? installation_id = null)
    {
        // Get the pending repo from session
        var pendingRepo = HttpContext.Session.GetString("PendingRepo");
        HttpContext.Session.Remove("PendingRepo");

        if (string.IsNullOrEmpty(pendingRepo))
        {
            // No pending repo - just redirect to create page
            TempData["Success"] = "GitHub App installed successfully. You can now connect your repositories.";
            return RedirectToAction("Create");
        }

        // Try to connect the repository now that the app is installed
        var result = await _gitHubService.GetRepositoryInstallationAsync(pendingRepo);

        if (result == null)
        {
            // Still can't access - maybe they didn't grant access to this specific repo
            return RedirectToAction("Create", new
            {
                repo = pendingRepo,
                error = $"GitHub App installed, but repository '{pendingRepo}' is not accessible. " +
                        "Please ensure you granted access to this specific repository during installation."
            });
        }

        var userId = GetCurrentUserId();
        var (installationId, repo) = result.Value;

        try
        {
            var project = await _projectService.CreateProjectAsync(
                userId,
                repo.Id,
                repo.FullName,
                repo.HtmlUrl,
                repo.DefaultBranch,
                installationId);

            TempData["Success"] = $"Project {repo.FullName} connected successfully. Pushes to the '{repo.DefaultBranch}' branch will now trigger builds.";
            return RedirectToAction("Details", new { id = project.Id });
        }
        catch (InvalidOperationException ex)
        {
            return RedirectToAction("Create", new
            {
                repo = pendingRepo,
                error = ex.Message
            });
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
        var missingSecrets = project.GetMissingSecretsFrom(secretNames);

        var viewModel = new ProjectSettingsViewModel
        {
            Id = project.Id,
            RepoFullName = project.RepoFullName,
            BranchFilter = project.BranchFilter,
            EnablePrBuilds = project.EnablePrBuilds,
            TimeoutMinutes = project.TimeoutMinutes,
            DockerImage = project.DockerImage,
            Profile = project.Profile,
            ManualProfileOverride = !project.IsProfileValid() && !string.IsNullOrWhiteSpace(project.Profile),
            ManualProfile = project.Profile,
            AvailableProfiles = project.GetAvailableProfileNames(),
            IsProfileValid = project.IsProfileValid(),
            RequiredSecrets = project.RequiredSecrets,
            NotifyOnFailure = project.NotifyOnFailure,
            NotificationEmail = project.NotificationEmail,
            AccountEmail = project.Owner.Email,
            SecretNames = secretNames,
            MissingSecrets = missingSecrets
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

        var effectiveProfile = form.ManualProfileOverride
            ? string.IsNullOrWhiteSpace(form.ManualProfile) ? null : form.ManualProfile.Trim()
            : string.IsNullOrWhiteSpace(form.Profile) ? null : form.Profile.Trim();
        var effectiveNotificationEmail = string.IsNullOrWhiteSpace(form.NotificationEmail)
            ? (form.NotifyOnFailure ? project.Owner.Email : null)
            : form.NotificationEmail.Trim();

        await _projectService.UpdateProjectSettingsAsync(
            id,
            form.BranchFilter,
            form.EnablePrBuilds,
            form.TimeoutMinutes,
            form.DockerImage,
            effectiveProfile,
            form.NotifyOnFailure,
            effectiveNotificationEmail);

        TempData["Success"] = "Settings updated successfully.";
        return RedirectToAction("Settings", new { id });
    }

    /// <summary>
    /// Manually triggers re-detection of required secrets and profiles from build.csando.
    /// </summary>
    [HttpPost("{id:int}/refresh-secrets")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshSecrets(int id)
    {
        var userId = GetCurrentUserId();
        var project = await _projectService.GetProjectForUserAsync(id, userId);

        if (project == null)
        {
            return View("NotFound");
        }

        var detectedSecrets = await _projectService.DetectAndUpdateRequiredSecretsAsync(id);
        var detectedProfiles = await _projectService.DetectAndUpdateProfilesAsync(id);

        var messages = new List<string>();

        if (detectedSecrets.Count > 0)
        {
            messages.Add($"{detectedSecrets.Count} required secret{(detectedSecrets.Count == 1 ? "" : "s")}: {string.Join(", ", detectedSecrets)}");
        }

        if (detectedProfiles.Count > 0)
        {
            messages.Add($"{detectedProfiles.Count} profile{(detectedProfiles.Count == 1 ? "" : "s")}: {string.Join(", ", detectedProfiles)}");
        }

        if (messages.Count > 0)
        {
            TempData["Success"] = $"Detected {string.Join("; ", messages)}";
        }
        else
        {
            TempData["Success"] = "No required secrets or profiles detected in build.csando.";
        }

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

    /// <summary>
    /// Bulk imports secrets from .env file format.
    /// Parses KEY=value lines, ignoring comments (#) and empty lines.
    /// </summary>
    [HttpPost("{id:int}/secrets/bulk")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkImportSecrets(int id, BulkSecretsFormModel form)
    {
        var userId = GetCurrentUserId();
        var project = await _projectService.GetProjectForUserAsync(id, userId);

        if (project == null)
        {
            return View("NotFound");
        }

        if (string.IsNullOrWhiteSpace(form.Content))
        {
            TempData["Error"] = "No content provided.";
            return RedirectToAction("Settings", new { id });
        }

        var lines = form.Content.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var imported = 0;
        var errors = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            // Parse KEY=value format
            var equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex <= 0)
            {
                errors.Add($"Invalid format: {trimmed[..Math.Min(20, trimmed.Length)]}...");
                continue;
            }

            var name = trimmed[..equalsIndex].Trim();
            var value = trimmed[(equalsIndex + 1)..].Trim();

            // Remove surrounding quotes from value if present
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            // Normalize name to uppercase
            name = name.ToUpperInvariant().Replace('-', '_');

            // Validate secret name
            if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Z_][A-Z0-9_]*$"))
            {
                errors.Add($"Invalid name: {name}");
                continue;
            }

            if (string.IsNullOrEmpty(value))
            {
                errors.Add($"Empty value for: {name}");
                continue;
            }

            await _projectService.SetSecretAsync(id, name, value);
            imported++;
        }

        if (errors.Count > 0)
        {
            TempData["Error"] = $"Imported {imported} secrets. Errors: {string.Join(", ", errors.Take(3))}" +
                (errors.Count > 3 ? $" and {errors.Count - 3} more..." : "");
        }
        else if (imported > 0)
        {
            TempData["Success"] = $"Imported {imported} secret{(imported == 1 ? "" : "s")} successfully.";
        }
        else
        {
            TempData["Error"] = "No valid secrets found in the input.";
        }

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

        // Re-detect required secrets from the build script (catches new env vars)
        await _projectService.DetectAndUpdateRequiredSecretsAsync(id);

        // Reload to get updated RequiredSecrets
        await _db.Entry(project).ReloadAsync();

        // Check if project is properly configured
        var secretNames = await _projectService.GetSecretNamesAsync(id);
        var missingSecrets = project.GetMissingSecretsFrom(secretNames);

        if (missingSecrets.Count > 0)
        {
            TempData["Error"] = $"Cannot start build: missing required secrets: {string.Join(", ", missingSecrets)}. Configure them in project settings.";
            return RedirectToAction("Details", new { id });
        }

        var targetBranch = branch ?? project.DefaultBranch;

        // Get the latest commit SHA for the branch from GitHub
        string commitSha = "HEAD";
        string commitMessage = "Manual build trigger";

        if (project.InstallationId.HasValue)
        {
            var sha = await _gitHubService.GetBranchHeadShaAsync(
                project.InstallationId.Value,
                project.RepoFullName,
                targetBranch);

            if (!string.IsNullOrEmpty(sha))
            {
                commitSha = sha;
                commitMessage = $"Manual build of {targetBranch}";
            }
        }

        // Queue a manual build
        var buildId = await _buildService.QueueBuildAsync(
            id,
            commitSha,
            targetBranch,
            BuildTrigger.Manual,
            commitMessage,
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
