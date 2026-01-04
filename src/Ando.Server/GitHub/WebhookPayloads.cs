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
    [JsonPropertyName("ref")]
    public string Ref { get; init; } = "";

    [JsonPropertyName("before")]
    public string Before { get; init; } = "";

    [JsonPropertyName("after")]
    public string After { get; init; } = "";

    [JsonPropertyName("repository")]
    public RepositoryInfo? Repository { get; init; }

    [JsonPropertyName("pusher")]
    public PusherInfo? Pusher { get; init; }

    [JsonPropertyName("head_commit")]
    public CommitInfo? HeadCommit { get; init; }

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
    [JsonPropertyName("action")]
    public string Action { get; init; } = "";

    [JsonPropertyName("number")]
    public int Number { get; init; }

    [JsonPropertyName("pull_request")]
    public PullRequestInfo? PullRequest { get; init; }

    [JsonPropertyName("repository")]
    public RepositoryInfo? Repository { get; init; }

    [JsonPropertyName("installation")]
    public InstallationInfo? Installation { get; init; }
}

/// <summary>
/// Pull request details.
/// </summary>
public record PullRequestInfo
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("number")]
    public int Number { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("head")]
    public PullRequestHead? Head { get; init; }

    [JsonPropertyName("base")]
    public PullRequestBase? Base { get; init; }

    [JsonPropertyName("user")]
    public UserInfo? User { get; init; }
}

/// <summary>
/// Pull request head (source) branch info.
/// </summary>
public record PullRequestHead
{
    [JsonPropertyName("ref")]
    public string Ref { get; init; } = "";

    [JsonPropertyName("sha")]
    public string Sha { get; init; } = "";

    [JsonPropertyName("repo")]
    public RepositoryInfo? Repo { get; init; }
}

/// <summary>
/// Pull request base (target) branch info.
/// </summary>
public record PullRequestBase
{
    [JsonPropertyName("ref")]
    public string Ref { get; init; } = "";

    [JsonPropertyName("sha")]
    public string Sha { get; init; } = "";
}

/// <summary>
/// Repository information.
/// </summary>
public record RepositoryInfo
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("full_name")]
    public string FullName { get; init; } = "";

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; init; } = "";

    [JsonPropertyName("clone_url")]
    public string CloneUrl { get; init; } = "";

    [JsonPropertyName("default_branch")]
    public string DefaultBranch { get; init; } = "main";

    [JsonPropertyName("owner")]
    public OwnerInfo? Owner { get; init; }
}

/// <summary>
/// Repository owner information.
/// </summary>
public record OwnerInfo
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("login")]
    public string Login { get; init; } = "";
}

/// <summary>
/// Pusher information (who pushed the commit).
/// </summary>
public record PusherInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("email")]
    public string? Email { get; init; }
}

/// <summary>
/// Commit information.
/// </summary>
public record CommitInfo
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";

    [JsonPropertyName("author")]
    public AuthorInfo? Author { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; init; }
}

/// <summary>
/// Commit author information.
/// </summary>
public record AuthorInfo
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("username")]
    public string? Username { get; init; }
}

/// <summary>
/// GitHub App installation information.
/// </summary>
public record InstallationInfo
{
    [JsonPropertyName("id")]
    public long Id { get; init; }
}

/// <summary>
/// User information.
/// </summary>
public record UserInfo
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("login")]
    public string Login { get; init; } = "";
}
