// =============================================================================
// SmtpEmailService.cs
//
// Summary: Email service implementation using direct SMTP.
//
// Sends transactional emails via SMTP using MailKit.
// Inherits Razor view rendering from BaseEmailService.
//
// Design Decisions:
// - Uses MailKit for modern SMTP support (SSL/TLS, authentication)
// - Supports both authenticated and anonymous SMTP
// - Connection pooling not implemented (create connection per email)
// =============================================================================

using Ando.Server.Configuration;
using Ando.Server.Data;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Ando.Server.Services;

/// <summary>
/// Email service implementation using direct SMTP via MailKit.
/// </summary>
public class SmtpEmailService : BaseEmailService
{
    public SmtpEmailService(
        IOptions<EmailSettings> settings,
        IUrlService urlService,
        AndoDbContext db,
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider,
        ILogger<SmtpEmailService> logger)
        : base(settings, urlService, db, viewEngine, tempDataProvider, serviceProvider, logger)
    {
    }

    /// <inheritdoc />
    public override async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        var smtpSettings = Settings.Smtp;

        if (string.IsNullOrEmpty(smtpSettings.Host))
        {
            Logger.LogWarning("SMTP host not configured, skipping email to {To}", to);
            return;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(Settings.FromName, Settings.FromAddress));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = htmlBody
            };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();

            // Determine the secure socket option based on settings
            var secureSocketOptions = smtpSettings.UseSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            // Port 465 typically uses implicit SSL
            if (smtpSettings.Port == 465 && smtpSettings.UseSsl)
            {
                secureSocketOptions = SecureSocketOptions.SslOnConnect;
            }

            await client.ConnectAsync(smtpSettings.Host, smtpSettings.Port, secureSocketOptions);

            // Authenticate if credentials are provided
            if (!string.IsNullOrEmpty(smtpSettings.Username))
            {
                await client.AuthenticateAsync(smtpSettings.Username, smtpSettings.Password);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            Logger.LogInformation("Sent email via SMTP to {To}: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending email via SMTP to {To}", to);
        }
    }
}
