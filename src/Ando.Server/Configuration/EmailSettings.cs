// =============================================================================
// EmailSettings.cs
//
// Summary: Configuration settings for email service provider selection.
//
// Supports multiple email providers: Resend, Azure Communication Services, SMTP.
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
    /// Azure Email Communication Service.
    /// </summary>
    Azure,

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
    /// Settings for Azure Email Communication Service.
    /// </summary>
    public AzureProviderSettings Azure { get; set; } = new();

    /// <summary>
    /// Settings for SMTP provider.
    /// </summary>
    public SmtpProviderSettings Smtp { get; set; } = new();
}

/// <summary>
/// Resend-specific settings.
/// </summary>
public class ResendProviderSettings
{
    /// <summary>
    /// Resend API key (starts with "re_").
    /// </summary>
    public string ApiKey { get; set; } = "";
}

/// <summary>
/// Azure Email Communication Service settings.
/// </summary>
public class AzureProviderSettings
{
    /// <summary>
    /// Azure Communication Services connection string.
    /// </summary>
    public string ConnectionString { get; set; } = "";
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
