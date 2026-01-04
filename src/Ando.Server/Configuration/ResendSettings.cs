// =============================================================================
// ResendSettings.cs
//
// Summary: Configuration settings for Resend email service.
//
// Resend is used to send email notifications (build failures). The API key
// authenticates with the Resend API, and FromAddress is the sender email.
//
// Design Decisions:
// - Simple configuration since Resend has a straightforward API
// - FromAddress should be a verified domain in Resend
// =============================================================================

namespace Ando.Server.Configuration;

/// <summary>
/// Configuration for Resend email service.
/// </summary>
public class ResendSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Resend";

    /// <summary>
    /// Resend API key (starts with "re_").
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Email address to send from (must be verified domain).
    /// </summary>
    public string FromAddress { get; set; } = "";

    /// <summary>
    /// Display name for the from address.
    /// </summary>
    public string FromName { get; set; } = "Ando CI";
}
