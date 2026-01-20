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
    private readonly IRequiredSecretsDetector _secretsDetector;
    private readonly IProfileDetector _profileDetector;
    private readonly ILogger<ProjectService> _logger;

    public ProjectService(
        AndoDbContext db,
        IEncryptionService encryption,
        IRequiredSecretsDetector secretsDetector,
        IProfileDetector profileDetector,
        ILogger<ProjectService> logger)
    {
        _db = db;
        _encryption = encryption;
        _secretsDetector = secretsDetector;
        _profileDetector = profileDetector;
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
        // Check if project already exists for this repo AND owner.
        // Different users can add the same repo as separate projects.
        var existing = await _db.Projects
            .FirstOrDefaultAsync(p => p.GitHubRepoId == gitHubRepoId && p.OwnerId == ownerId);

        if (existing != null)
        {
            throw new InvalidOperationException($"You have already added repository {repoFullName}");
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

        // Auto-detect required secrets and profiles from build.csando
        if (installationId.HasValue)
        {
            await DetectAndUpdateRequiredSecretsAsync(project.Id);
            await DetectAndUpdateProfilesAsync(project.Id);
        }

        return project;
    }

    /// <inheritdoc />
    public async Task<bool> UpdateProjectSettingsAsync(
        int projectId,
        string branchFilter,
        bool enablePrBuilds,
        int timeoutMinutes,
        string? dockerImage,
        string? profile,
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
        project.Profile = string.IsNullOrWhiteSpace(profile) ? null : profile.Trim();
        // Note: RequiredSecrets and AvailableProfiles are auto-detected from build.csando, not set manually
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> DetectAndUpdateRequiredSecretsAsync(int projectId)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null || !project.InstallationId.HasValue)
        {
            _logger.LogWarning("Cannot detect secrets: project {ProjectId} not found or no installation", projectId);
            return [];
        }

        try
        {
            var detectedSecrets = await _secretsDetector.DetectRequiredSecretsAsync(
                project.InstallationId.Value,
                project.RepoFullName,
                project.DefaultBranch);

            // Update the project's required secrets
            var newRequiredSecrets = detectedSecrets.Count > 0
                ? string.Join(", ", detectedSecrets)
                : null;

            if (project.RequiredSecrets != newRequiredSecrets)
            {
                project.RequiredSecrets = newRequiredSecrets;
                await _db.SaveChangesAsync();

                _logger.LogInformation(
                    "Updated required secrets for project {ProjectId}: {Secrets}",
                    projectId,
                    newRequiredSecrets ?? "(none)");
            }

            return detectedSecrets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting secrets for project {ProjectId}", projectId);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> DetectAndUpdateProfilesAsync(int projectId)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null || !project.InstallationId.HasValue)
        {
            _logger.LogWarning("Cannot detect profiles: project {ProjectId} not found or no installation", projectId);
            return [];
        }

        try
        {
            var detectedProfiles = await _profileDetector.DetectProfilesAsync(
                project.InstallationId.Value,
                project.RepoFullName,
                project.DefaultBranch);

            // Update the project's available profiles
            project.SetAvailableProfiles(detectedProfiles);
            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Updated available profiles for project {ProjectId}: {Profiles}",
                projectId,
                detectedProfiles.Count > 0 ? string.Join(", ", detectedProfiles) : "(none)");

            return detectedProfiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting profiles for project {ProjectId}", projectId);
            return [];
        }
    }
}
