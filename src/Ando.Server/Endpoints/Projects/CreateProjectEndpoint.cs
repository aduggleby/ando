// =============================================================================
// CreateProjectEndpoint.cs
//
// Summary: FastEndpoint for creating a new project.
//
// Creates a new project from a GitHub repository. If the GitHub App is not
// installed on the repository, returns a redirect URL for installation.
//
// Design Decisions:
// - Requires authentication
// - Validates repository format (owner/repo)
// - Handles GitHub App installation flow
// - Supports both URL and short form input
// =============================================================================

using System.Security.Claims;
using Ando.Server.Configuration;
using Ando.Server.Contracts.Projects;
using Ando.Server.GitHub;
using Ando.Server.Services;
using FastEndpoints;
using Microsoft.Extensions.Options;

namespace Ando.Server.Endpoints.Projects;

/// <summary>
/// POST /api/projects - Create a new project.
/// </summary>
public class CreateProjectEndpoint : Endpoint<CreateProjectRequest, CreateProjectResponse>
{
    private readonly IProjectService _projectService;
    private readonly IGitHubService _gitHubService;
    private readonly GitHubSettings _gitHubSettings;

    public CreateProjectEndpoint(
        IProjectService projectService,
        IGitHubService gitHubService,
        IOptions<GitHubSettings> gitHubSettings)
    {
        _projectService = projectService;
        _gitHubService = gitHubService;
        _gitHubSettings = gitHubSettings.Value;
    }

    public override void Configure()
    {
        Post("/projects");
    }

    public override async Task HandleAsync(CreateProjectRequest req, CancellationToken ct)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        // Validate and normalize input
        // Accept both RepoFullName (preferred) and RepoUrl (legacy/alternate client field).
        var repoFullName = (req.RepoFullName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(repoFullName))
        {
            repoFullName = (req.RepoUrl ?? "").Trim();
        }

        if (string.IsNullOrWhiteSpace(repoFullName))
        {
            await SendAsync(new CreateProjectResponse(
                false,
                Error: "Repository is required. Provide 'owner/repo' or a GitHub URL."
            ), cancellation: ct);
            return;
        }

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
            await SendAsync(new CreateProjectResponse(
                false,
                Error: "Invalid repository format. Please enter as 'owner/repo' (e.g., 'octocat/hello-world')."
            ), cancellation: ct);
            return;
        }

        // Look up the repository via GitHub App
        var result = await _gitHubService.GetRepositoryInstallationAsync(repoFullName);

        if (result == null)
        {
            // App not installed - return redirect URL for GitHub App installation
            if (!string.IsNullOrEmpty(_gitHubSettings.AppName))
            {
                var installUrl = $"https://github.com/apps/{_gitHubSettings.AppName}/installations/new";
                await SendAsync(new CreateProjectResponse(
                    false,
                    RedirectUrl: installUrl,
                    Error: $"Repository '{repoFullName}' not found or the Ando GitHub App is not installed."
                ), cancellation: ct);
                return;
            }

            await SendAsync(new CreateProjectResponse(
                false,
                Error: $"Repository '{repoFullName}' not found or the Ando GitHub App is not installed."
            ), cancellation: ct);
            return;
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

            await SendAsync(new CreateProjectResponse(
                true,
                ProjectId: project.Id
            ), cancellation: ct);
        }
        catch (InvalidOperationException ex)
        {
            await SendAsync(new CreateProjectResponse(
                false,
                Error: ex.Message
            ), cancellation: ct);
        }
    }
}
