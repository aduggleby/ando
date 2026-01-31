// =============================================================================
// ServerSettings.cs
//
// Summary: General server configuration settings.
//
// Contains settings that affect how the server operates, including the
// public-facing base URL used for generating links in emails.
//
// Design Decisions:
// - BaseUrl is required for containerized deployments behind reverse proxies
// - The container doesn't know its external URL, so it must be configured
// =============================================================================

namespace Ando.Server.Configuration;

/// <summary>
/// General server configuration settings.
/// </summary>
public class ServerSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Server";

    /// <summary>
    /// The public-facing base URL for the server (e.g., "https://ci.example.com").
    /// Used for generating links in emails (verification, password reset, etc.).
    /// Must include scheme (https://) and should not have a trailing slash.
    /// </summary>
    public string BaseUrl { get; set; } = "";
}
