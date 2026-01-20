// =============================================================================
// Project.cs
//
// Summary: Represents a GitHub repository configured for CI builds.
//
// A project links a GitHub repository to the build system. It stores repository
// metadata, build configuration (branch filters, timeouts), and notification
// settings. Each project is owned by a single user.
//
// Design Decisions:
// - Single owner per project (no team/org support in v1)
// - BranchFilter is comma-separated for simplicity
// - InstallationId links to the GitHub App installation for API access
// =============================================================================

namespace Ando.Server.Models;

/// <summary>
/// A GitHub repository configured for CI builds.
/// </summary>
public class Project
{
    public int Id { get; set; }

    public int OwnerId { get; set; }

    /// <summary>
    /// The user who owns this project.
    /// </summary>
    public ApplicationUser Owner { get; set; } = null!;

    // -------------------------------------------------------------------------
    // GitHub Repository Info
    // -------------------------------------------------------------------------

    /// <summary>
    /// GitHub's unique repository ID.
    /// </summary>
    public long GitHubRepoId { get; set; }

    /// <summary>
    /// Full repository name in "owner/repo" format.
    /// </summary>
    public string RepoFullName { get; set; } = "";

    /// <summary>
    /// HTTPS URL to the repository.
    /// </summary>
    public string RepoUrl { get; set; } = "";

    /// <summary>
    /// Default branch name (e.g., "main" or "master").
    /// </summary>
    public string DefaultBranch { get; set; } = "main";

    /// <summary>
    /// GitHub App installation ID for this repository.
    /// Required for API access and webhook verification.
    /// </summary>
    public long? InstallationId { get; set; }

    // -------------------------------------------------------------------------
    // Build Settings
    // -------------------------------------------------------------------------

    /// <summary>
    /// Comma-separated list of branches that trigger builds.
    /// Default is "main,master" to catch both common defaults.
    /// </summary>
    public string BranchFilter { get; set; } = "main,master";

    /// <summary>
    /// Whether pull requests trigger builds.
    /// Default is false to avoid unexpected build usage.
    /// </summary>
    public bool EnablePrBuilds { get; set; } = false;

    /// <summary>
    /// Maximum build duration in minutes before timeout.
    /// Default is 15 minutes.
    /// </summary>
    public int TimeoutMinutes { get; set; } = 15;

    /// <summary>
    /// Custom Docker image for builds. Null uses the system default.
    /// </summary>
    public string? DockerImage { get; set; }

    /// <summary>
    /// Comma-separated list of required environment variable names.
    /// Builds are blocked until all required secrets are configured.
    /// </summary>
    public string? RequiredSecrets { get; set; }

    /// <summary>
    /// The selected build profile to use. Null means no profile (default build).
    /// Must match one of the profiles in AvailableProfiles.
    /// </summary>
    public string? Profile { get; set; }

    /// <summary>
    /// Comma-separated list of available profile names detected from build.csando.
    /// Updated when the repo is connected and on each build.
    /// </summary>
    public string? AvailableProfiles { get; set; }

    // -------------------------------------------------------------------------
    // Notification Settings
    // -------------------------------------------------------------------------

    /// <summary>
    /// Whether to send email notifications on build failure.
    /// </summary>
    public bool NotifyOnFailure { get; set; } = true;

    /// <summary>
    /// Override email address for notifications. Null uses owner's email.
    /// </summary>
    public string? NotificationEmail { get; set; }

    // -------------------------------------------------------------------------
    // Timestamps
    // -------------------------------------------------------------------------

    public DateTime CreatedAt { get; set; }

    public DateTime? LastBuildAt { get; set; }

    // -------------------------------------------------------------------------
    // Navigation Properties
    // -------------------------------------------------------------------------

    /// <summary>
    /// All builds for this project.
    /// </summary>
    public ICollection<Build> Builds { get; set; } = [];

    /// <summary>
    /// Environment variable secrets for this project.
    /// </summary>
    public ICollection<ProjectSecret> Secrets { get; set; } = [];

    // -------------------------------------------------------------------------
    // Helper Methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Checks if a branch matches the configured filter.
    /// </summary>
    public bool MatchesBranchFilter(string branch)
    {
        var filters = BranchFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return filters.Any(f => f.Equals(branch, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the email address to use for notifications.
    /// </summary>
    public string? GetNotificationEmail()
    {
        return NotificationEmail ?? Owner.Email;
    }

    /// <summary>
    /// Gets the list of required secret names.
    /// </summary>
    public IReadOnlyList<string> GetRequiredSecretNames()
    {
        if (string.IsNullOrWhiteSpace(RequiredSecrets))
            return [];

        return RequiredSecrets
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Gets the list of required secrets that are not yet configured.
    /// Uses the Secrets navigation property (must be loaded).
    /// </summary>
    public IReadOnlyList<string> GetMissingSecrets()
    {
        var required = GetRequiredSecretNames();
        if (required.Count == 0)
            return [];

        var configured = Secrets.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return required.Where(r => !configured.Contains(r)).ToList();
    }

    /// <summary>
    /// Gets the list of required secrets that are not in the provided list.
    /// Use when Secrets navigation property is not loaded.
    /// </summary>
    public IReadOnlyList<string> GetMissingSecretsFrom(IEnumerable<string> configuredSecretNames)
    {
        var required = GetRequiredSecretNames();
        if (required.Count == 0)
            return [];

        var configured = configuredSecretNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return required.Where(r => !configured.Contains(r)).ToList();
    }

    /// <summary>
    /// Checks if all required secrets are configured.
    /// </summary>
    public bool IsConfigured => GetMissingSecrets().Count == 0;

    /// <summary>
    /// Gets the list of available profile names.
    /// </summary>
    public IReadOnlyList<string> GetAvailableProfileNames()
    {
        if (string.IsNullOrWhiteSpace(AvailableProfiles))
            return [];

        return AvailableProfiles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order()
            .ToList();
    }

    /// <summary>
    /// Checks if the selected profile is valid.
    /// A null profile is always valid (uses default build).
    /// A non-null profile must exist in AvailableProfiles.
    /// </summary>
    public bool IsProfileValid()
    {
        if (string.IsNullOrWhiteSpace(Profile))
            return true;

        var available = GetAvailableProfileNames();
        return available.Contains(Profile, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Updates the available profiles from a detected list.
    /// </summary>
    public void SetAvailableProfiles(IEnumerable<string> profiles)
    {
        var sorted = profiles
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order();

        AvailableProfiles = sorted.Any() ? string.Join(",", sorted) : null;
    }
}
