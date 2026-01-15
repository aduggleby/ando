// =============================================================================
// BuildsController.cs
//
// Summary: Controller for build operations.
//
// Handles viewing build details, cancelling builds, retrying builds,
// and downloading artifacts. Provides real-time build status via SignalR.
//
// Design Decisions:
// - All actions require authentication
// - Ownership is verified via project ownership
// - Cancellation only allowed for queued/running builds
// - Retry creates a new build with same parameters
// =============================================================================

using System.Security.Claims;
using Ando.Server.Data;
using Ando.Server.Models;
using Ando.Server.Services;
using Ando.Server.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Controllers;

/// <summary>
/// Controller for build operations.
/// </summary>
[Authorize]
[Route("builds")]
public class BuildsController : Controller
{
    private readonly AndoDbContext _db;
    private readonly IBuildService _buildService;
    private readonly IProjectService _projectService;
    private readonly ILogger<BuildsController> _logger;

    public BuildsController(
        AndoDbContext db,
        IBuildService buildService,
        IProjectService projectService,
        ILogger<BuildsController> logger)
    {
        _db = db;
        _buildService = buildService;
        _projectService = projectService;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Build Details
    // -------------------------------------------------------------------------

    /// <summary>
    /// Shows build details with logs and artifacts.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var userId = GetCurrentUserId();

        var build = await _db.Builds
            .Include(b => b.Project)
            .Include(b => b.LogEntries.OrderBy(l => l.Sequence))
            .Include(b => b.Artifacts)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (build == null)
        {
            return View("NotFound");
        }

        // Verify ownership
        if (build.Project.OwnerId != userId)
        {
            return View("NotFound");
        }

        var viewModel = new BuildDetailsViewModel
        {
            Id = build.Id,
            ProjectId = build.ProjectId,
            ProjectName = build.Project.RepoFullName,
            ProjectUrl = build.Project.RepoUrl,
            CommitSha = build.CommitSha,
            Branch = build.Branch,
            CommitMessage = build.CommitMessage,
            CommitAuthor = build.CommitAuthor,
            PullRequestNumber = build.PullRequestNumber,
            Status = build.Status,
            Trigger = build.Trigger,
            QueuedAt = build.QueuedAt,
            StartedAt = build.StartedAt,
            FinishedAt = build.FinishedAt,
            Duration = build.Duration,
            StepsTotal = build.StepsTotal,
            StepsCompleted = build.StepsCompleted,
            StepsFailed = build.StepsFailed,
            ErrorMessage = build.ErrorMessage,
            CanCancel = build.Status == BuildStatus.Queued || build.Status == BuildStatus.Running,
            CanRetry = build.Status == BuildStatus.Failed ||
                       build.Status == BuildStatus.Cancelled ||
                       build.Status == BuildStatus.TimedOut,
            IsLive = build.Status == BuildStatus.Queued || build.Status == BuildStatus.Running,
            LogEntries = build.LogEntries.Select(l => new LogEntryViewModel
            {
                Id = l.Id,
                Sequence = l.Sequence,
                Type = l.Type.ToString(),
                Message = l.Message,
                StepName = l.StepName,
                Timestamp = l.Timestamp
            }).ToList(),
            Artifacts = build.Artifacts.Select(a => new ArtifactViewModel
            {
                Id = a.Id,
                Name = a.Name,
                FormattedSize = FormatFileSize(a.SizeBytes),
                CreatedAt = a.CreatedAt
            }).ToList()
        };

        return View(viewModel);
    }

    // -------------------------------------------------------------------------
    // Cancel Build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Cancels a running or queued build.
    /// </summary>
    [HttpPost("{id:int}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = GetCurrentUserId();

        var build = await _db.Builds
            .Include(b => b.Project)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (build == null)
        {
            return View("NotFound");
        }

        // Verify ownership
        if (build.Project.OwnerId != userId)
        {
            return View("NotFound");
        }

        if (build.Status != BuildStatus.Queued && build.Status != BuildStatus.Running)
        {
            TempData["Error"] = "Build cannot be cancelled in its current state.";
            return RedirectToAction("Details", new { id });
        }

        var success = await _buildService.CancelBuildAsync(id);

        if (success)
        {
            TempData["Success"] = "Build cancelled.";
        }
        else
        {
            TempData["Error"] = "Failed to cancel build.";
        }

        return RedirectToAction("Details", new { id });
    }

    // -------------------------------------------------------------------------
    // Retry Build
    // -------------------------------------------------------------------------

    /// <summary>
    /// Retries a failed, cancelled, or timed out build.
    /// </summary>
    [HttpPost("{id:int}/retry")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(int id)
    {
        var userId = GetCurrentUserId();

        var build = await _db.Builds
            .Include(b => b.Project)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (build == null)
        {
            return View("NotFound");
        }

        // Verify ownership
        if (build.Project.OwnerId != userId)
        {
            return View("NotFound");
        }

        if (build.Status != BuildStatus.Failed &&
            build.Status != BuildStatus.Cancelled &&
            build.Status != BuildStatus.TimedOut)
        {
            TempData["Error"] = "Build cannot be retried in its current state.";
            return RedirectToAction("Details", new { id });
        }

        // Create a new build with same parameters
        var newBuildId = await _buildService.QueueBuildAsync(
            build.ProjectId,
            build.CommitSha,
            build.Branch,
            BuildTrigger.Manual,
            build.CommitMessage,
            build.CommitAuthor,
            build.PullRequestNumber);

        TempData["Success"] = "Build queued for retry.";
        return RedirectToAction("Details", new { id = newBuildId });
    }

    // -------------------------------------------------------------------------
    // Download Artifact
    // -------------------------------------------------------------------------

    /// <summary>
    /// Downloads a build artifact.
    /// </summary>
    [HttpGet("{buildId:int}/artifacts/{artifactId:int}")]
    public async Task<IActionResult> DownloadArtifact(int buildId, int artifactId)
    {
        var userId = GetCurrentUserId();

        var artifact = await _db.BuildArtifacts
            .Include(a => a.Build)
            .ThenInclude(b => b.Project)
            .FirstOrDefaultAsync(a => a.Id == artifactId && a.BuildId == buildId);

        if (artifact == null)
        {
            return View("NotFound");
        }

        // Verify ownership
        if (artifact.Build.Project.OwnerId != userId)
        {
            return View("NotFound");
        }

        if (!System.IO.File.Exists(artifact.StoragePath))
        {
            _logger.LogWarning("Artifact file not found: {Path}", artifact.StoragePath);
            TempData["Error"] = "Artifact file not found.";
            return RedirectToAction("Details", new { id = buildId });
        }

        var contentType = "application/octet-stream";
        return PhysicalFile(artifact.StoragePath, contentType, artifact.Name);
    }

    // -------------------------------------------------------------------------
    // API: Get Log Entries (for SignalR catch-up)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Gets log entries after a specific sequence number (for SignalR catch-up).
    /// </summary>
    [HttpGet("{id:int}/logs")]
    public async Task<IActionResult> GetLogs(int id, [FromQuery] int afterSequence = 0)
    {
        var userId = GetCurrentUserId();

        var build = await _db.Builds
            .Include(b => b.Project)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (build == null)
        {
            return View("NotFound");
        }

        // Verify ownership
        if (build.Project.OwnerId != userId)
        {
            return View("NotFound");
        }

        var logs = await _db.BuildLogEntries
            .Where(l => l.BuildId == id && l.Sequence > afterSequence)
            .OrderBy(l => l.Sequence)
            .Select(l => new LogEntryViewModel
            {
                Id = l.Id,
                Sequence = l.Sequence,
                Type = l.Type.ToString(),
                Message = l.Message,
                StepName = l.StepName,
                Timestamp = l.Timestamp
            })
            .ToListAsync();

        return Json(new
        {
            logs,
            status = build.Status.ToString(),
            isComplete = build.Status != BuildStatus.Queued && build.Status != BuildStatus.Running
        });
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return int.Parse(claim?.Value ?? "0");
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}
