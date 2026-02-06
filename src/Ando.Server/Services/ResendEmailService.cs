// =============================================================================
// ResendEmailService.cs
//
// Summary: Email service implementation using Resend SDK.
//
// Sends transactional emails via the Resend .NET SDK (https://resend.com).
// Inherits Razor view rendering from BaseEmailService.
//
// Design Decisions:
// - Uses official Resend NuGet SDK (IResend) instead of raw HttpClient
// - API key from configuration determines if emails are sent
// - Gracefully skips sending if not configured
// =============================================================================

using Ando.Server.Configuration;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using Resend;

namespace Ando.Server.Services;

/// <summary>
/// Email service implementation using Resend SDK.
/// </summary>
public class ResendEmailService : BaseEmailService
{
    private readonly IResend _resendClient;

    public ResendEmailService(
        IResend resendClient,
        IOptions<EmailSettings> settings,
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider,
        ILogger<ResendEmailService> logger)
        : base(settings, viewEngine, tempDataProvider, serviceProvider, logger)
    {
        _resendClient = resendClient;
    }

    /// <inheritdoc />
    public override async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        var apiKey = Settings.Resend.ApiKey;

        if (string.IsNullOrEmpty(apiKey))
        {
            Logger.LogWarning("Resend API key not configured, skipping email to {To}", to);
            return;
        }

        try
        {
            var message = new EmailMessage
            {
                From = GetFromString(),
                Subject = subject,
                HtmlBody = htmlBody
            };
            message.To.Add(to);

            await _resendClient.EmailSendAsync(message);

            Logger.LogInformation("Sent email via Resend to {To}: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending email via Resend to {To}", to);
        }
    }
}
