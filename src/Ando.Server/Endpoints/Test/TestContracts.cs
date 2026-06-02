// =============================================================================
// TestContracts.cs
//
// Summary: Request/response contracts for test-only FastEndpoints.
//
// These DTOs are consumed by E2E test tooling endpoints under /api/test.
// =============================================================================

using Ando.Server.Models;

namespace Ando.Server.Endpoints.Test;

/// <summary>
/// Request body for creating a test user.
/// </summary>
public record CreateTestUserRequest
{
    /// <summary>
    /// Optional test login name.
    /// </summary>
    public string? Login { get; init; }

    /// <summary>
    /// Optional test email.
    /// </summary>
    public string? Email { get; init; }
}

/// <summary>
/// Response returned when a test user is created.
/// </summary>
public record CreateTestUserResponse
{
    /// <summary>
    /// Created user ID.
    /// </summary>
    public int UserId { get; init; }

    /// <summary>
    /// Placeholder GitHub ID for test users.
    /// </summary>
    public long GitHubId { get; init; }

    /// <summary>
    /// Effective login/display name.
    /// </summary>
    public string Login { get; init; } = "";

    /// <summary>
    /// Created user email.
    /// </summary>
    public string Email { get; init; } = "";

    /// <summary>
    /// Generated synthetic auth token for test tooling.
    /// </summary>
    public string AuthToken { get; init; } = "";
}

/// <summary>
/// Response containing a user's current email verification state and token.
/// </summary>
public record TestEmailVerificationTokenResponse
{
    /// <summary>
    /// Whether the user is already verified.
    /// </summary>
    public bool EmailVerified { get; init; }

    /// <summary>
    /// Current verification token, if present.
    /// </summary>
    public string? Token { get; init; }
}

/// <summary>
/// Response containing a generated password reset token for test automation.
/// </summary>
public record TestPasswordResetTokenResponse
{
    /// <summary>
    /// Generated ASP.NET Core Identity reset token.
    /// </summary>
    public string Token { get; init; } = "";
}

/// <summary>
/// Request to set a user's role in test environments.
/// </summary>
public record SetTestUserRoleRequest
{
    /// <summary>
    /// Target role name.
    /// </summary>
    public string Role { get; init; } = "";
}

/// <summary>
/// Request body for creating a test project.
/// </summary>
public record CreateTestProjectRequest
{
    /// <summary>
    /// Owner user ID.
    /// </summary>
    public int UserId { get; init; }

    /// <summary>
    /// Optional repo name.
    /// </summary>
    public string? RepoName { get; init; }

    /// <summary>
    /// Optional default branch.
    /// </summary>
    public string? DefaultBranch { get; init; }

    /// <summary>
    /// Optional branch filter.
    /// </summary>
    public string? BranchFilter { get; init; }

    /// <summary>
    /// Optional default profile.
    /// </summary>
    public string? Profile { get; init; }

    /// <summary>
    /// Optional available profile set.
    /// </summary>
    public List<string>? AvailableProfiles { get; init; }

    /// <summary>
    /// Enables pull request builds.
    /// </summary>
    public bool EnablePrBuilds { get; init; }

    /// <summary>
    /// Optional timeout override.
    /// </summary>
    public int? TimeoutMinutes { get; init; }

    /// <summary>
    /// Enables failure notifications.
    /// </summary>
    public bool NotifyOnFailure { get; init; } = true;

    /// <summary>
    /// Optional notification email.
    /// </summary>
    public string? NotificationEmail { get; init; }
}

/// <summary>
/// Response returned when a test project is created.
/// </summary>
public record CreateTestProjectResponse
{
    /// <summary>
    /// Created project ID.
    /// </summary>
    public int ProjectId { get; init; }

    /// <summary>
    /// Full repository name.
    /// </summary>
    public string RepoFullName { get; init; } = "";

    /// <summary>
    /// Repository URL.
    /// </summary>
    public string RepoUrl { get; init; } = "";
}

/// <summary>
/// Request body for adding a test secret.
/// </summary>
public record AddTestSecretRequest
{
    /// <summary>
    /// Secret name.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Secret plaintext value.
    /// </summary>
    public string Value { get; init; } = "";
}

/// <summary>
/// Request body for creating a test build.
/// </summary>
public record CreateTestBuildRequest
{
    /// <summary>
    /// Target project ID.
    /// </summary>
    public int ProjectId { get; init; }

    /// <summary>
    /// Optional commit SHA.
    /// </summary>
    public string? CommitSha { get; init; }

    /// <summary>
    /// Optional branch.
    /// </summary>
    public string? Branch { get; init; }

    /// <summary>
    /// Optional commit message.
    /// </summary>
    public string? CommitMessage { get; init; }

    /// <summary>
    /// Optional commit author.
    /// </summary>
    public string? CommitAuthor { get; init; }

    /// <summary>
    /// Optional initial build status.
    /// </summary>
    public BuildStatus? Status { get; init; }

    /// <summary>
    /// Optional build trigger.
    /// </summary>
    public BuildTrigger? Trigger { get; init; }

    /// <summary>
    /// Optional PR number.
    /// </summary>
    public int? PullRequestNumber { get; init; }
}

/// <summary>
/// Response returned when a test build is created.
/// </summary>
public record CreateTestBuildResponse
{
    /// <summary>
    /// Created build ID.
    /// </summary>
    public int BuildId { get; init; }

    /// <summary>
    /// Created build status.
    /// </summary>
    public string Status { get; init; } = "";
}

/// <summary>
/// Request body for updating a test build.
/// </summary>
public record UpdateTestBuildRequest
{
    /// <summary>
    /// Optional status transition.
    /// </summary>
    public BuildStatus? Status { get; init; }

    /// <summary>
    /// Optional error message.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Optional step total.
    /// </summary>
    public int? StepsTotal { get; init; }

    /// <summary>
    /// Optional completed steps count.
    /// </summary>
    public int? StepsCompleted { get; init; }

    /// <summary>
    /// Optional failed steps count.
    /// </summary>
    public int? StepsFailed { get; init; }
}

/// <summary>
/// Request body for appending build log entries.
/// </summary>
public record AddTestLogEntriesRequest
{
    /// <summary>
    /// Log entries to append.
    /// </summary>
    public List<TestLogEntry> Entries { get; init; } = [];
}

/// <summary>
/// Test log entry payload.
/// </summary>
public record TestLogEntry
{
    /// <summary>
    /// Optional log entry type.
    /// </summary>
    public LogEntryType? Type { get; init; }

    /// <summary>
    /// Log message.
    /// </summary>
    public string Message { get; init; } = "";

    /// <summary>
    /// Optional step name.
    /// </summary>
    public string? StepName { get; init; }
}

/// <summary>
/// Request body for adding a test artifact.
/// </summary>
public record AddTestArtifactRequest
{
    /// <summary>
    /// Artifact file name.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Optional storage path.
    /// </summary>
    public string? StoragePath { get; init; }

    /// <summary>
    /// Optional artifact size.
    /// </summary>
    public long? SizeBytes { get; init; }
}

/// <summary>
/// Request body for targeted cleanup.
/// </summary>
public record CleanupRequest
{
    /// <summary>
    /// Optional user ID to delete.
    /// </summary>
    public int? UserId { get; init; }

    /// <summary>
    /// Optional project ID to delete.
    /// </summary>
    public int? ProjectId { get; init; }
}
