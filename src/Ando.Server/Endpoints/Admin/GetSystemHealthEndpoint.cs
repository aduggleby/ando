// =============================================================================
// GetSystemHealthEndpoint.cs
//
// Summary: Admin endpoint for live subsystem health probes.
//
// Probes database connectivity, Hangfire background processing, and GitHub App
// authentication readiness. Used by the admin dashboard system health card.
// =============================================================================

using Ando.Server.Contracts.Admin;
using Ando.Server.Data;
using Ando.Server.GitHub;
using Ando.Server.Models;
using FastEndpoints;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Endpoints.Admin;

/// <summary>
/// GET /api/admin/system-health - Run live system health probes.
/// </summary>
public class GetSystemHealthEndpoint : EndpointWithoutRequest<SystemHealthResponse>
{
    private readonly AndoDbContext _db;
    private readonly IGitHubService _gitHubService;

    /// <summary>
    /// Initializes endpoint dependencies.
    /// </summary>
    public GetSystemHealthEndpoint(
        AndoDbContext db,
        IGitHubService gitHubService)
    {
        _db = db;
        _gitHubService = gitHubService;
    }

    /// <inheritdoc />
    public override void Configure()
    {
        Get("/admin/system-health");
        Roles(UserRoles.Admin);
    }

    /// <inheritdoc />
    public override async Task HandleAsync(CancellationToken ct)
    {
        var checks = new List<SystemHealthCheckDto>
        {
            await ProbeDatabaseAsync(ct),
            await ProbeBackgroundJobsAsync(ct),
            await ProbeGitHubAsync(ct)
        };

        await SendAsync(new SystemHealthResponse(checks, DateTime.UtcNow), cancellation: ct);
    }

    private async Task<SystemHealthCheckDto> ProbeDatabaseAsync(CancellationToken ct)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct);
            if (!canConnect)
            {
                return new SystemHealthCheckDto("Database", "error", "Cannot connect.");
            }

            // Run a tiny query to validate actual command execution path.
            await _db.Projects.AsNoTracking().Select(p => p.Id).FirstOrDefaultAsync(ct);
            return new SystemHealthCheckDto("Database", "healthy", "Connected.");
        }
        catch (Exception ex)
        {
            return new SystemHealthCheckDto("Database", "error", ex.Message);
        }
    }

    private async Task<SystemHealthCheckDto> ProbeBackgroundJobsAsync(CancellationToken ct)
    {
        await Task.Yield();

        try
        {
            if (JobStorage.Current == null)
            {
                return new SystemHealthCheckDto("Background Jobs", "warning", "Job storage not configured.");
            }

            var api = JobStorage.Current.GetMonitoringApi();
            var servers = api.Servers();
            if (servers.Count == 0)
            {
                return new SystemHealthCheckDto("Background Jobs", "warning", "No active workers.");
            }

            return new SystemHealthCheckDto("Background Jobs", "healthy", $"{servers.Count} worker(s) active.");
        }
        catch (Exception ex)
        {
            return new SystemHealthCheckDto("Background Jobs", "error", ex.Message);
        }
    }

    private async Task<SystemHealthCheckDto> ProbeGitHubAsync(CancellationToken ct)
    {
        try
        {
            var slug = await _gitHubService.GetAppSlugAsync();
            if (string.IsNullOrWhiteSpace(slug))
            {
                return new SystemHealthCheckDto("GitHub Integration", "error", "GitHub App authentication failed.");
            }

            return new SystemHealthCheckDto("GitHub Integration", "healthy", $"GitHub App: {slug}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SystemHealthCheckDto("GitHub Integration", "error", ex.Message);
        }
    }
}

