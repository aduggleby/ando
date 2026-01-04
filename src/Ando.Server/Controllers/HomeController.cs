// =============================================================================
// HomeController.cs
//
// Summary: Controller for the main dashboard and health endpoints.
//
// Handles the home page (dashboard) and health check endpoint used by
// Docker health checks and load balancers.
//
// Design Decisions:
// - Dashboard requires authentication
// - Health endpoint is public for infrastructure monitoring
// =============================================================================

using System.Security.Claims;
using Ando.Server.Data;
using Ando.Server.Models;
using Ando.Server.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Controllers;

/// <summary>
/// Controller for the main dashboard and system endpoints.
/// </summary>
public class HomeController : Controller
{
    private readonly AndoDbContext _db;

    public HomeController(AndoDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Dashboard page showing recent builds and project overview.
    /// </summary>
    [Authorize]
    public async Task<IActionResult> Index()
    {
        var userId = GetCurrentUserId();
        var today = DateTime.UtcNow.Date;

        // Get user's project IDs
        var projectIds = await _db.Projects
            .Where(p => p.OwnerId == userId)
            .Select(p => p.Id)
            .ToListAsync();

        // Get recent builds across all user's projects
        var recentBuilds = await _db.Builds
            .Include(b => b.Project)
            .Where(b => projectIds.Contains(b.ProjectId))
            .OrderByDescending(b => b.QueuedAt)
            .Take(10)
            .Select(b => new RecentBuildItem(
                b.Id,
                b.Project.RepoFullName,
                b.Branch,
                b.CommitSha.Length >= 8 ? b.CommitSha.Substring(0, 8) : b.CommitSha,
                b.Status.ToString(),
                b.StartedAt,
                b.Duration))
            .ToListAsync();

        // Get statistics
        var totalProjects = projectIds.Count;

        var buildsToday = await _db.Builds
            .Where(b => projectIds.Contains(b.ProjectId))
            .Where(b => b.QueuedAt >= today)
            .CountAsync();

        var failedToday = await _db.Builds
            .Where(b => projectIds.Contains(b.ProjectId))
            .Where(b => b.QueuedAt >= today)
            .Where(b => b.Status == BuildStatus.Failed || b.Status == BuildStatus.TimedOut)
            .CountAsync();

        var viewModel = new DashboardViewModel
        {
            RecentBuilds = recentBuilds,
            TotalProjects = totalProjects,
            BuildsToday = buildsToday,
            FailedToday = failedToday
        };

        return View(viewModel);
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return int.Parse(claim?.Value ?? "0");
    }

    /// <summary>
    /// Health check endpoint for Docker/Kubernetes probes.
    /// </summary>
    [AllowAnonymous]
    [Route("health")]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    /// <summary>
    /// Error page for unhandled exceptions.
    /// </summary>
    [AllowAnonymous]
    [Route("error")]
    public IActionResult Error()
    {
        return View();
    }
}
