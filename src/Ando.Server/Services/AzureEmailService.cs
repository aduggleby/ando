// =============================================================================
// AzureEmailService.cs
//
// Summary: Email service implementation using Azure Email Communication Service.
//
// Sends transactional emails via Azure Communication Services.
// Inherits Razor view rendering from BaseEmailService.
//
// Design Decisions:
// - Uses Azure.Communication.Email SDK
// - Connection string from configuration
// - Gracefully skips sending if not configured
// =============================================================================

using Ando.Server.Configuration;
using Azure;
using Azure.Communication.Email;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;

namespace Ando.Server.Services;

/// <summary>
/// Email service implementation using Azure Email Communication Service.
/// </summary>
public class AzureEmailService : BaseEmailService
{
    private readonly EmailClient? _emailClient;

    public AzureEmailService(
        IOptions<EmailSettings> settings,
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider,
        ILogger<AzureEmailService> logger)
        : base(settings, viewEngine, tempDataProvider, serviceProvider, logger)
    {
        var connectionString = Settings.Azure.ConnectionString;
        if (!string.IsNullOrEmpty(connectionString))
        {
            _emailClient = new EmailClient(connectionString);
        }
    }

    /// <inheritdoc />
    public override async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        if (_emailClient == null)
        {
            Logger.LogWarning("Azure Email connection string not configured, skipping email to {To}", to);
            return;
        }

        try
        {
            var emailContent = new EmailContent(subject)
            {
                Html = htmlBody
            };

            var emailMessage = new EmailMessage(
                senderAddress: Settings.FromAddress,
                content: emailContent,
                recipients: new EmailRecipients([new EmailAddress(to)]));

            var operation = await _emailClient.SendAsync(WaitUntil.Started, emailMessage);

            Logger.LogInformation(
                "Sent email via Azure to {To}: {Subject} (OperationId: {OperationId})",
                to, subject, operation.Id);
        }
        catch (RequestFailedException ex)
        {
            Logger.LogError(ex, "Failed to send email via Azure to {To}: {ErrorCode}",
                to, ex.ErrorCode);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending email via Azure to {To}", to);
        }
    }
}
