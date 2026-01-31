// =============================================================================
// EmailSettings.cs
//
// Summary: Configuration settings for email service provider selection.
//
// Supports multiple email providers: Resend and SMTP.
// The Provider setting determines which implementation is used at runtime.
//
// Design Decisions:
// - Provider enum for type-safe selection
// - Each provider has its own settings section
// - Common settings (FromAddress, FromName) at root level
// =============================================================================

namespace Ando.Server.Configuration;

/// <summary>
/// Available email service providers.
/// </summary>
public enum EmailProvider
{
    /// <summary>
    /// Resend API (https://resend.com).
    /// </summary>
    Resend,

    /// <summary>
    /// Direct SMTP connection.
    /// </summary>
    Smtp
}

/// <summary>
/// Root configuration for email services.
/// </summary>
public class EmailSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Email";

    /// <summary>
    /// Which email provider to use.
    /// </summary>
    public EmailProvider Provider { get; set; } = EmailProvider.Resend;

    /// <summary>
    /// Email address to send from (must be verified/authorized by provider).
    /// </summary>
    public string FromAddress { get; set; } = "";

    /// <summary>
    /// Display name for the from address.
    /// </summary>
    public string FromName { get; set; } = "Ando CI";

    /// <summary>
    /// Settings for Resend provider.
    /// </summary>
    public ResendProviderSettings Resend { get; set; } = new();

    /// <summary>
    /// Settings for SMTP provider.
    /// </summary>
    public SmtpProviderSettings Smtp { get; set; } = new();
}

/// <summary>
/// Resend-specific settings.
/// Also supports Resend-compatible API providers (e.g., SelfMX) by setting a custom BaseUrl.
/// </summary>
public class ResendProviderSettings
{
    /// <summary>
    /// Resend API key (starts with "re_" for official Resend).
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Base URL for the Resend API. Defaults to official Resend API.
    /// Set to a custom URL for Resend-compatible providers (e.g., "https://api.selfmx.com/").
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.resend.com/";
}

/// <summary>
/// SMTP-specific settings.
/// </summary>
public class SmtpProviderSettings
{
    /// <summary>
    /// SMTP server hostname.
    /// </summary>
    public string Host { get; set; } = "";

    /// <summary>
    /// SMTP server port (typically 587 for TLS, 465 for SSL, 25 for unencrypted).
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// Username for SMTP authentication (leave empty for no auth).
    /// </summary>
    public string Username { get; set; } = "";

    /// <summary>
    /// Password for SMTP authentication.
    /// </summary>
    public string Password { get; set; } = "";

    /// <summary>
    /// Whether to use SSL/TLS encryption.
    /// </summary>
    public bool UseSsl { get; set; } = true;
}
