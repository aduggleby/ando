// =============================================================================
// WebhookPayloads.cs
//
// Summary: Data transfer objects for GitHub webhook payloads.
//
// Contains classes representing the structure of GitHub webhook events.
// Only the fields we need are included; GitHub sends much more data.
//
// Design Decisions:
// - Use records for immutability
// - Nullable properties for optional fields
// - Only include fields we actually use
// =============================================================================

using System.Text.Json.Serialization;

namespace Ando.Server.GitHub;

/// <summary>
/// Push event webhook payload.
/// </summary>
public record PushEventPayload
{
    /// <summary>
    /// Git ref being pushed (e.g., "refs/heads/main").
    /// </summary>
    [JsonPropertyName("ref")]
    public string Ref { get; init; } = "";

    /// <summary>
    /// Commit SHA before the push.
    /// </summary>
    [JsonPropertyName("before")]
    public string Before { get; init; } = "";

    /// <summary>
    /// Commit SHA after the push (the new head).
    /// </summary>
    [JsonPropertyName("after")]
    public string After { get; init; } = "";

    /// <summary>
    /// Repository that received the push.
    /// </summary>
    [JsonPropertyName("repository")]
    public RepositoryInfo? Repository { get; init; }

    /// <summary>
    /// User who pushed the commits.
    /// </summary>
    [JsonPropertyName("pusher")]
    public PusherInfo? Pusher { get; init; }

    /// <summary>
    /// Information about the head commit.
    /// </summary>
    [JsonPropertyName("head_commit")]
    public CommitInfo? HeadCommit { get; init; }

    /// <summary>
    /// GitHub App installation that received this event.
    /// </summary>
    [JsonPropertyName("installation")]
    public InstallationInfo? Installation { get; init; }

    /// <summary>
    /// Gets the branch name from the ref (strips "refs/heads/" prefix).
    /// </summary>
    [JsonIgnore]
    public string Branch => Ref.StartsWith("refs/heads/")
        ? Ref["refs/heads/".Length..]
        : Ref;

    /// <summary>
    /// Gets the commit SHA being pushed.
    /// </summary>
    [JsonIgnore]
    public string CommitSha => After;
}

/// <summary>
/// Pull request event webhook payload.
/// </summary>
public record PullRequestEventPayload
{
    /// <summary>
    /// Action that triggered the event (opened, synchronize, closed, etc.).
    /// </summary>
    [JsonPropertyName("action")]
    public string Action { get; init; } = "";

    /// <summary>
    /// Pull request number.
    /// </summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }

    /// <summary>
    /// Pull request details.
    /// </summary>
    [JsonPropertyName("pull_request")]
    public PullRequestInfo? PullRequest { get; init; }

    /// <summary>
    /// Repository the pull request is against.
    /// </summary>
    [JsonPropertyName("repository")]
    public RepositoryInfo? Repository { get; init; }

    /// <summary>
    /// GitHub App installation that received this event.
    /// </summary>
    [JsonPropertyName("installation")]
    public InstallationInfo? Installation { get; init; }
}

/// <summary>
/// Pull request details.
/// </summary>
public record PullRequestInfo
{
    /// <summary>
    /// GitHub's internal ID for the pull request.
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }

    /// <summary>
    /// Pull request number within the repository.
    /// </summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }

    /// <summary>
    /// Title of the pull request.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    /// <summary>
    /// Head (source) branch of the pull request.
    /// </summary>
    [JsonPropertyName("head")]
    public PullRequestHead? Head { get; init; }

    /// <summary>
    /// Base (target) branch of the pull request.
    /// </summary>
    [JsonPropertyName("base")]
    public PullRequestBase? Base { get; init; }

    /// <summary>
    /// User who opened the pull request.
    /// </summary>
    [JsonPropertyName("user")]
    public UserInfo? User { get; init; }
}

/// <summary>
/// Pull request head (source) branch info.
/// </summary>
public record PullRequestHead
{
    /// <summary>
    /// Branch name of the head (source) branch.
    /// </summary>
    [JsonPropertyName("ref")]
    public string Ref { get; init; } = "";

    /// <summary>
    /// Commit SHA at the head of the source branch.
    /// </summary>
    [JsonPropertyName("sha")]
    public string Sha { get; init; } = "";

    /// <summary>
    /// Repository containing the source branch (for fork PRs).
    /// </summary>
    [JsonPropertyName("repo")]
    public RepositoryInfo? Repo { get; init; }
}

/// <summary>
/// Pull request base (target) branch info.
/// </summary>
public record PullRequestBase
{
    /// <summary>
    /// Branch name of the base (target) branch.
    /// </summary>
    [JsonPropertyName("ref")]
    public string Ref { get; init; } = "";

    /// <summary>
    /// Commit SHA at the base of the target branch.
    /// </summary>
    [JsonPropertyName("sha")]
    public string Sha { get; init; } = "";
}

/// <summary>
/// Repository information.
/// </summary>
public record RepositoryInfo
{
    /// <summary>
    /// GitHub's internal ID for the repository.
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }

    /// <summary>
    /// Repository name (without owner prefix).
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>
    /// Full repository name (owner/repo).
    /// </summary>
    [JsonPropertyName("full_name")]
    public string FullName { get; init; } = "";

    /// <summary>
    /// URL to the repository on GitHub.
    /// </summary>
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; init; } = "";

    /// <summary>
    /// Git clone URL for the repository.
    /// </summary>
    [JsonPropertyName("clone_url")]
    public string CloneUrl { get; init; } = "";

    /// <summary>
    /// Default branch of the repository.
    /// </summary>
    [JsonPropertyName("default_branch")]
    public string DefaultBranch { get; init; } = "main";

    /// <summary>
    /// Owner of the repository.
    /// </summary>
    [JsonPropertyName("owner")]
    public OwnerInfo? Owner { get; init; }
}

/// <summary>
/// Repository owner information.
/// </summary>
public record OwnerInfo
{
    /// <summary>
    /// GitHub's internal ID for the owner.
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }

    /// <summary>
    /// GitHub username or organization name.
    /// </summary>
    [JsonPropertyName("login")]
    public string Login { get; init; } = "";
}

/// <summary>
/// Pusher information (who pushed the commit).
/// </summary>
public record PusherInfo
{
    /// <summary>
    /// Name of the user who pushed.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>
    /// Email of the user who pushed.
    /// </summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

/// <summary>
/// Commit information.
/// </summary>
public record CommitInfo
{
    /// <summary>
    /// Commit SHA.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    /// <summary>
    /// Commit message.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = "";

    /// <summary>
    /// Author of the commit.
    /// </summary>
    [JsonPropertyName("author")]
    public AuthorInfo? Author { get; init; }

    /// <summary>
    /// When the commit was created.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; init; }
}

/// <summary>
/// Commit author information.
/// </summary>
public record AuthorInfo
{
    /// <summary>
    /// Name of the commit author.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>
    /// Email of the commit author.
    /// </summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>
    /// GitHub username of the commit author.
    /// </summary>
    [JsonPropertyName("username")]
    public string? Username { get; init; }
}

/// <summary>
/// GitHub App installation information.
/// </summary>
public record InstallationInfo
{
    /// <summary>
    /// GitHub App installation ID.
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }
}

/// <summary>
/// User information.
/// </summary>
public record UserInfo
{
    /// <summary>
    /// GitHub's internal ID for the user.
    /// </summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }

    /// <summary>
    /// GitHub username.
    /// </summary>
    [JsonPropertyName("login")]
    public string Login { get; init; } = "";
}
