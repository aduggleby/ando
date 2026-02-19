// =============================================================================
// HomeController.cs
//
// Summary: Infrastructure endpoints for health checks and diagnostics.
//
// Provides unauthenticated health checks used by Docker and operations.
//
// Design Decisions:
// - Keep endpoint paths stable for existing Docker/test checks
// - Keep diagnostics available without the legacy Razor UI stack
// =============================================================================

using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ando.Server.GitHub;

namespace Ando.Server.Controllers;

/// <summary>
/// Infrastructure endpoints for health checks and diagnostics.
/// </summary>
public class HomeController : Controller
{
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
    /// Docker health check endpoint that verifies Docker-in-Docker functionality.
    /// Runs a simple hello-world container to test that the Docker socket is accessible
    /// and containers can be spawned from within this server container.
    /// </summary>
    [AllowAnonymous]
    [Route("health/docker")]
    public async Task<IActionResult> DockerHealth()
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
                return StatusCode(503, new
                {
                    status = "unhealthy",
                    error = "Failed to start docker process",
                    timestamp = DateTime.UtcNow
                });
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                return StatusCode(503, new
                {
                    status = "unhealthy",
                    error = $"Docker command failed with exit code {process.ExitCode}",
                    details = error,
                    timestamp = DateTime.UtcNow
                });
            }

            // Check if we got the expected output from hello-world
            var success = output.Contains("Hello from Docker!");

            if (success)
            {
                return Ok(new
                {
                    status = "healthy",
                    message = "Docker-in-Docker is working correctly",
                    timestamp = DateTime.UtcNow
                });
            }
            else
            {
                return StatusCode(503, new
                {
                    status = "unhealthy",
                    error = "Unexpected output from hello-world container",
                    output = output.Length > 500 ? output.Substring(0, 500) : output,
                    timestamp = DateTime.UtcNow
                });
            }
        }
        catch (Exception ex)
        {
            return StatusCode(503, new
            {
                status = "unhealthy",
                error = ex.Message,
                timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Generic error endpoint for exception handler middleware.
    /// </summary>
    [AllowAnonymous]
    [Route("error")]
    public IActionResult Error()
    {
        return Problem(
            title: "An unexpected error occurred.",
            statusCode: StatusCodes.Status500InternalServerError);
    }

    /// <summary>
    /// Diagnostic endpoint to test GitHub App connectivity.
    /// Tests JWT generation and optionally checks a specific repository.
    /// </summary>
    [AllowAnonymous]
    [Route("health/github")]
    public async Task<IActionResult> GitHubHealth([FromQuery] string? repo = null)
    {
        var gitHubService = HttpContext.RequestServices.GetRequiredService<IGitHubService>();

        try
        {
            if (string.IsNullOrEmpty(repo))
            {
                // Just test that we can generate tokens (basic connectivity)
                return Ok(new
                {
                    status = "healthy",
                    message = "GitHub App configuration is valid. Provide ?repo=owner/repo to test specific repository access.",
                    timestamp = DateTime.UtcNow
                });
            }

            // Test repository access
            var result = await gitHubService.GetRepositoryInstallationAsync(repo);

            if (result == null)
            {
                return StatusCode(404, new
                {
                    status = "not_found",
                    error = $"Repository '{repo}' not found or GitHub App is not installed on it",
                    timestamp = DateTime.UtcNow
                });
            }

            var (installationId, repository) = result.Value;

            return Ok(new
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
            });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new
            {
                status = "unhealthy",
                error = ex.Message,
                timestamp = DateTime.UtcNow
            });
        }
    }
}
