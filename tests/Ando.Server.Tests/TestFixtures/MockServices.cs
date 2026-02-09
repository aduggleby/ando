// =============================================================================
// MockServices.cs
//
// Summary: Mock implementations of services for unit testing.
//
// Provides testable implementations of external services like GitHub and email.
// Captures calls for verification in tests.
// =============================================================================

using Ando.Server.GitHub;
using Ando.Server.Models;
using Ando.Server.Services;

namespace Ando.Server.Tests.TestFixtures;

/// <summary>
/// Mock implementation of IGitHubService for testing.
/// </summary>
public class MockGitHubService : IGitHubService
{
    public List<CommitStatusCall> CommitStatusCalls { get; } = [];
    public List<CloneRepoCall> CloneRepoCalls { get; } = [];
    public List<FetchCheckoutCall> FetchCheckoutCalls { get; } = [];
    public List<GitHubRepository> MockRepositories { get; set; } = [];
    public string? MockInstallationToken { get; set; } = "test-installation-token";
    public Exception? ThrowOnCommitStatus { get; set; }
    public Exception? ThrowOnCloneRepo { get; set; }
    public Exception? ThrowOnFetchCheckout { get; set; }

    public Task<bool> SetCommitStatusAsync(
        long installationId,
        string repoFullName,
        string commitSha,
        CommitStatusState state,
        string? description = null,
        string? targetUrl = null,
        string context = "ci/ando")
    {
        if (ThrowOnCommitStatus != null)
        {
            throw ThrowOnCommitStatus;
        }

        CommitStatusCalls.Add(new CommitStatusCall(
            installationId,
            repoFullName,
            commitSha,
            state,
            description,
            targetUrl,
            context));

        return Task.FromResult(true);
    }

    public Task<bool> CloneRepositoryAsync(
        long installationId,
        string repoFullName,
        string branch,
        string commitSha,
        string targetDirectory,
        string? gitTokenOverride = null)
    {
        if (ThrowOnCloneRepo != null)
        {
            throw ThrowOnCloneRepo;
        }

        CloneRepoCalls.Add(new CloneRepoCall(
            installationId,
            repoFullName,
            branch,
            commitSha,
            targetDirectory));

        return Task.FromResult(true);
    }

    public Task<bool> FetchAndCheckoutAsync(
        long installationId,
        string repoFullName,
        string branch,
        string commitSha,
        string repoDirectory,
        string? gitTokenOverride = null)
    {
        if (ThrowOnFetchCheckout != null)
        {
            throw ThrowOnFetchCheckout;
        }

        FetchCheckoutCalls.Add(new FetchCheckoutCall(
            installationId,
            repoFullName,
            branch,
            commitSha,
            repoDirectory));

        return Task.FromResult(true);
    }

    public Task<string?> GetInstallationTokenAsync(long installationId)
    {
        return Task.FromResult(MockInstallationToken);
    }

    public Task<IReadOnlyList<GitHubRepository>> GetInstallationRepositoriesAsync(long installationId)
    {
        return Task.FromResult<IReadOnlyList<GitHubRepository>>(MockRepositories);
    }

    public Task<IReadOnlyList<GitHubRepository>> GetUserRepositoriesAsync(string accessToken)
    {
        return Task.FromResult<IReadOnlyList<GitHubRepository>>(MockRepositories);
    }

    public Task<(long InstallationId, GitHubRepository Repository)?> GetRepositoryInstallationAsync(string repoFullName)
    {
        var repo = MockRepositories.FirstOrDefault(r => r.FullName == repoFullName);
        if (repo != null)
        {
            return Task.FromResult<(long InstallationId, GitHubRepository Repository)?>((MockInstallationId, repo));
        }
        return Task.FromResult<(long InstallationId, GitHubRepository Repository)?>(null);
    }

    public long MockInstallationId { get; set; } = 12345;
    public string? MockBranchHeadSha { get; set; } = "abc123";
    public Dictionary<string, string> MockFileContents { get; set; } = [];

    public Task<string?> GetBranchHeadShaAsync(long installationId, string repoFullName, string branch)
    {
        return Task.FromResult(MockBranchHeadSha);
    }

    public Task<string?> GetFileContentAsync(long installationId, string repoFullName, string filePath, string? branch = null)
    {
        if (MockFileContents.TryGetValue(filePath, out var content))
        {
            return Task.FromResult<string?>(content);
        }
        return Task.FromResult<string?>(null);
    }
}

public record CommitStatusCall(
    long InstallationId,
    string RepoFullName,
    string CommitSha,
    CommitStatusState State,
    string? Description,
    string? TargetUrl,
    string Context);

public record CloneRepoCall(
    long InstallationId,
    string RepoFullName,
    string Branch,
    string CommitSha,
    string TargetDirectory);

public record FetchCheckoutCall(
    long InstallationId,
    string RepoFullName,
    string Branch,
    string CommitSha,
    string RepoDirectory);

/// <summary>
/// Mock implementation of IBuildService for testing.
/// </summary>
public class MockBuildService : IBuildService
{
    public List<QueueBuildCall> QueueBuildCalls { get; } = [];
    public List<int> CancelBuildCalls { get; } = [];
    public List<int> RetryBuildCalls { get; } = [];
    public int NextBuildId { get; set; } = 1;
    public Build? MockBuild { get; set; }
    public List<Build> MockBuilds { get; set; } = [];

    /// <summary>
    /// When set, QueueBuildAsync will throw this exception.
    /// </summary>
    public Exception? ThrowOnQueueBuild { get; set; }

    public Task<int> QueueBuildAsync(
        int projectId,
        string commitSha,
        string branch,
        BuildTrigger trigger,
        string? commitMessage = null,
        string? commitAuthor = null,
        int? pullRequestNumber = null,
        string? profile = null)
    {
        if (ThrowOnQueueBuild != null)
        {
            throw ThrowOnQueueBuild;
        }

        var buildId = NextBuildId++;
        QueueBuildCalls.Add(new QueueBuildCall(
            projectId,
            commitSha,
            branch,
            trigger,
            commitMessage,
            commitAuthor,
            pullRequestNumber,
            profile,
            buildId));

        return Task.FromResult(buildId);
    }

    public Task<Build?> GetBuildAsync(int buildId)
    {
        return Task.FromResult(MockBuild);
    }

    public Task<IReadOnlyList<Build>> GetBuildsForProjectAsync(int projectId, int skip = 0, int take = 20)
    {
        return Task.FromResult<IReadOnlyList<Build>>(MockBuilds);
    }

    public Task<IReadOnlyList<Build>> GetRecentBuildsForUserAsync(int userId, int take = 10)
    {
        return Task.FromResult<IReadOnlyList<Build>>(MockBuilds);
    }

    public Task<bool> CancelBuildAsync(int buildId)
    {
        CancelBuildCalls.Add(buildId);
        return Task.FromResult(true);
    }

    public Task<int> RetryBuildAsync(int buildId)
    {
        RetryBuildCalls.Add(buildId);
        var newBuildId = NextBuildId++;
        return Task.FromResult(newBuildId);
    }

    public Task UpdateBuildStatusAsync(
        int buildId,
        BuildStatus status,
        string? errorMessage = null,
        int? stepsTotal = null,
        int? stepsCompleted = null,
        int? stepsFailed = null)
    {
        return Task.CompletedTask;
    }
}

public record QueueBuildCall(
    int ProjectId,
    string CommitSha,
    string Branch,
    BuildTrigger Trigger,
    string? CommitMessage,
    string? CommitAuthor,
    int? PullRequestNumber,
    string? Profile,
    int ResultBuildId);

/// <summary>
/// Mock implementation of IEmailService for testing.
/// </summary>
public class MockEmailService : IEmailService
{
    public List<EmailCall> SentEmails { get; } = [];

    public Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        SentEmails.Add(new EmailCall("Custom", to, subject));
        return Task.CompletedTask;
    }

    public Task SendBuildFailedEmailAsync(Build build, string recipientEmail)
    {
        SentEmails.Add(new EmailCall(
            "BuildFailed",
            recipientEmail,
            $"Build #{build.Id} failed"));

        return Task.CompletedTask;
    }
}

public record EmailCall(string Template, string ToAddress, string Subject);

/// <summary>
/// Mock implementation of IEncryptionService for testing.
/// </summary>
public class MockEncryptionService : IEncryptionService
{
    public string Encrypt(string plaintext)
    {
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"encrypted:{plaintext}"));
    }

    public string Decrypt(string ciphertext)
    {
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext));
        return decoded.Replace("encrypted:", "");
    }
}
