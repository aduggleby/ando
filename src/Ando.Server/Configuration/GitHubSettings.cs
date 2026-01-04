// =============================================================================
// GitHubSettings.cs
//
// Summary: Configuration settings for GitHub App integration.
//
// These settings configure the GitHub App used for OAuth login, webhook
// verification, and repository access. The private key is used to generate
// JWTs for GitHub App API authentication.
//
// Design Decisions:
// - Separate ClientId/ClientSecret for OAuth vs AppId/PrivateKey for API
// - PrivateKeyPath allows file-based key storage (more secure than inline)
// - WebhookSecret is required for signature verification
// =============================================================================

namespace Ando.Server.Configuration;

/// <summary>
/// Configuration for GitHub App integration.
/// </summary>
public class GitHubSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "GitHub";

    /// <summary>
    /// GitHub App ID (numeric).
    /// </summary>
    public string AppId { get; set; } = "";

    /// <summary>
    /// GitHub App name (slug).
    /// </summary>
    public string AppName { get; set; } = "";

    /// <summary>
    /// OAuth Client ID for user authentication.
    /// </summary>
    public string ClientId { get; set; } = "";

    /// <summary>
    /// OAuth Client Secret for user authentication.
    /// </summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>
    /// Secret for verifying webhook signatures (HMAC-SHA256).
    /// </summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>
    /// Path to the GitHub App private key PEM file.
    /// Used to generate JWTs for API authentication.
    /// </summary>
    public string PrivateKeyPath { get; set; } = "";
}
