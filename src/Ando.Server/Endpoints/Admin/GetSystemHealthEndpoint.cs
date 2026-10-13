// =============================================================================
// GetSystemHealthEndpoint.cs
//
// Summary: Admin endpoint for live subsystem health probes.
//
// Probes database connectivity, Hangfire background processing, and GitHub App
// authentication readiness. Used by the admin dashboard system health card.
//
// Design Decisions:
// - Database connectivity and job storage are separate checks (keys
//   `database` and `jobStorage`) with their own status: with PostgreSQL or
//   SQL Server both live in the same database server, but a reachable
//   database does not prove Hangfire's schema and workers are working
// - The database check names the configured provider (SqlServer/PostgreSql)
// - Display names stay unchanged because the admin UI keys rows on them
// =============================================================================

using Ando.Server.Configuration;
using Ando.Server.Contracts.Admin;
using Ando.Server.Data;
using Ando.Server.GitHub;
using Ando.Server.Models;
using FastEndpoints;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ando.Server.Endpoints.Admin;

/// <summary>
/// GET /api/admin/system-health - Run live system health probes.
/// </summary>
public class GetSystemHealthEndpoint : EndpointWithoutRequest<SystemHealthResponse>
{
    private readonly AndoDbContext _db;
    private readonly IGitHubService _gitHubService;
    private readonly string _databaseProvider;

    /// <summary>
    /// Stable key of the database connectivity check.
    /// </summary>
    public const string DatabaseKey = "database";

    /// <summary>
    /// Stable key of the Hangfire job storage check.
    /// </summary>
    public const string JobStorageKey = "jobStorage";

    /// <summary>
    /// Stable key of the GitHub App check.
    /// </summary>
    public const string GitHubKey = "github";

    /// <summary>
    /// Initializes endpoint dependencies.
    /// </summary>
    /// <param name="db">Application database context.</param>
    /// <param name="gitHubService">GitHub App service.</param>
    /// <param name="databaseSettings">Database settings naming the configured provider.</param>
    public GetSystemHealthEndpoint(
        AndoDbContext db,
        IGitHubService gitHubService,
        IOptions<DatabaseSettings> databaseSettings)
    {
        _db = db;
        _gitHubService = gitHubService;
        _databaseProvider = databaseSettings.Value.Provider.ToString();
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
            await ProbeJobStorageAsync(ct),
            await ProbeGitHubAsync(ct)
        };

        await Send.OkAsync(new SystemHealthResponse(checks, DateTime.UtcNow), cancellation: ct);
    }

    private SystemHealthCheckDto DatabaseCheck(string status, string message) =>
        new("Database", status, $"{_databaseProvider}: {message}", DatabaseKey, _databaseProvider);

    private static SystemHealthCheckDto JobStorageCheck(string status, string message) =>
        new("Background Jobs", status, message, JobStorageKey);

    private static SystemHealthCheckDto GitHubCheck(string status, string message) =>
        new("GitHub Integration", status, message, GitHubKey);

    private async Task<SystemHealthCheckDto> ProbeDatabaseAsync(CancellationToken ct)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct);
            if (!canConnect)
            {
                return DatabaseCheck("error", "cannot connect.");
            }

            // Run a tiny query to validate actual command execution path.
            await _db.Projects.AsNoTracking().Select(p => p.Id).FirstOrDefaultAsync(ct);
            return DatabaseCheck("healthy", "connected.");
        }
        catch (Exception ex)
        {
            return DatabaseCheck("error", ex.Message);
        }
    }

    // Probes Hangfire's own storage (the monitoring API queries its tables), not
    // the EF connection, so a missing or broken Hangfire schema shows up here.
    private async Task<SystemHealthCheckDto> ProbeJobStorageAsync(CancellationToken ct)
    {
        await Task.Yield();

        try
        {
            if (JobStorage.Current == null)
            {
                return JobStorageCheck("warning", "Job storage not configured.");
            }

            var api = JobStorage.Current.GetMonitoringApi();
            var servers = api.Servers();
            if (servers.Count == 0)
            {
                return JobStorageCheck("warning", "No active workers.");
            }

            return JobStorageCheck("healthy", $"{servers.Count} worker(s) active.");
        }
        catch (Exception ex)
        {
            return JobStorageCheck("error", ex.Message);
        }
    }

    private async Task<SystemHealthCheckDto> ProbeGitHubAsync(CancellationToken ct)
    {
        try
        {
            var slug = await _gitHubService.GetAppSlugAsync();
            if (string.IsNullOrWhiteSpace(slug))
            {
                return GitHubCheck("error", "GitHub App authentication failed.");
            }

            return GitHubCheck("healthy", $"GitHub App: {slug}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return GitHubCheck("error", ex.Message);
        }
    }
}

