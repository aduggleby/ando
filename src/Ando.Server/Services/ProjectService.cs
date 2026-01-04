// =============================================================================
// ProjectService.cs
//
// Summary: Implementation of project management operations.
//
// Handles all project CRUD operations including settings management and
// secret storage. Secrets are encrypted before storage.
//
// Design Decisions:
// - Secrets are encrypted using IEncryptionService
// - Deletion cascades to builds and secrets via EF configuration
// - Branch filter stored as comma-separated string
// =============================================================================

using Ando.Server.Data;
using Ando.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Services;

/// <summary>
/// Implementation of project management operations.
/// </summary>
public class ProjectService : IProjectService
{
    private readonly AndoDbContext _db;
    private readonly IEncryptionService _encryption;
    private readonly ILogger<ProjectService> _logger;

    public ProjectService(
        AndoDbContext db,
        IEncryptionService encryption,
        ILogger<ProjectService> logger)
    {
        _db = db;
        _encryption = encryption;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Project>> GetProjectsForUserAsync(int userId)
    {
        return await _db.Projects
            .Where(p => p.OwnerId == userId)
            .OrderByDescending(p => p.LastBuildAt ?? p.CreatedAt)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<Project?> GetProjectAsync(int projectId)
    {
        return await _db.Projects
            .Include(p => p.Owner)
            .FirstOrDefaultAsync(p => p.Id == projectId);
    }

    /// <inheritdoc />
    public async Task<Project?> GetProjectForUserAsync(int projectId, int userId)
    {
        return await _db.Projects
            .Include(p => p.Owner)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerId == userId);
    }

    /// <inheritdoc />
    public async Task<Project> CreateProjectAsync(
        int ownerId,
        long gitHubRepoId,
        string repoFullName,
        string repoUrl,
        string defaultBranch,
        long? installationId = null)
    {
        // Check if project already exists for this repo
        var existing = await _db.Projects
            .FirstOrDefaultAsync(p => p.GitHubRepoId == gitHubRepoId);

        if (existing != null)
        {
            throw new InvalidOperationException($"Project already exists for repository {repoFullName}");
        }

        var project = new Project
        {
            OwnerId = ownerId,
            GitHubRepoId = gitHubRepoId,
            RepoFullName = repoFullName,
            RepoUrl = repoUrl,
            DefaultBranch = defaultBranch,
            BranchFilter = defaultBranch, // Default to only build the default branch
            InstallationId = installationId,
            CreatedAt = DateTime.UtcNow
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Created project {ProjectId} for {Repo} (owner: {OwnerId})",
            project.Id, repoFullName, ownerId);

        return project;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateProjectSettingsAsync(
        int projectId,
        string branchFilter,
        bool enablePrBuilds,
        int timeoutMinutes,
        string? dockerImage,
        bool notifyOnFailure,
        string? notificationEmail)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null)
        {
            return false;
        }

        project.BranchFilter = branchFilter;
        project.EnablePrBuilds = enablePrBuilds;
        project.TimeoutMinutes = Math.Max(1, Math.Min(60, timeoutMinutes));
        project.DockerImage = string.IsNullOrWhiteSpace(dockerImage) ? null : dockerImage.Trim();
        project.NotifyOnFailure = notifyOnFailure;
        project.NotificationEmail = string.IsNullOrWhiteSpace(notificationEmail) ? null : notificationEmail.Trim();

        await _db.SaveChangesAsync();

        _logger.LogInformation("Updated settings for project {ProjectId}", projectId);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteProjectAsync(int projectId)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null)
        {
            return false;
        }

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted project {ProjectId} ({Repo})", projectId, project.RepoFullName);

        return true;
    }

    /// <inheritdoc />
    public async Task SetSecretAsync(int projectId, string name, string value)
    {
        var secret = await _db.ProjectSecrets
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Name == name);

        var encryptedValue = _encryption.Encrypt(value);

        if (secret == null)
        {
            secret = new ProjectSecret
            {
                ProjectId = projectId,
                Name = name,
                EncryptedValue = encryptedValue,
                CreatedAt = DateTime.UtcNow
            };
            _db.ProjectSecrets.Add(secret);
        }
        else
        {
            secret.EncryptedValue = encryptedValue;
            secret.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation("Set secret {Name} for project {ProjectId}", name, projectId);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteSecretAsync(int projectId, string name)
    {
        var secret = await _db.ProjectSecrets
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Name == name);

        if (secret == null)
        {
            return false;
        }

        _db.ProjectSecrets.Remove(secret);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Deleted secret {Name} from project {ProjectId}", name, projectId);

        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetSecretNamesAsync(int projectId)
    {
        return await _db.ProjectSecrets
            .Where(s => s.ProjectId == projectId)
            .Select(s => s.Name)
            .OrderBy(n => n)
            .ToListAsync();
    }
}
