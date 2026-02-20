// =============================================================================
// CompatibilityEndpoints.cs
//
// Summary: FastEndpoints replacements for legacy non-API compatibility routes.
//
// These endpoints preserve existing non-API route behavior used by probes,
// external integrations, and redirects while the application runs in SPA mode.
// =============================================================================

using System.Diagnostics;
using Ando.Server.GitHub;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Ando.Server.Endpoints.Infrastructure;

/// <summary>
/// GET /health - Health check endpoint for container/platform probes.
/// </summary>
public class RootHealthEndpoint : EndpointWithoutRequest<object>
{
    public override void Configure()
    {
        Get("/health");
        AllowAnonymous();
        RoutePrefixOverride(string.Empty);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await SendAsync(new { status = "healthy", timestamp = DateTime.UtcNow }, cancellation: ct);
    }
}

/// <summary>
/// GET /health/docker - Verifies Docker-in-Docker execution capability.
/// </summary>
public class DockerHealthEndpoint : EndpointWithoutRequest<object>
{
    public override void Configure()
    {
        Get("/health/docker");
        AllowAnonymous();
        RoutePrefixOverride(string.Empty);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--rm");
        startInfo.ArgumentList.Add("hello-world");

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                await SendAsync(new
                {
                    status = "unhealthy",
                    error = "Failed to start docker process",
                    timestamp = DateTime.UtcNow
                }, 503, ct);
                return;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                await SendAsync(new
                {
                    status = "unhealthy",
                    error = $"Docker command failed with exit code {process.ExitCode}",
                    details = error,
                    timestamp = DateTime.UtcNow
                }, 503, ct);
                return;
            }

            var success = output.Contains("Hello from Docker!", StringComparison.Ordinal);

            if (success)
            {
                await SendAsync(new
                {
                    status = "healthy",
                    message = "Docker-in-Docker is working correctly",
                    timestamp = DateTime.UtcNow
                }, cancellation: ct);
                return;
            }

            await SendAsync(new
            {
                status = "unhealthy",
                error = "Unexpected output from hello-world container",
                output = output.Length > 500 ? output[..500] : output,
                timestamp = DateTime.UtcNow
            }, 503, ct);
        }
        catch (Exception ex)
        {
            await SendAsync(new
            {
                status = "unhealthy",
                error = ex.Message,
                timestamp = DateTime.UtcNow
            }, 503, ct);
        }
    }
}

/// <summary>
/// GET /health/github - Basic GitHub app connectivity diagnostics.
/// </summary>
public class GitHubHealthEndpoint : EndpointWithoutRequest<object>
{
    private readonly IGitHubService _gitHubService;

    public GitHubHealthEndpoint(IGitHubService gitHubService)
    {
        _gitHubService = gitHubService;
    }

    public override void Configure()
    {
        Get("/health/github");
        AllowAnonymous();
        RoutePrefixOverride(string.Empty);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var repo = HttpContext.Request.Query["repo"].ToString();

        try
        {
            if (string.IsNullOrWhiteSpace(repo))
            {
                await SendAsync(new
                {
                    status = "healthy",
                    message = "GitHub App configuration is valid. Provide ?repo=owner/repo to test specific repository access.",
                    timestamp = DateTime.UtcNow
                }, cancellation: ct);
                return;
            }

            var result = await _gitHubService.GetRepositoryInstallationAsync(repo);

            if (result == null)
            {
                await SendAsync(new
                {
                    status = "not_found",
                    error = $"Repository '{repo}' not found or GitHub App is not installed on it",
                    timestamp = DateTime.UtcNow
                }, 404, ct);
                return;
            }

            var (installationId, repository) = result.Value;

            await SendAsync(new
            {
                status = "healthy",
                message = "GitHub App can access the repository",
                repository = new
                {
                    id = repository.Id,
                    fullName = repository.FullName,
                    defaultBranch = repository.DefaultBranch,
                    isPrivate = repository.IsPrivate
                },
                installationId,
                timestamp = DateTime.UtcNow
            }, cancellation: ct);
        }
        catch (Exception ex)
        {
            await SendAsync(new
            {
                status = "unhealthy",
                error = ex.Message,
                timestamp = DateTime.UtcNow
            }, 503, ct);
        }
    }
}

/// <summary>
/// GET /error - Generic exception handler endpoint.
/// </summary>
public class ErrorEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/error");
        AllowAnonymous();
        RoutePrefixOverride(string.Empty);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await SendResultAsync(TypedResults.Problem(
            title: "An unexpected error occurred.",
            statusCode: StatusCodes.Status500InternalServerError));
    }
}

/// <summary>
/// GET /session - Compatibility redirect for GitHub app setup URL.
/// </summary>
public class SessionGetEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/session");
        AllowAnonymous();
        RoutePrefixOverride(string.Empty);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var qs = HttpContext.Request.QueryString.Value ?? string.Empty;
        await SendRedirectAsync("/projects/create" + qs, false, false);
    }
}

/// <summary>
/// POST /session - Compatibility redirect for GitHub app setup URL.
/// </summary>
public class SessionPostEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/session");
        AllowAnonymous();
        RoutePrefixOverride(string.Empty);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var qs = HttpContext.Request.QueryString.Value ?? string.Empty;
        await SendRedirectAsync("/projects/create" + qs, false, false);
    }
}
