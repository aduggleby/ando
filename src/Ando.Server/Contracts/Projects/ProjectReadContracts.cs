// =============================================================================
// ProjectReadContracts.cs
//
// Summary: Query/read response contracts for project endpoints.
// =============================================================================

namespace Ando.Server.Contracts.Projects;

/// <summary>
/// Response containing list of projects.
/// </summary>
/// <param name="Projects">List of projects owned by the user.</param>
public record GetProjectsResponse(
    IReadOnlyList<ProjectListItemDto> Projects
);

/// <summary>
/// Response containing project status list with sorting info.
/// </summary>
/// <param name="Projects">List of project statuses.</param>
/// <param name="SortField">Field used for sorting.</param>
/// <param name="SortDirection">Sort direction (Ascending/Descending).</param>
public record GetProjectsStatusResponse(
    IReadOnlyList<ProjectStatusDto> Projects,
    string SortField,
    string SortDirection
);

/// <summary>
/// Response containing project details.
/// </summary>
/// <param name="Project">Full project details.</param>
public record GetProjectResponse(
    ProjectDetailsDto Project
);

/// <summary>
/// Response containing project settings.
/// </summary>
/// <param name="Settings">Project settings.</param>
public record GetProjectSettingsResponse(
    ProjectSettingsDto Settings
);
