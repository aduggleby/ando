// =============================================================================
// ProjectWriteContracts.cs
//
// Summary: Command/write request and response contracts for project endpoints.
// =============================================================================

namespace Ando.Server.Contracts.Projects;

/// <summary>
/// Request to create a new project from a GitHub repository.
/// </summary>
public class CreateProjectRequest
{
    /// <summary>
    /// Full repository name in owner/repo format.
    /// </summary>
    public string RepoFullName { get; set; } = "";

    /// <summary>
    /// Back-compat with older/alternate clients that send a URL field.
    /// The server normalizes URLs into owner/repo format.
    /// </summary>
    public string? RepoUrl { get; set; }
}

/// <summary>
/// Response from project creation.
/// </summary>
/// <param name="Success">Whether creation succeeded.</param>
/// <param name="ProjectId">ID of the created project.</param>
/// <param name="Error">Error message if creation failed.</param>
/// <param name="RedirectUrl">URL for GitHub App installation if needed.</param>
public record CreateProjectResponse(
    bool Success,
    int? ProjectId = null,
    string? Error = null,
    string? RedirectUrl = null
);

/// <summary>
/// Request to update project settings.
/// </summary>
public class UpdateProjectSettingsRequest
{
    /// <summary>
    /// Branch filter pattern for triggering builds.
    /// </summary>
    public string BranchFilter { get; set; } = "";

    /// <summary>
    /// Whether to enable pull request builds.
    /// </summary>
    public bool EnablePrBuilds { get; set; }

    /// <summary>
    /// Build timeout in minutes.
    /// </summary>
    public int TimeoutMinutes { get; set; } = 15;

    /// <summary>
    /// Custom Docker image for builds.
    /// </summary>
    public string? DockerImage { get; set; }

    /// <summary>
    /// Build profile name to use.
    /// </summary>
    public string? Profile { get; set; }

    /// <summary>
    /// Whether to send failure notifications.
    /// </summary>
    public bool NotifyOnFailure { get; set; } = true;

    /// <summary>
    /// Email address for failure notifications.
    /// </summary>
    public string? NotificationEmail { get; set; }
}

/// <summary>
/// Response from settings update.
/// </summary>
/// <param name="Success">Whether update succeeded.</param>
/// <param name="Error">Error message if update failed.</param>
public record UpdateProjectSettingsResponse(
    bool Success,
    string? Error = null
);

/// <summary>
/// Response from project deletion.
/// </summary>
/// <param name="Success">Whether deletion succeeded.</param>
/// <param name="Error">Error message if deletion failed.</param>
public record DeleteProjectResponse(
    bool Success,
    string? Error = null
);

/// <summary>
/// Request to manually trigger a build.
/// </summary>
public class TriggerBuildRequest
{
    /// <summary>
    /// Branch to build (defaults to repository's default branch).
    /// </summary>
    public string? Branch { get; set; }
}

/// <summary>
/// Response from build trigger.
/// </summary>
/// <param name="Success">Whether the build was triggered.</param>
/// <param name="BuildId">ID of the created build.</param>
/// <param name="Error">Error message if trigger failed.</param>
public record TriggerBuildResponse(
    bool Success,
    int? BuildId = null,
    string? Error = null
);
