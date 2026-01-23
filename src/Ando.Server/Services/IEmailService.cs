// =============================================================================
// IEmailService.cs
//
// Summary: Interface for email sending operations.
//
// Provides methods for sending build notification emails. Uses Razor templates
// for email content generation.
// =============================================================================

using Ando.Server.Models;

namespace Ando.Server.Services;

/// <summary>
/// Service for sending email notifications.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email with the specified subject and HTML body.
    /// </summary>
    /// <param name="to">Recipient email address.</param>
    /// <param name="subject">Email subject.</param>
    /// <param name="htmlBody">HTML body content.</param>
    Task SendEmailAsync(string to, string subject, string htmlBody);

    /// <summary>
    /// Sends a build failure notification email.
    /// </summary>
    /// <param name="build">The failed build.</param>
    /// <param name="recipientEmail">Email address to send to.</param>
    Task SendBuildFailedEmailAsync(Build build, string recipientEmail);
}
